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

using System.Globalization;
using System.Linq;
using System.Text;
using VDF.Core.FFTools;
using VDF.Core.Utils;

namespace VDF.Core {
	// Single-pair diagnostic: runs the exact same gates a real scan applies to two
	// user-picked files and produces a human-readable report explaining at which
	// step the pair passes or fails. Intended for the "why doesn't VDF detect these
	// two obvious duplicates?" support question — the report is deliberately written
	// in plain English so users can paste it into a GitHub issue.
	public sealed partial class ScanEngine {

		/// <summary>
		/// Compares two files using the current <see cref="Settings"/> and returns a
		/// step-by-step report of every detection gate (scan inclusion, media info,
		/// frame sampling, duration tolerance, folder matching, visual similarity,
		/// hard links and partial clip detection) with the reason for the final verdict.
		/// Does not read or modify the scan database.
		/// </summary>
		public Task<string> TestFilePairAsync(string fileA, string fileB) =>
			Task.Run(() => TestFilePair(fileA, fileB));

		string TestFilePair(string fileA, string fileB) {
			var inv = CultureInfo.InvariantCulture;
			StringBuilder sb = new();
			List<string> failures = new();
			List<string> scanInclusionIssues = new();
			List<string> hints = new();
			// This standalone pair test computes requiredMatches locally; make sure it never
			// reads a value cached by a previous (possibly aborted) scan phase.
			matchingRequiredSampleMatches = null;

			string FormatSimilarity(float similarity) =>
				float.IsNaN(similarity) ? "NaN" : (similarity * 100f).ToString("0.0#", inv) + "%";
			string FormatSeconds(double s) => s.ToString("0.0##", inv) + "s";

			sb.AppendLine("=== Duplicate Detection Test ===");
			sb.AppendLine($"File A: {fileA}");
			sb.AppendLine($"File B: {fileB}");
			sb.AppendLine();

			if (isScanning)
				return "A scan is currently running. Stop or finish the scan before running the file pair test.";

			// Same FFmpeg/FFprobe availability requirements as PrepareSearch()
			if (!Settings.UseNativeFfmpegBinding && !FFmpegExists)
				return "Cannot run test: FFmpeg was not found.";
			if (!FFprobeExists)
				return "Cannot run test: FFprobe was not found.";
			if (Settings.UseNativeFfmpegBinding && !FFTools.FFmpegNative.FFmpegHelper.DoFFmpegLibraryFilesExist)
				return $"Cannot run test: FFmpeg libraries were not found. {FFTools.FFmpegNative.FFmpegHelper.DescribeExpectedLibraries()}";

			// PrepareSearch() applies these statics at scan start; the test has to do
			// the same so sampling runs with the configured acceleration/arguments.
			FfmpegEngine.HardwareAccelerationMode = Settings.HardwareAccelerationMode;
			FfmpegEngine.CustomFFArguments = Settings.CustomFFArguments;
			FfmpegEngine.UseNativeBinding = Settings.UseNativeFfmpegBinding;

			// ── File checks ─────────────────────────────────────────────────
			sb.AppendLine("--- File checks ---");
			FileEntry?[] entries = new FileEntry?[2];
			string[] paths = { fileA, fileB };
			string[] labels = { "A", "B" };
			for (int i = 0; i < 2; i++) {
				if (string.IsNullOrWhiteSpace(paths[i]))
					return $"File {labels[i]}: no file selected.";
				if (!File.Exists(paths[i]))
					return $"File {labels[i]}: file does not exist: {paths[i]}";

				string extension = Path.GetExtension(paths[i]);
				if (!FileUtils.IsMediaExtension(extension)) {
					sb.AppendLine($"{labels[i]}: FAIL — extension '{extension}' is not a recognized video or image type. A scan will never pick this file up.");
					failures.Add($"File {labels[i]} has an unsupported file extension ('{extension}').");
					continue;
				}

				try {
					entries[i] = new FileEntry(paths[i]);
				}
				catch (Exception e) {
					return $"File {labels[i]}: could not be read: {e.Message}";
				}
				FileEntry entry = entries[i]!;
				string kind = entry.IsImage ? "image" : "video";
				sb.AppendLine($"{labels[i]}: {kind}, {(entry.FileSize / 1024d / 1024d).ToString("0.0#", inv)} MB");
			}
			if (entries[0] == null || entries[1] == null) {
				AppendVerdict(sb, false, failures, hints, scanInclusionIssues, null);
				return sb.ToString();
			}
			FileEntry a = entries[0]!, b = entries[1]!;
			if (string.Equals(a.Path, b.Path, CoreUtils.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
				return "Both paths point to the same file — pick two different files.";
			sb.AppendLine();

			// ── Scan inclusion ──────────────────────────────────────────────
			// Whether a real scan with the current settings would process these files at
			// all. Failing here doesn't abort the test — similarity is still computed —
			// but the verdict points it out, because "the file is never scanned" is the
			// most common reason for 'missing' duplicates.
			sb.AppendLine("--- Scan inclusion (current settings) ---");
			for (int i = 0; i < 2; i++) {
				FileEntry entry = entries[i]!;
				if (InvalidEntry(entry, out _, out string? reason) && reason != null) {
					sb.AppendLine($"{labels[i]}: WARNING — a real scan would skip this file: {reason}.");
					scanInclusionIssues.Add($"File {labels[i]} would be skipped by a scan: {reason}.");
				}
				else {
					sb.AppendLine($"{labels[i]}: OK — file would be included in a scan.");
				}
				entry.invalid = false;
			}

			// Stored database entry may carry flags that exclude the file from scans
			// even though the file itself is fine.
			if (DatabaseUtils.Database.Count > 0) {
				for (int i = 0; i < 2; i++) {
					if (!DatabaseUtils.Database.TryGetValue(entries[i]!, out var dbEntry))
						continue;
					if (dbEntry.Flags.Has(EntryFlags.ManuallyExcluded)) {
						sb.AppendLine($"{labels[i]}: WARNING — this file was manually excluded in a previous session and is skipped by scans.");
						scanInclusionIssues.Add($"File {labels[i]} was manually excluded from scans. Clean or clear the database to undo this.");
					}
					if (dbEntry.Flags.Has(EntryFlags.ThumbnailError) && !Settings.AlwaysRetryFailedSampling) {
						sb.AppendLine($"{labels[i]}: WARNING — thumbnail sampling failed for this file in a previous scan and retry is disabled ('Always retry failed sampling').");
						scanInclusionIssues.Add($"File {labels[i]} previously failed sampling and is skipped. Enable 'Always retry failed sampling' and rescan.");
					}
				}
			}
			sb.AppendLine();

			// ── Image vs video gate ─────────────────────────────────────────
			if (a.IsImage != b.IsImage) {
				sb.AppendLine("--- File type ---");
				sb.AppendLine($"A is {(a.IsImage ? "an image" : "a video")}, B is {(b.IsImage ? "an image" : "a video")}.");
				sb.AppendLine("FAIL — images are only compared with other images and videos only with other videos. This pair is never compared.");
				failures.Add("One file is an image and the other is a video — such pairs are never compared.");
				AppendVerdict(sb, false, failures, hints, scanInclusionIssues, null);
				return sb.ToString();
			}

			// ── Media info & frame sampling ─────────────────────────────────
			sb.AppendLine("--- Media info & frame sampling ---");
			List<float> positions = new();
			float positionCounter = 0f;
			for (int i = 0; i < Settings.ThumbnailCount; i++) {
				positionCounter += 1.0F / (Settings.ThumbnailCount + 1);
				positions.Add(positionCounter);
			}
			if (!a.IsImage)
				sb.AppendLine($"Sampling {Settings.ThumbnailCount} frame(s) per video (settings: thumbnail count){(Settings.MaxSamplingDurationSeconds > 0 ? $", limited to the first {FormatSeconds(Settings.MaxSamplingDurationSeconds)}" : "")}.");

			for (int i = 0; i < 2; i++) {
				FileEntry entry = entries[i]!;
				if (entry.IsImage) {
					if (!GetGrayBytesFromImage(entry, Settings.UseExifCreationDate, Settings.ExtendedFFToolsLogging)) {
						if (entry.Flags.Has(EntryFlags.TooDark)) {
							sb.AppendLine($"{labels[i]}: FAIL — more than 80% of the pixels are nearly black. The file is flagged 'too dark' and excluded from comparisons.");
							failures.Add($"File {labels[i]} is too dark to compare (mostly black pixels).");
						}
						else {
							sb.AppendLine($"{labels[i]}: FAIL — FFmpeg could not decode this image.");
							failures.Add($"File {labels[i]} could not be decoded.");
						}
						continue;
					}
					var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s => s.Width > 0 && s.Height > 0);
					sb.AppendLine($"{labels[i]}: decoded OK{(stream != null ? $", {stream.Width}x{stream.Height}" : "")}");
				}
				else {
					MediaInfo? info = FFProbeEngine.GetMediaInfo(entry.Path, Settings.ExtendedFFToolsLogging);
					if (info == null) {
						sb.AppendLine($"{labels[i]}: FAIL — FFprobe could not read the media information. The file is skipped by scans (metadata error).");
						failures.Add($"File {labels[i]} has unreadable media information.");
						continue;
					}
					entry.mediaInfo = info;
					var stream = info.Streams?.FirstOrDefault(s => s.Width > 0 && s.Height > 0);
					sb.AppendLine($"{labels[i]}: duration {info.Duration.ToString(@"hh\:mm\:ss\.f", inv)} ({FormatSeconds(info.Duration.TotalSeconds)}){(stream != null ? $", {stream.Width}x{stream.Height}, {stream.CodecName}" : "")}");

					if (!FfmpegEngine.GetGrayBytesFromVideo(entry, positions, Settings.MaxSamplingDurationSeconds, Settings.ExtendedFFToolsLogging)) {
						if (entry.Flags.Has(EntryFlags.TooDark)) {
							sb.AppendLine($"{labels[i]}: FAIL — every sampled frame is nearly black. The file is flagged 'too dark' and excluded from comparisons.");
							failures.Add($"File {labels[i]} is too dark to compare (all sampled frames are mostly black). Sampling at different positions (thumbnail count) or limiting the sampling duration may help.");
						}
						else {
							sb.AppendLine($"{labels[i]}: FAIL — frame sampling failed (FFmpeg could not decode frames from this file).");
							failures.Add($"File {labels[i]} could not be sampled.");
						}
					}
					else {
						var sampledAt = positions.Select(p => FormatSeconds(entry.GetGrayBytesIndex(p, Settings.MaxSamplingDurationSeconds)));
						sb.AppendLine($"{labels[i]}: sampled at {string.Join(", ", sampledAt)}");
					}
				}
			}
			sb.AppendLine();

			// Build the same per-entry compare snapshots a real scan materializes.
			bool snapshotsOk = true;
			foreach (FileEntry entry in new[] { a, b }) {
				if (entry.IsImage) {
					if (!entry.grayBytes.TryGetValue(0, out byte[]? imageGray) || imageGray == null) {
						snapshotsOk = false;
						continue;
					}
					entry.compareGray = new[] { imageGray };
				}
				else {
					var gray = new byte[]?[positions.Count];
					bool complete = entry.mediaInfo != null;
					for (int j = 0; complete && j < positions.Count; j++) {
						double idx = entry.GetGrayBytesIndex(positions[j], Settings.MaxSamplingDurationSeconds);
						if (!entry.grayBytes.TryGetValue(idx, out byte[]? data) || data == null)
							complete = false;
						else
							gray[j] = data;
					}
					if (!complete) {
						snapshotsOk = false;
						continue;
					}
					entry.compareGray = gray;
					if (Settings.UsePHashing)
						entry.comparePHashes = ComputePHashesFromGray(gray);
				}
			}
			if (!snapshotsOk) {
				AppendVerdict(sb, false, failures, hints, scanInclusionIssues, null);
				a.compareGray = b.compareGray = null;
				a.comparePHashes = b.comparePHashes = null;
				return sb.ToString();
			}

			// ── Duration gate (videos only) ─────────────────────────────────
			bool durationPassed = true;
			if (!a.IsImage) {
				sb.AppendLine("--- Duration check ---");
				double durationA = a.mediaInfo!.Duration.TotalSeconds;
				double durationB = b.mediaInfo!.Duration.TotalSeconds;
				double allowedSeconds = Math.Min(Settings.GetDurationToleranceSeconds(durationA), Settings.GetDurationToleranceSeconds(durationB));
				double diffSeconds = Math.Abs(durationA - durationB);
				durationPassed = diffSeconds <= allowedSeconds;
				sb.AppendLine($"Difference: {FormatSeconds(diffSeconds)}, allowed: {FormatSeconds(allowedSeconds)} (duration tolerance: {Settings.PercentDurationDifference.ToString("0.#", inv)}%{(Settings.DurationDifferenceMinSeconds > 0 ? $", min {FormatSeconds(Settings.DurationDifferenceMinSeconds)}" : "")}{(Settings.DurationDifferenceMaxSeconds > 0 ? $", max {FormatSeconds(Settings.DurationDifferenceMaxSeconds)}" : "")})");
				if (durationPassed)
					sb.AppendLine("PASS — durations are close enough, the pair is compared.");
				else {
					sb.AppendLine("FAIL — the duration difference exceeds the tolerance. The files are never visually compared.");
					failures.Add($"The durations differ by {FormatSeconds(diffSeconds)} but only {FormatSeconds(allowedSeconds)} is allowed.");
					hints.Add($"Increase the duration difference tolerance (currently {Settings.PercentDurationDifference.ToString("0.#", inv)}%) so that {FormatSeconds(diffSeconds)} falls within the allowed range{(Settings.EnablePartialClipDetection ? "" : ", or enable partial clip detection if one video is a cut-down version of the other")}.");
				}
				sb.AppendLine();
			}

			// ── Folder matching gate ────────────────────────────────────────
			bool folderPassed = true;
			if (Settings.FolderMatchMode != FolderMatchMode.None) {
				sb.AppendLine("--- Folder matching ---");
				bool sameFolder = SameFolderAtDepth(a.Folder, b.Folder, Settings.SameFolderDepth);
				if (Settings.FolderMatchMode == FolderMatchMode.SameFolderOnly && !sameFolder) {
					folderPassed = false;
					sb.AppendLine($"FAIL — folder matching is set to 'same folder only' (depth {Settings.SameFolderDepth}) but the files are in different folders. The pair is never compared.");
					failures.Add("Folder matching is set to 'same folder only' but the files are in different folders.");
					hints.Add("Set folder matching back to 'compare across all folders'.");
				}
				else if (Settings.FolderMatchMode == FolderMatchMode.DifferentFolderOnly && sameFolder) {
					folderPassed = false;
					sb.AppendLine($"FAIL — folder matching is set to 'different folders only' (depth {Settings.SameFolderDepth}) but the files are in the same folder. The pair is never compared.");
					failures.Add("Folder matching is set to 'different folders only' but the files are in the same folder.");
					hints.Add("Set folder matching back to 'compare across all folders'.");
				}
				else {
					sb.AppendLine($"PASS — folder matching mode '{Settings.FolderMatchMode}' allows this pair.");
				}
				sb.AppendLine();
			}

			// ── Visual similarity ───────────────────────────────────────────
			sb.AppendLine("--- Visual similarity ---");
			bool usePHash = !a.IsImage && Settings.UsePHashing;
			sb.AppendLine($"Method: {(a.IsImage ? "32x32 grayscale (single image)" : usePHash ? $"perceptual hash (pHash), quorum over {positions.Count} frame(s)" : $"32x32 grayscale, averaged over {positions.Count} frame(s)")}{(Settings.IgnoreBlackPixels || Settings.IgnoreWhitePixels ? $" — ignoring {(Settings.IgnoreBlackPixels && Settings.IgnoreWhitePixels ? "black and white" : Settings.IgnoreBlackPixels ? "black" : "white")} pixels" : "")}");

			bool isDuplicate = CheckIfDuplicate(a, null, null, b, out float difference);
			float similarity = 1f - difference;

			// Per-frame breakdown for the grayscale path. CheckIfDuplicate exits early
			// once the accumulated difference exceeds the limit and then reports no
			// meaningful difference value, so the displayed similarity is recomputed
			// here from all frames (the pass/fail verdict above is authoritative).
			if (!a.IsImage && !usePHash) {
				float diffSum = 0f;
				for (int j = 0; j < positions.Count; j++) {
					float frameDiff = Settings.IgnoreBlackPixels || Settings.IgnoreWhitePixels ?
						GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(a.compareGray![j]!, b.compareGray![j]!, Settings.IgnoreBlackPixels, Settings.IgnoreWhitePixels) :
						GrayBytesUtils.PercentageDifference(a.compareGray![j]!, b.compareGray![j]!);
					diffSum += frameDiff;
					if (positions.Count > 1)
						sb.AppendLine($"Frame {j + 1}: {FormatSimilarity(1f - frameDiff)}");
				}
				similarity = 1f - diffSum / positions.Count;
			}
			if (usePHash && (a.comparePHashes == null || b.comparePHashes == null)) {
				sb.AppendLine("FAIL — the pHash could not be computed for at least one file; in pHash mode such files never match.");
				failures.Add("pHash data could not be computed for at least one file.");
			}
			else if (usePHash) {
				// Per-frame breakdown mirroring the quorum rule in CheckIfDuplicate. Recompute
				// the headline similarity from every frame here too: CheckIfDuplicate reports 1f
				// (0% similarity) on a non-match and a matched-only figure would still disagree
				// with the frames listed below, so the summary is averaged over all frames to
				// match the breakdown (the pass/fail verdict above is authoritative).
				int passCount = 0;
				float simSum = 0f;
				for (int j = 0; j < positions.Count; j++) {
					bool framePass = pHash.PHashCompare.IsDuplicateByPercent(a.comparePHashes![j], b.comparePHashes![j], out float frameSimilarity, Settings.Percent / 100f, strict: true);
					if (framePass) passCount++;
					simSum += frameSimilarity;
					if (positions.Count > 1)
						sb.AppendLine($"Frame {j + 1}: {FormatSimilarity(frameSimilarity)}{(framePass ? "" : " (below threshold)")}");
				}
				similarity = positions.Count > 0 ? simSum / positions.Count : 0f;
				int requiredMatches = Math.Max(1, (int)Math.Ceiling(positions.Count * Math.Clamp(Settings.PHashRequiredMatchingSampleRatio, 0.01f, 1f)));
				sb.AppendLine($"Frames passing the similarity threshold: {passCount} of {positions.Count} — required: {requiredMatches}");
			}

			sb.AppendLine($"Similarity: {FormatSimilarity(similarity)} — required: at least {Settings.Percent.ToString("0.#", inv)}%");

			// Flipped comparison, same as TryCheckDuplicate
			bool flipped = false;
			if (Settings.CompareHorizontallyFlipped) {
				byte[]?[] flippedGray = CreateFlippedGrayBytes(a);
				ulong[]? flippedPHashes = usePHash ? ComputePHashesFromGray(flippedGray) : null;
				if (CheckIfDuplicate(a, flippedGray, flippedPHashes, b, out float flippedDifference)) {
					sb.AppendLine($"Horizontally flipped similarity: {FormatSimilarity(1f - flippedDifference)}");
					if (!isDuplicate || flippedDifference < difference) {
						isDuplicate = true;
						flipped = true;
						difference = flippedDifference;
						similarity = 1f - difference;
					}
				}
			}

			if (isDuplicate) {
				sb.AppendLine($"PASS — the files are considered visually similar{(flipped ? " (as a horizontally flipped match)" : "")}.");
			}
			else {
				sb.AppendLine("FAIL — the similarity is below the configured minimum.");
				failures.Add($"Visual similarity is {FormatSimilarity(similarity)} but at least {Settings.Percent.ToString("0.#", inv)}% is required.");
				if (!float.IsNaN(similarity) && similarity > 0f)
					hints.Add($"Lower the minimum similarity below {FormatSimilarity(similarity)} to catch this pair — but beware of false positives at low values.");
				if (usePHash) {
					// Tell the user whether the grayscale method would have caught the pair.
					float graySum = 0f;
					for (int j = 0; j < positions.Count; j++)
						graySum += Settings.IgnoreBlackPixels || Settings.IgnoreWhitePixels ?
							GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(a.compareGray![j]!, b.compareGray![j]!, Settings.IgnoreBlackPixels, Settings.IgnoreWhitePixels) :
							GrayBytesUtils.PercentageDifference(a.compareGray![j]!, b.compareGray![j]!);
					float graySimilarity = 1f - graySum / positions.Count;
					sb.AppendLine($"For reference, grayscale similarity (pHash disabled): {FormatSimilarity(graySimilarity)}");
					if (graySimilarity >= Settings.Percent / 100f)
						hints.Add("Disabling perceptual hashing (pHash) would detect this pair — the grayscale comparison passes.");
				}
				if (!a.IsImage && positions.Count == 1)
					hints.Add("Only 1 frame per video is sampled. Increasing the thumbnail count makes the comparison more robust.");
			}
			sb.AppendLine();

			// ── Hard link gate ──────────────────────────────────────────────
			if (isDuplicate && Settings.ExcludeHardLinks &&
				a.FileSize == b.FileSize &&
				(a.IsImage || a.mediaInfo!.Duration == b.mediaInfo!.Duration) &&
				HardLinkUtils.AreSameFile(a.Path, b.Path)) {
				sb.AppendLine("--- Hard link check ---");
				sb.AppendLine("FAIL — both paths are hard links to the same data on disk and 'exclude hard links' is enabled.");
				failures.Add("The files are hard links of each other and hard links are excluded.");
				hints.Add("Disable 'exclude hard links' if you want such pairs reported.");
				sb.AppendLine();
				isDuplicate = false;
			}

			bool detected = isDuplicate && durationPassed && folderPassed;

			// ── Partial clip detection ──────────────────────────────────────
			// The real scan only runs this phase for files not already grouped visually,
			// so the test mirrors that and only checks when the pair was not detected.
			if (!detected && !a.IsImage && Settings.EnablePartialClipDetection)
				detected = TestPartialClipPair(sb, a, b, failures, hints, FormatSimilarity, FormatSeconds);

			AppendVerdict(sb, detected, failures, hints, scanInclusionIssues, flipped ? "flipped match" : null);

			a.compareGray = b.compareGray = null;
			a.comparePHashes = b.comparePHashes = null;
			return sb.ToString();
		}

		// Runs the same gates as ScanForPartialDuplicates for a single pair and
		// appends the outcome. Returns true when the pair would be detected as a
		// partial clip.
		bool TestPartialClipPair(StringBuilder sb, FileEntry a, FileEntry b,
			List<string> failures, List<string> hints,
			Func<float, string> formatSimilarity, Func<double, string> formatSeconds) {
			var inv = CultureInfo.InvariantCulture;
			sb.AppendLine("--- Partial clip detection (audio fingerprint) ---");

			double secondsA = a.mediaInfo!.Duration.TotalSeconds;
			double secondsB = b.mediaInfo!.Duration.TotalSeconds;
			FileEntry source = secondsA >= secondsB ? a : b;
			FileEntry clip = secondsA >= secondsB ? b : a;
			double sourceSec = Math.Max(secondsA, secondsB);
			double clipSec = Math.Min(secondsA, secondsB);

			if (sourceSec < 1.0 || clipSec < 1.0) {
				sb.AppendLine("SKIPPED — both videos must be at least 1 second long.");
				return false;
			}
			double ratio = clipSec / sourceSec;
			if (ratio < Settings.PartialClipMinRatio) {
				sb.AppendLine($"FAIL — the shorter video is only {(ratio * 100).ToString("0.#", inv)}% of the longer one; at least {(Settings.PartialClipMinRatio * 100).ToString("0.#", inv)}% is required (minimum clip ratio).");
				failures.Add("The shorter video is below the minimum partial clip ratio.");
				hints.Add("Lower the minimum clip/source ratio in the partial clip settings.");
				return false;
			}
			if (ratio >= 0.95) {
				sb.AppendLine("SKIPPED — the videos are nearly the same length (>= 95%); such pairs are handled by the normal visual comparison, not by partial clip detection.");
				return false;
			}

			string[] labels = { "longer video", "shorter video" };
			FileEntry[] pair = { source, clip };
			for (int i = 0; i < 2; i++) {
				FileEntry entry = pair[i];
				if (entry.AudioFingerprint == null &&
					!entry.Flags.Has(EntryFlags.NoAudioTrack) &&
					!entry.Flags.Has(EntryFlags.AudioFingerprintError) &&
					!entry.Flags.Has(EntryFlags.SilentAudioTrack)) {
					sb.AppendLine($"Extracting audio fingerprint of the {labels[i]}...");
					ExtractAudioFingerprint(entry);
				}
				if (entry.Flags.Has(EntryFlags.NoAudioTrack)) {
					sb.AppendLine($"FAIL — the {labels[i]} has no usable audio track. Partial clip detection requires audio.");
					failures.Add($"The {labels[i]} has no audio track, so partial clip detection cannot match it.");
					return false;
				}
				if (entry.Flags.Has(EntryFlags.SilentAudioTrack)) {
					sb.AppendLine($"FAIL — the audio track of the {labels[i]} is silent. Silent tracks are excluded because they would match any other silent video.");
					failures.Add($"The {labels[i]} has a silent audio track.");
					return false;
				}
				if (entry.Flags.Has(EntryFlags.AudioFingerprintError) || entry.AudioFingerprint == null || entry.AudioFingerprint.Length < 2) {
					sb.AppendLine($"FAIL — the audio fingerprint of the {labels[i]} could not be extracted.");
					failures.Add($"Audio fingerprint extraction failed for the {labels[i]}.");
					return false;
				}
			}
			if (clip.AudioFingerprint!.Length >= source.AudioFingerprint!.Length) {
				sb.AppendLine("FAIL — the shorter video's audio fingerprint is not shorter than the longer video's; the sliding-window comparison requires a clearly shorter clip.");
				failures.Add("The audio fingerprints are too similar in length for partial clip matching.");
				return false;
			}

			float simThreshold = (float)Settings.PartialClipSimilarityThreshold;
			var (similarity, offsetSec) = SlidingWindowCompare(clip.AudioFingerprint, source.AudioFingerprint, simThreshold);
			sb.AppendLine($"Best audio match: {formatSimilarity(similarity)} at offset {formatSeconds(offsetSec)} — required: at least {(Settings.PartialClipSimilarityThreshold * 100).ToString("0.#", inv)}%");
			if (similarity < simThreshold) {
				sb.AppendLine("FAIL — the audio similarity is below the configured threshold.");
				failures.Add($"Best partial clip audio similarity is {formatSimilarity(similarity)} but at least {(Settings.PartialClipSimilarityThreshold * 100).ToString("0.#", inv)}% is required.");
				if (similarity > 0.5f)
					hints.Add($"Lower the partial clip audio similarity threshold below {formatSimilarity(similarity)} to catch this pair.");
				return false;
			}

			if (Settings.PartialClipRequireVisualMatch) {
				bool pass = VerifyPartialClipVisually(source, clip, offsetSec, out float visualSim);
				sb.AppendLine($"Visual confirmation at the matched offset: {formatSimilarity(visualSim)} — required: at least {(Settings.PartialClipVisualThreshold * 100).ToString("0.#", inv)}%");
				if (!pass) {
					sb.AppendLine("FAIL — the audio matches but the frames at the matched offset are not similar enough (visual confirmation).");
					failures.Add($"Partial clip audio matched, but the visual confirmation similarity is {formatSimilarity(visualSim)} (required: {(Settings.PartialClipVisualThreshold * 100).ToString("0.#", inv)}%).");
					hints.Add("Lower the partial clip visual similarity threshold, or disable 'require visual confirmation'.");
					return false;
				}
			}

			sb.AppendLine($"PASS — the shorter video would be detected as a partial clip of the longer one (offset {formatSeconds(offsetSec)}).");
			sb.AppendLine();
			return true;
		}

		static void AppendVerdict(StringBuilder sb, bool detected,
			List<string> failures, List<string> hints, List<string> scanInclusionIssues, string? note) {
			sb.AppendLine("=== Verdict ===");
			if (detected && scanInclusionIssues.Count == 0) {
				sb.AppendLine($"DETECTED — these two files would be reported as duplicates by a scan{(note != null ? $" ({note})" : "")}.");
				sb.AppendLine("If your scans do not show them, make sure both folders are scanned and the scan settings match the ones used for this test.");
			}
			else if (detected) {
				sb.AppendLine("NOT DETECTED — the files are similar enough, but they would never make it into a scan:");
				foreach (string issue in scanInclusionIssues)
					sb.AppendLine($"- {issue}");
			}
			else {
				sb.AppendLine("NOT DETECTED — a scan would not report these files as duplicates:");
				foreach (string failure in failures)
					sb.AppendLine($"- {failure}");
				foreach (string issue in scanInclusionIssues)
					sb.AppendLine($"- {issue}");
				if (hints.Count > 0) {
					sb.AppendLine();
					sb.AppendLine("Suggestions:");
					foreach (string hint in hints)
						sb.AppendLine($"- {hint}");
				}
			}
		}
	}
}
