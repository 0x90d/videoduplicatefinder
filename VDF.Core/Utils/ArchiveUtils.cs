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

using System.Diagnostics;
using System.IO.Compression;

namespace VDF.Core.Utils {
	internal enum ArchiveKind {
		Zip,
		TarGz,
		TarXz
	}

	/// <summary>Archive extraction shared by the AI component and FFmpeg downloaders.</summary>
	internal static class ArchiveUtils {
		internal static void Extract(string archivePath, string targetFolder, ArchiveKind kind) {
			if (kind == ArchiveKind.Zip) {
				// .NET's extractor already rejects zip-slip entries that would land
				// outside the target directory.
				ZipFile.ExtractToDirectory(archivePath, targetFolder, overwriteFiles: true);
				return;
			}

			// Tar archives are only downloaded on Linux/macOS, where tar is part of the
			// base system.
			var psi = new ProcessStartInfo {
				FileName = "tar",
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardError = true,
			};
			psi.ArgumentList.Add(kind == ArchiveKind.TarXz ? "-xJf" : "-xzf");
			psi.ArgumentList.Add(archivePath);
			psi.ArgumentList.Add("-C");
			psi.ArgumentList.Add(targetFolder);
			// NOTE: do NOT pass --no-absolute-filenames. It is not a valid GNU tar option
			// (rejected even by GNU tar 1.35) and is absent from BSD/busybox tar, so it
			// aborted extraction on Linux/macOS with "tar: unrecognized option" (issue #788).
			// tar already strips leading '/'s by default, and the archive is checksum-verified.

			using Process process = Process.Start(psi) ?? throw new IOException("Failed to start tar.");
			// Drain stderr before waiting, or a chatty tar can fill the pipe and deadlock.
			string error = process.StandardError.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
				throw new IOException(string.IsNullOrWhiteSpace(error) ? "Failed to extract archive." : error);
		}
	}
}
