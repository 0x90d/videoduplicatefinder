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

namespace VDF.Core.Utils {

	/// <summary>
	/// Crash breadcrumbs for the phases that decode media in-process: the media-analysis
	/// phase and the partial-clip visual gate. A native crash (e.g. an access violation
	/// inside an FFmpeg library on a corrupt file, issue #861, or inside a GPU driver,
	/// issue #863) kills the process with no managed error path: nothing gets flagged, so
	/// the next scan re-attempts the same file and dies at the same point, forever.
	///
	/// Each worker thread writes a tiny per-thread breadcrumb file naming the media file it
	/// is about to process and blanks it when the file completes. After a hard crash the
	/// non-empty breadcrumbs identify the files that were in flight; the next scan flags
	/// them like a completed failure (so they are skipped, recoverable via the
	/// "always retry failed files" setting) instead of crashing on them again.
	///
	/// Everything here is best-effort: breadcrumb I/O must never fail a scan. Writes rely on
	/// the OS page cache surviving a process crash — no fsync needed (only power loss would
	/// lose them, and then a lost breadcrumb merely means one more crash-and-learn cycle).
	/// </summary>
	internal static class ScanCrashJournal {
		internal const string PhaseSampling = "sampling";
		internal const string PhaseAudio = "audio";
		internal const string PhaseImage = "image";
		internal const string PhasePartialVerify = "partialverify";
		const string FilePrefix = "scan-inflight-";

		static string? journalFolder;
		static volatile bool shutdownCleared;
		static int processExitHookRegistered;
		[ThreadStatic] static string? threadFile;

		internal readonly record struct Suspect(string Phase, string Path);

		/// <summary>Sets the folder breadcrumbs live in (the database folder). While null, Begin/End/Collect are no-ops.</summary>
		internal static void Initialize(string? folder) {
			journalFolder = folder;
			shutdownCleared = false;
			// Covers every frontend (GUI window close, CLI Ctrl+C, Web SIGTERM) without
			// per-frontend wiring. A native access violation never runs ProcessExit —
			// its breadcrumbs survive, which is the entire crash signal.
			if (folder != null && Interlocked.Exchange(ref processExitHookRegistered, 1) == 0)
				AppDomain.CurrentDomain.ProcessExit += (_, _) => ClearOnCleanShutdown();
		}

		/// <summary>
		/// Graceful-shutdown hook: a process that gets here did NOT die inside native code,
		/// so any in-flight breadcrumbs are innocent (the user closed the app or aborted the
		/// scan mid-file) and must not be quarantined at the next scan.
		/// </summary>
		internal static void ClearOnCleanShutdown() {
			shutdownCleared = true; // stop new Begin() writes first, then blank what exists
			string? folder = journalFolder;
			if (folder == null)
				return;
			try {
				foreach (string file in Directory.EnumerateFiles(folder, FilePrefix + "*.txt")) {
					try {
						File.WriteAllText(file, string.Empty);
					}
					catch (Exception) { }
				}
			}
			catch (Exception) { }
		}

		/// <summary>Records that this thread is about to process <paramref name="mediaFile"/>.</summary>
		internal static void Begin(string phase, string mediaFile) {
			string? folder = journalFolder;
			if (folder == null || shutdownCleared)
				return;
			try {
				// Not cached across calls: the database folder can change between scans.
				threadFile = FileUtils.SafePathCombine(folder, $"{FilePrefix}{Environment.CurrentManagedThreadId}.txt");
				File.WriteAllText(threadFile, FormatLine(phase, mediaFile));
			}
			catch (Exception) {
				threadFile = null;
			}
		}

		/// <summary>Marks this thread's current file as completed (crash-innocent).</summary>
		internal static void End() {
			string? file = threadFile;
			if (file == null)
				return;
			try {
				File.WriteAllText(file, string.Empty);
			}
			catch (Exception) { }
		}

		internal static string FormatLine(string phase, string path) => phase + "|" + path;

		internal static bool TryParseLine(string line, out string phase, out string path) {
			phase = string.Empty;
			path = string.Empty;
			int sep = line.IndexOf('|');
			if (sep <= 0 || sep == line.Length - 1)
				return false;
			phase = line[..sep];
			path = line[(sep + 1)..];
			return true;
		}

		/// <summary>
		/// Returns the files a previous (crashed) session left in flight and removes all
		/// breadcrumb files. Call once per scan, after the database folder is known and
		/// before any worker starts writing new breadcrumbs.
		/// </summary>
		internal static List<Suspect> CollectLeftovers() {
			var result = new List<Suspect>();
			string? folder = journalFolder;
			if (folder == null || !Directory.Exists(folder))
				return result;
			try {
				foreach (string file in Directory.EnumerateFiles(folder, FilePrefix + "*.txt")) {
					try {
						string content = File.ReadAllText(file).Trim();
						if (content.Length > 0 && TryParseLine(content, out string phase, out string path))
							result.Add(new Suspect(phase, path));
					}
					catch (Exception) { }
					try {
						File.Delete(file);
					}
					catch (Exception) { }
				}
			}
			catch (Exception) { }
			return result;
		}
	}
}
