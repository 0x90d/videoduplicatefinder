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

using VDF.Core.Utils;

namespace VDF.Core.Tests;

// #861: a native access violation kills the process without flagging the file that
// caused it, so every following scan crashed at the same point. The quarantine pass
// turns the crash breadcrumbs into per-entry error flags on the next scan.
public class CrashQuarantineTests {
	static FileEntry Entry(string path, bool isImage = false) {
		var entry = new FileEntry {
			_Path = path,
			Folder = Path.GetDirectoryName(path)!,
			FileSize = 1,
		};
		if (isImage)
			entry.Flags.Set(EntryFlags.IsImage);
		return entry;
	}

	[Fact]
	public void SamplingCrash_FlagsThumbnailError() {
		var entry = Entry(@"C:\videos\poison.mp4");
		var suspects = new[] { new ScanCrashJournal.Suspect(ScanCrashJournal.PhaseSampling, entry.Path) };

		int flagged = ScanEngine.ApplyCrashQuarantine(new[] { entry }, suspects);

		Assert.Equal(1, flagged);
		Assert.True(entry.Flags.Has(EntryFlags.ThumbnailError));
		Assert.False(entry.Flags.Has(EntryFlags.AudioFingerprintError));
	}

	[Fact]
	public void ImageCrash_FlagsThumbnailError() {
		var entry = Entry(@"C:\images\poison.jpg", isImage: true);
		var suspects = new[] { new ScanCrashJournal.Suspect(ScanCrashJournal.PhaseImage, entry.Path) };

		Assert.Equal(1, ScanEngine.ApplyCrashQuarantine(new[] { entry }, suspects));
		Assert.True(entry.Flags.Has(EntryFlags.ThumbnailError));
	}

	[Fact]
	public void AudioCrash_FlagsOnlyAudioFingerprintError_EntryStaysVideoComparable() {
		var entry = Entry(@"C:\videos\poison.mp4");
		var suspects = new[] { new ScanCrashJournal.Suspect(ScanCrashJournal.PhaseAudio, entry.Path) };

		Assert.Equal(1, ScanEngine.ApplyCrashQuarantine(new[] { entry }, suspects));
		Assert.True(entry.Flags.Has(EntryFlags.AudioFingerprintError));
		// The gray bytes of an audio-crash file are typically fine — it must not be
		// excluded from video comparison via ThumbnailError.
		Assert.False(entry.Flags.Has(EntryFlags.ThumbnailError));
		// No fake empty fingerprint: null is what makes the entry retryable later.
		Assert.Null(entry.AudioFingerprint);
	}

	[Fact]
	public void UnrelatedEntries_AreUntouched() {
		var poison = Entry(@"C:\videos\poison.mp4");
		var innocent = Entry(@"C:\videos\innocent.mp4");
		var suspects = new[] { new ScanCrashJournal.Suspect(ScanCrashJournal.PhaseSampling, poison.Path) };

		int flagged = ScanEngine.ApplyCrashQuarantine(new[] { poison, innocent }, suspects);

		Assert.Equal(1, flagged);
		Assert.Equal((EntryFlags)0, innocent.Flags & (EntryFlags.ThumbnailError | EntryFlags.AudioFingerprintError));
	}

	[Fact]
	public void NoSuspects_DoesNothing() {
		var entry = Entry(@"C:\videos\a.mp4");
		Assert.Equal(0, ScanEngine.ApplyCrashQuarantine(new[] { entry }, Array.Empty<ScanCrashJournal.Suspect>()));
	}

	[Fact]
	public void QuarantinedEntries_AreReportedViaCallback() {
		var entry = Entry(@"C:\videos\poison.mp4");
		var suspects = new[] { new ScanCrashJournal.Suspect(ScanCrashJournal.PhaseSampling, entry.Path) };
		var reported = new List<FileEntry>();

		ScanEngine.ApplyCrashQuarantine(new[] { entry }, suspects, reported.Add);

		Assert.Equal(new[] { entry }, reported);
	}
}

// The audio-fingerprint eligibility gate, extracted from GatherInfos so the
// crash-quarantine retry semantics stay pinned.
public class NeedsAudioFingerprintTests {
	static FileEntry VideoEntry() => new() {
		_Path = @"C:\videos\a.mp4",
		Folder = @"C:\videos",
		FileSize = 1,
	};

	[Fact]
	public void FreshVideo_NeedsFingerprint() {
		Assert.True(ScanEngine.NeedsAudioFingerprint(VideoEntry(), partialClipDetectionEnabled: true, alwaysRetryFailedSampling: false));
	}

	[Fact]
	public void DetectionDisabled_NeverNeedsFingerprint() {
		Assert.False(ScanEngine.NeedsAudioFingerprint(VideoEntry(), partialClipDetectionEnabled: false, alwaysRetryFailedSampling: true));
	}

	[Fact]
	public void CachedFingerprint_NotReExtracted() {
		var entry = VideoEntry();
		entry.AudioFingerprint = new uint[] { 1, 2, 3 };
		Assert.False(ScanEngine.NeedsAudioFingerprint(entry, true, true));
	}

	[Fact]
	public void CrashQuarantinedEntry_IsSkippedByDefault() {
		// Flag without fingerprint = the state ApplyCrashQuarantine leaves behind.
		var entry = VideoEntry();
		entry.Flags.Set(EntryFlags.AudioFingerprintError);

		Assert.False(ScanEngine.NeedsAudioFingerprint(entry, true, alwaysRetryFailedSampling: false));
	}

	[Fact]
	public void CrashQuarantinedEntry_RetriedWithAlwaysRetryFailedSampling() {
		var entry = VideoEntry();
		entry.Flags.Set(EntryFlags.AudioFingerprintError);

		Assert.True(ScanEngine.NeedsAudioFingerprint(entry, true, alwaysRetryFailedSampling: true));
	}

	[Fact]
	public void CompletedAudioFailure_NotRetriedEvenWithAlwaysRetry() {
		// A genuine (completed) extraction failure records flag + empty fingerprint;
		// the non-null fingerprint keeps it blocked so only crash-quarantined entries retry.
		var entry = VideoEntry();
		entry.Flags.Set(EntryFlags.AudioFingerprintError);
		entry.AudioFingerprint = Array.Empty<uint>();

		Assert.False(ScanEngine.NeedsAudioFingerprint(entry, true, alwaysRetryFailedSampling: true));
	}

	[Theory]
	[InlineData(EntryFlags.NoAudioTrack)]
	[InlineData(EntryFlags.SilentAudioTrack)]
	public void KnownAudiolessEntries_NotRetriedEvenWithAlwaysRetry(EntryFlags flag) {
		var entry = VideoEntry();
		entry.Flags.Set(flag);
		Assert.False(ScanEngine.NeedsAudioFingerprint(entry, true, alwaysRetryFailedSampling: true));
	}

	[Fact]
	public void Images_NeverNeedAudioFingerprint() {
		var entry = VideoEntry();
		entry.Flags.Set(EntryFlags.IsImage);
		Assert.False(ScanEngine.NeedsAudioFingerprint(entry, true, true));
	}
}
