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
using System.Text.RegularExpressions;
using FFmpeg.AutoGen;
using VDF.Core.Utils;

namespace VDF.Core.FFTools.FFmpegNative {
	/// <summary>
	/// FFmpeg shared libraries the native binding cannot load because they belong to another
	/// FFmpeg major version, for example the FFmpeg 8 a previous VDF downloaded while this one
	/// binds FFmpeg 9.
	/// </summary>
	/// <param name="FoundAvcodecMajor">The libavcodec major found (62 for FFmpeg 8).</param>
	/// <param name="NeededAvcodecMajor">The libavcodec major the binding loads.</param>
	/// <param name="Folder">Where the mismatched libraries are.</param>
	internal sealed record NativeLibraryMismatch(int FoundAvcodecMajor, int NeededAvcodecMajor, string Folder) {
		public string Found => Describe(FoundAvcodecMajor);
		public string Needed => Describe(NeededAvcodecMajor);
		static string Describe(int avcodecMajor) {
			int ffmpegMajor = FfmpegDownloader.MapToFfmpegMajor(avcodecMajor, 0, 0);
			return ffmpegMajor > 0 ? $"FFmpeg {ffmpegMajor}" : $"libavcodec {avcodecMajor}";
		}
	}

	internal static partial class NativeLibraryVersions {
		// Exactly the names the binding loads: avcodec-62.dll, libavcodec.so.62, libavcodec.62.dylib.
		// Unversioned links (libavcodec.so) and full versions (libavcodec.so.62.1.100) are not loaded.
		[GeneratedRegex(@"^(?:(?<lib>av[a-z]+|sw[a-z]+|postproc)-(?<major>\d+)\.dll|lib(?<lib>av[a-z]+|sw[a-z]+|postproc)\.so\.(?<major>\d+)|lib(?<lib>av[a-z]+|sw[a-z]+|postproc)\.(?<major>\d+)\.dylib)$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
		private static partial Regex LibraryFileName();

		/// <summary>Reads an FFmpeg library file name in its Windows, Linux or macOS form.</summary>
		internal static bool TryParseLibraryFileName(string fileName, out string library, out int major) {
			var m = LibraryFileName().Match(fileName);
			if (m.Success && int.TryParse(m.Groups["major"].Value, out major)) {
				library = m.Groups["lib"].Value.ToLowerInvariant();
				return true;
			}
			library = string.Empty;
			major = 0;
			return false;
		}

		/// <summary>
		/// The first of <paramref name="folders"/> holding a libavcodec of another major than
		/// <paramref name="neededAvcodecMajor"/> (the highest one when there are several), or null.
		/// </summary>
		internal static NativeLibraryMismatch? FindMismatch(IEnumerable<string> folders, int neededAvcodecMajor) {
			foreach (string folder in folders.Distinct(StringComparer.Ordinal)) {
				int found = 0;
				try {
					if (!Directory.Exists(folder)) continue;
					foreach (string file in Directory.EnumerateFiles(folder, "*avcodec*")) {
						if (TryParseLibraryFileName(Path.GetFileName(file), out string lib, out int major) &&
							lib == "avcodec" && major != neededAvcodecMajor)
							found = Math.Max(found, major);
					}
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
				if (found > 0)
					return new NativeLibraryMismatch(found, neededAvcodecMajor, folder);
			}
			return null;
		}

		/// <summary>
		/// Native binding wanted but its libraries missing: the FFmpeg of another version it found
		/// instead, so the user can be told why rather than only that something is missing. Null
		/// when the binding's libraries are there or nothing of another version is either.
		/// </summary>
		internal static NativeLibraryMismatch? FindMismatch() =>
			FFmpegHelper.DoFFmpegLibraryFilesExist ? null : FindMismatch(CandidateFolders(), ffmpeg.LibraryVersionMap["avcodec"]);

		/// <summary>Where the binding looks for its libraries, VDF's own folders first.</summary>
		internal static IEnumerable<string> CandidateFolders() {
			string app = CoreUtils.CurrentFolder;
			yield return Path.Combine(app, "bin");
			yield return Path.Combine(app, "lib");
			yield return app;
			string? exeDir = Path.GetDirectoryName(FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) ?? string.Empty);
			if (!string.IsNullOrEmpty(exeDir)) {
				yield return exeDir;
				if (Directory.GetParent(exeDir)?.FullName is { } prefix)
					yield return Path.Combine(prefix, "lib");
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				yield return "/opt/homebrew/lib";
				yield return "/usr/local/lib";
			}
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				foreach (string dir in new[] { "/usr/lib/x86_64-linux-gnu", "/usr/lib/aarch64-linux-gnu", "/usr/lib64", "/usr/lib", "/usr/local/lib" })
					yield return dir;
			}
			string pathVar = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "LD_LIBRARY_PATH"
				: RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "DYLD_LIBRARY_PATH" : "PATH";
			foreach (string dir in (Environment.GetEnvironmentVariable(pathVar) ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
				yield return dir;
		}

		/// <summary>
		/// Deletes FFmpeg libraries of another major than <paramref name="expected"/> lists for them
		/// from <paramref name="folder"/>: after an update the previous download's libraries (about
		/// 100 MB) would otherwise stay next to the new ones for good. Touches only exact library
		/// file names the binding knows; returns what it deleted.
		/// </summary>
		internal static List<string> RemoveOtherMajors(string folder, IReadOnlyDictionary<string, int> expected) {
			var removed = new List<string>();
			if (!Directory.Exists(folder))
				return removed;
			foreach (string file in Directory.EnumerateFiles(folder)) {
				string name = Path.GetFileName(file);
				if (!TryParseLibraryFileName(name, out string lib, out int major) ||
					!expected.TryGetValue(lib, out int wanted) || major == wanted)
					continue;
				try {
					File.Delete(file);
					removed.Add(name);
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
					Logger.Instance.Warn($"Could not remove the outdated FFmpeg library {file}: {e.Message}");
				}
			}
			return removed;
		}
	}
}
