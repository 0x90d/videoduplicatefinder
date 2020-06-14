using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DuplicateFinderEngine {
	public static class Utils {
		public static string FFprobeExecutableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";
		public static string FFmpegExecutableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
		private static readonly string[] suf = { " B", " KB", " MB", " GB", " TB", " PB", " EB" };

		private static string _ffmpegDirectory;

		/// <summary>
		/// Returns the full path to the ffmpeg executable.
		/// </summary>
		public static string FfmpegPath
		{
			get 
			{
				if (_ffmpegDirectory == null)
				{
					_ffmpegDirectory = FindFfmpegDirectory();
				}

				return Path.Combine(_ffmpegDirectory, FFmpegExecutableName);
			}
		}

		/// <summary>
		/// Returns the full path of the ffprobe executable.
		/// </summary>
		public static string FfprobePath
		{
			get
			{
				if (_ffmpegDirectory == null) 
				{
					_ffmpegDirectory = FindFfmpegDirectory();
				}

				return Path.Combine(_ffmpegDirectory, FFprobeExecutableName);
			}
		}

		/// <summary>
		/// Returns true if both ffmpeg and ffprobe can be found.
		/// </summary>
		public static bool FfFilesExist
		{
			get
			{
				return File.Exists(FfmpegPath) && File.Exists(FfprobePath);
			}
		}

		/// <summary>
		/// Trims milliseconds from a timespan making it better compare-able against another timespan
		/// </summary>
		/// <param name="ts"></param>
		/// <returns></returns>
		public static TimeSpan TrimMiliseconds(this TimeSpan ts) => new TimeSpan(ts.Days, ts.Hours, ts.Minutes, ts.Seconds);
		/// <summary>
		/// Formats byte length to a human readable format
		/// </summary>
		/// <param name="byteCount"></param>
		/// <returns></returns>
		public static string BytesToString(long byteCount) {
			if (byteCount == 0)
				return "0" + suf[0];
			var bytes = Math.Abs(byteCount);
			var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
			var num = Math.Round(bytes / Math.Pow(1024, place), 1);
			return (Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture) + suf[place];
		}

		private static readonly Random getrandom = new Random();
		/// <summary>
		/// Returns a random number
		/// </summary>
		/// <param name="min"></param>
		/// <param name="max"></param>
		/// <returns></returns>
		public static int GetRandomNumber(int min = 0, int max = 500000) => getrandom.Next(min, max);

		/// <summary>
		/// Get safe path on all systems ignoring slashes
		/// </summary>
		/// <param name="path1"></param>
		/// <param name="path2"></param>
		/// <returns></returns>
		public static string SafePathCombine(string path1, string path2) {
			if (!Path.IsPathRooted(path2))
				Path.Combine(path1, path2);

			path2 = path2.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return Path.Combine(path1, path2);
		}

		/// <summary>
		/// Tries to find the location of an ffmpeg executable.
		/// Searches next to the executable, inside the <executable>\bin directory and in PATH.
		/// </summary>
		/// <returns>Full path to the ffmpeg executable.</returns>
		private static string FindFfmpegDirectory()
		{
			Logger.Instance.Info("OS: " + RuntimeInformation.OSDescription);
			var currentDir = Path.GetDirectoryName(typeof(FFmpegWrapper.FFmpegWrapper).Assembly.Location);

			// start discovery
			if (File.Exists(Path.Combine(currentDir, FFmpegExecutableName)))
			{
				// it's next to FFmpegWrapper.dll
				return currentDir;
			}

			if (File.Exists(Path.Combine(currentDir, "bin", FFmpegExecutableName)))
			{
				// it's in \bin
				return Path.Combine(currentDir, "bin");
			}

			// try all directories in PATH
			var pathsEnv = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
			if (pathsEnv == null)
			{
				// if PATH doesn't exist, return null
				return null;
			}
			else
			{
				// otherwise check each directory
				foreach (var path in pathsEnv)
				{
					if (!Directory.Exists(path))
					{
						// if the directory doesn't even exist, continue on the next foreach item
						continue;
					}

					try
					{
						// otherwise get all files in the directory
						var files = new DirectoryInfo(path).GetFiles();

						// get the first file whose name starts with FFmpegExecutableName
						var result = files.FirstOrDefault(x => x.Name.StartsWith(FFmpegExecutableName, true, CultureInfo.InvariantCulture));
						if (result != null)
						{
							// return its directory
							return result.Directory.ToString();
						}
					}
					catch (Exception e)
					{
						Console.WriteLine(e);
					}
				}

				// didn't find anything in PATH either
				return null;
			}
		}
	}
}
