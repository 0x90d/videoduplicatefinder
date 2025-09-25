// /*
//     Copyright (C) 2025 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System.Linq;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using FFmpeg.AutoGen.Native;

namespace VDF.Core.FFTools.FFmpegNative {
	static class FFmpegHelper {
		static readonly LinuxFunctionResolver linuxFunctionResolver = new();
		static readonly WindowsFunctionResolver windowsFunctionResolver = new();
		static readonly MacFunctionResolver macFunctionResolver = new();

		private static bool ffmpegLibraryFound;
		public static unsafe string? Av_strerror(int error) {
			const int bufferSize = 1024;
			byte* buffer = stackalloc byte[bufferSize];
			ffmpeg.av_strerror(error, buffer, bufferSize).ThrowExceptionIfError();
			string? message = Marshal.PtrToStringAnsi((IntPtr)buffer);
			return message;
		}

		public static int ThrowExceptionIfError(this int error) {
			if (error < 0)
				throw new FFInvalidExitCodeException(Av_strerror(error) ?? "Unknown error");
			return error;
		}

		private static bool FindFFmpegLibraryFiles() {
			try {

				string? path = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg);
				if (path != null) {

					if (CheckForFfmpegLibraryFilesInFolder(Path.GetDirectoryName(path)!))
						return true;
					if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
						if (CheckForFfmpegLibraryFilesInFolder(Path.Combine(Directory.GetParent(Directory.GetParent(path)!.FullName)!.FullName, "lib")))
							return true;
					}

				}
				else if (path == null) {
					//Case where ffmpeg(.exe) does not exist but libraries files could exist
					path = Path.Combine(Utils.CoreUtils.CurrentFolder, "bin");
					if (CheckForFfmpegLibraryFilesInFolder(path))
						return true;
				}

				path = Utils.CoreUtils.CurrentFolder;
				if (CheckForFfmpegLibraryFilesInFolder(path))
					return true;


				//Try fast lookup first, credits: @Maltragor
				try {
					ffmpeg.RootPath = string.Empty;
					foreach (KeyValuePair<string, int> item in ffmpeg.LibraryVersionMap) {
						if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
							windowsFunctionResolver.GetOrLoadLibrary(item.Key, throwOnError: true);
						else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
							linuxFunctionResolver.GetOrLoadLibrary(item.Key, throwOnError: true);
						else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
							macFunctionResolver.GetOrLoadLibrary(item.Key, throwOnError: true);
					}
					return true;
				}
				catch { }

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
					string firstLibrary = $"lib{ffmpeg.LibraryVersionMap.Keys.First()}.so.{ffmpeg.LibraryVersionMap.Values.First()}";
					foreach (var libDir in Directory.EnumerateDirectories("/usr/", "lib*", new EnumerationOptions {
						IgnoreInaccessible = true,
						RecurseSubdirectories = false
					})) {

						foreach (string file in Directory.EnumerateFiles(libDir, firstLibrary, new EnumerationOptions {
							IgnoreInaccessible = true,
							RecurseSubdirectories = true,
							MaxRecursionDepth = 2,
						})) {
							string currentDirectory = Path.GetDirectoryName(file)!;
							if (CheckForFfmpegLibraryFilesInFolder(currentDirectory))
								return true;
						}
					}
				}
				var environmentVariables = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
				if (environmentVariables == null)
					return false;

				foreach (var environmentPath in environmentVariables) {
					if (!Directory.Exists(environmentPath))
						continue;
					if (CheckForFfmpegLibraryFilesInFolder(environmentPath))
						return true;
				}
			}
			catch (Exception e) {
				Utils.Logger.Instance.Info($"Failed to look for ffmpeg libraries: {e}");
			}
			return false;
		}

		internal static bool DoFFmpegLibraryFilesExist {
			get {
				if (ffmpegLibraryFound)
					return true;
				ffmpegLibraryFound = FindFFmpegLibraryFiles();
				return ffmpegLibraryFound;
			}
		}

		static bool CheckForFfmpegLibraryFilesInFolder(string path) {

			foreach (KeyValuePair<string, int> item in ffmpeg.LibraryVersionMap) {
				string libraryName =
					RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ?
						Path.Combine(path, $"lib{item.Key}.so.{item.Value}") :
					RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ?
						Path.Combine(path, $"lib{item.Key}.{item.Value}.dylib") :
					Path.Combine(path, $"{item.Key}-{item.Value}.dll");

				if (!File.Exists(libraryName))
					return false;
			}
			ffmpeg.RootPath = path;
			return true;

		}
	}
}
