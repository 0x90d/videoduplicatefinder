// /*
//     Copyright (C) 2026 0x90d
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

				// macOS: Homebrew installs the FFmpeg dylibs under its prefix, which a
				// Finder-launched app does not have on its dyld search path. Probe the
				// default Homebrew lib folders explicitly. See issue #764.
				if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
					foreach (var homebrewLib in new[] { "/opt/homebrew/lib", "/usr/local/lib" }) {
						if (CheckForFfmpegLibraryFilesInFolder(homebrewLib))
							return true;
					}
				}


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
				// On Linux, prefer LD_LIBRARY_PATH for shared library discovery; on macOS use DYLD_LIBRARY_PATH;
				// on Windows use PATH. Fall back to PATH if the OS-specific var is unset (e.g. standard Linux distros
				// that rely on the linker cache and don't set LD_LIBRARY_PATH).
				string libPathVarName =
					RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "LD_LIBRARY_PATH" :
					RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "DYLD_LIBRARY_PATH" :
					"PATH";
				var environmentVariables = Environment.GetEnvironmentVariable(libPathVarName)?.Split(Path.PathSeparator);
				if ((environmentVariables == null || environmentVariables.Length == 0) && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					environmentVariables = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
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

		static bool? canLoadNativeLibraries;
		/// <summary>
		/// True only if the FFmpeg shared libraries are present AND can actually be loaded and
		/// called. <see cref="DoFFmpegLibraryFilesExist"/> only checks that the files exist on
		/// disk (File.Exists); it never confirms they load. On some machines the libraries are
		/// present and the right version but still fail to load (missing system dependency,
		/// security software, an ABI/build mismatch), which previously surfaced as a
		/// NotSupportedException on every single decode call instead of one clear failure
		/// (issues #793/#795). Probe one trivial function per library so the load failure is
		/// detected once, up front. Cached for the process — once a native library fails to
		/// load it cannot be reloaded without a restart.
		/// </summary>
		internal static bool CanLoadNativeLibraries {
			get {
				if (canLoadNativeLibraries.HasValue)
					return canLoadNativeLibraries.Value;
				bool ok = false;
				try {
					if (DoFFmpegLibraryFilesExist) {
						// Touch each library. If any cannot be loaded/resolved, AutoGen throws here.
						_ = ffmpeg.avutil_version();
						_ = ffmpeg.avcodec_version();
						_ = ffmpeg.avformat_version();
						_ = ffmpeg.swscale_version();
						_ = ffmpeg.swresample_version();
						ok = true;
					}
				}
				catch (Exception e) {
					Utils.Logger.Instance.Info(
						$"FFmpeg shared libraries are present but could not be loaded; falling back to process mode. " +
						$"Reason: {e.GetType().Name}: {e.Message}. {DescribeExpectedLibraries()}");
					ok = false;
				}
				canLoadNativeLibraries = ok;
				return ok;
			}
		}

		/// <summary>
		/// Builds a diagnostic string listing the FFmpeg shared libraries the current AutoGen
		/// binding expects, plus any mismatched major versions detected on the system. Intended
		/// for error messages so users (particularly on Linux/Docker where the distro-packaged
		/// FFmpeg may be older than the binding) can see the version gap at a glance.
		/// </summary>
		internal static string DescribeExpectedLibraries() {
			var expected = string.Join(", ", GenerateLibraryFileNames());
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return $"Expected: {expected}";

			// On Linux, surface nearby mismatched majors (e.g. libavcodec.so.59 present while .62 expected)
			var found = new List<string>();
			try {
				foreach (var libKey in ffmpeg.LibraryVersionMap.Keys) {
					foreach (var libDir in new[] { "/usr/lib", "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64" }) {
						if (!Directory.Exists(libDir)) continue;
						foreach (var file in Directory.EnumerateFiles(libDir, $"lib{libKey}.so.*", SearchOption.TopDirectoryOnly)) {
							var name = Path.GetFileName(file);
							if (!found.Contains(name)) found.Add(name);
						}
					}
				}
			}
			catch { }
			return found.Count > 0
				? $"Expected: {expected}. Found on system: {string.Join(", ", found)}. The installed FFmpeg major version does not match what the bundled FFmpeg.AutoGen binding expects."
				: $"Expected: {expected}";
		}

		static bool CheckForFfmpegLibraryFilesInFolder(string path) {
			foreach (var file in GenerateLibraryFileNames()) {
				if (!File.Exists(Path.Combine(path, file))) {
					return false;
				}
			}
			ffmpeg.RootPath = path;
			return true;
		}
		
		public static string[] GenerateLibraryFileNames() =>
			ffmpeg.LibraryVersionMap
				.Select(item => {
					if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
						return $"lib{item.Key}.so.{item.Value}";

					if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
						return $"lib{item.Key}.{item.Value}.dylib";

					return $"{item.Key}-{item.Value}.dll";
				}).ToArray();
	}
}
