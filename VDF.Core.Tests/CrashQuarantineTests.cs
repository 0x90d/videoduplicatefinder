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

	[Fact]
	public void PartialVerifyCrash_FlagsThumbnailError() {
		// #863: the partial-clip visual gate decodes frames in-process; a crash there must
		// quarantine the file like a sampling crash so the gate never decodes it again.
		var entry = Entry(@"C:\videos\poison.mp4");
		var suspects = new[] { new ScanCrashJournal.Suspect(ScanCrashJournal.PhasePartialVerify, entry.Path) };

		Assert.Equal(1, ScanEngine.ApplyCrashQuarantine(new[] { entry }, suspects));
		Assert.True(entry.Flags.Has(EntryFlags.ThumbnailError));
		Assert.False(entry.Flags.Has(EntryFlags.AudioFingerprintError));
	}
}

// #863: an access violation inside the GPU driver (nvcuda64.dll) during "verifying
// partial clips" killed the app on the same file at every attempt. Once the crash
// journal has quarantined a file (ThumbnailError), the visual gate must not decode it
// again - the audio match alone decides, exactly like the no-frames case.
public class PartialVerifyDecodeBlockTests {
	static FileEntry Video(string name) => new() { _Path = Path.Combine(Path.GetTempPath(), name) };

	[Fact]
	public void FlaggedSourceOrClip_BlocksDecode() {
		var flagged = Video("poison.mp4");
		flagged.Flags.Set(EntryFlags.ThumbnailError);
		var healthy = Video("healthy.mp4");

		Assert.True(ScanEngine.PartialVerifyDecodeBlocked(flagged, healthy));
		Assert.True(ScanEngine.PartialVerifyDecodeBlocked(healthy, flagged));
		Assert.False(ScanEngine.PartialVerifyDecodeBlocked(healthy, Video("other.mp4")));
	}

	[Fact]
	public void VisualGate_SkipsVerifierForQuarantinedPairs_AndKeepsTheAssignment() {
		var engine = new ScanEngine();
		var videos = new List<FileEntry> { Video("source.mp4"), Video("clip0.mp4"), Video("clip1.mp4") };
		videos[1].Flags.Set(EntryFlags.ThumbnailError); // quarantined after a decoder crash
		var assignments = new List<(int sourceIdx, int clipIdx, float sim, int offsetSec, Guid groupId)> {
			(0, 1, 0.9f, 0, Guid.NewGuid()),
			(0, 2, 0.9f, 0, Guid.NewGuid()),
		};

		var verifiedPaths = new List<string>();
		var kept = engine.RunPartialClipVisualGate(videos, assignments, (_, clip, _) => {
			lock (verifiedPaths)
				verifiedPaths.Add(clip.Path);
			return (false, 0f); // the verifier drops everything it is actually asked about
		});

		// The quarantined pair never reaches the verifier and stays in (audio decides)...
		Assert.Single(kept);
		Assert.Equal(1, kept[0].clipIdx);
		// ...while the healthy pair was verified (and dropped by the stub).
		Assert.Equal(new[] { videos[2].Path }, verifiedPaths);
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
