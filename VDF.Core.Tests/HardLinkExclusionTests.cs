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
// End-to-end cover for Settings.ExcludeHardLinks in the main compare (#904 follow-up): two real
// hard links of one file with identical frames are driven through ScanForDuplicates on every
// path the gate lives on - images, the linear video loop and the bucketed video loop - and the
// gate must drop the pair only when the setting is on, and only for true hard links. The image
// exemption from the equal-duration prefilter is pinned explicitly: a still image's duration is
// zero today, but a legacy or animated entry may carry one, and that must not re-admit the pair.

using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // ScanForDuplicates reads the shared static database
public class HardLinkExclusionTests : IDisposable {

	readonly List<FileEntry> added = new();
	readonly string dir = Path.Combine(Path.GetTempPath(), "vdf-hardlink-" + Guid.NewGuid().ToString("N"));

	public HardLinkExclusionTests() => Directory.CreateDirectory(dir);

	public void Dispose() {
		foreach (var e in added)
			DatabaseUtils.Database.Remove(e);
		try { Directory.Delete(dir, recursive: true); }
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}

	const int FrameLength = GrayBytesUtils.Side * GrayBytesUtils.Side; // 1024
	static readonly byte[] SharedFrame = Frame(0x80);
	static readonly byte[] FileBytes = { 1, 2, 3, 4 };

	static byte[] Frame(byte value) {
		var frame = new byte[FrameLength];
		Array.Fill(frame, value);
		return frame;
	}

	/// <summary>Writes a real file and a real hard link to it; returns both paths.</summary>
	(string original, string link) CreateHardLinkedPair(string extension) {
		string original = Path.Combine(dir, "original" + extension);
		string link = Path.Combine(dir, "link" + extension);
		File.WriteAllBytes(original, FileBytes);
		HardLinkUtils.CreateHardLink(link, original);
		Assert.True(HardLinkUtils.AreSameFile(original, link), "test setup: both paths must resolve to the same file");
		return (original, link);
	}

	/// <summary>Writes two separate files with the same bytes (duplicates, but not hard links).</summary>
	(string a, string b) CreateSeparateCopies(string extension) {
		string a = Path.Combine(dir, "copy-a" + extension);
		string b = Path.Combine(dir, "copy-b" + extension);
		File.WriteAllBytes(a, FileBytes);
		File.WriteAllBytes(b, FileBytes);
		Assert.False(HardLinkUtils.AreSameFile(a, b), "test setup: separate files must not resolve to the same file");
		return (a, b);
	}

	FileEntry AddVideo(string path, double durationSeconds, byte[]? frame = null) {
		var entry = new FileEntry {
			_Path = path,
			FileSize = FileBytes.Length,
			invalid = false,
			IsImage = false,
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(durationSeconds) },
		};
		// ThumbnailCount is 1 with position 0.5 -> index = duration * 0.5.
		entry.grayBytes[entry.GetGrayBytesIndex(0.5f)] = frame ?? SharedFrame;
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	FileEntry AddImage(string path, TimeSpan duration = default) {
		var entry = new FileEntry {
			_Path = path,
			FileSize = FileBytes.Length,
			invalid = false,
			IsImage = true,
			// The scanner builds image MediaInfo without a Duration (TimeSpan.Zero).
			mediaInfo = new MediaInfo { Duration = duration },
		};
		// Image entries store their single frame under index 0.
		entry.grayBytes[0] = SharedFrame;
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	static ScanEngine NewEngine(bool excludeHardLinks) {
		var engine = new ScanEngine();
		engine.Settings.ThumbnailCount = 1;
		engine.positionList.Add(0.5f);
		engine.Settings.Percent = 95f;
		engine.Settings.ExcludeHardLinks = excludeHardLinks;
		engine.ElapsedTimer.Start();
		return engine;
	}

	static bool PairReported(ScanEngine engine, FileEntry a, FileEntry b) =>
		engine.Duplicates.Any(d => d.Path == a.Path) && engine.Duplicates.Any(d => d.Path == b.Path);

	[Fact]
	public void Videos_LinearPath_HardLinksDroppedOnlyWhenExcluded() {
		var (original, link) = CreateHardLinkedPair(".mp4");
		var a = AddVideo(original, 100);
		var b = AddVideo(link, 100);

		var excluding = NewEngine(excludeHardLinks: true);
		excluding.ScanForDuplicates();
		Assert.False(PairReported(excluding, a, b), "hard links of one video must not be reported when ExcludeHardLinks is on");

		var reporting = NewEngine(excludeHardLinks: false);
		reporting.ScanForDuplicates();
		Assert.True(PairReported(reporting, a, b), "with ExcludeHardLinks off the pair is an ordinary duplicate");
	}

	[Fact]
	public void Images_HardLinksDroppedOnlyWhenExcluded() {
		var (original, link) = CreateHardLinkedPair(".jpg");
		var a = AddImage(original);
		var b = AddImage(link);

		var excluding = NewEngine(excludeHardLinks: true);
		excluding.ScanForDuplicates();
		Assert.False(PairReported(excluding, a, b), "hard links of one image must not be reported when ExcludeHardLinks is on");

		var reporting = NewEngine(excludeHardLinks: false);
		reporting.ScanForDuplicates();
		Assert.True(PairReported(reporting, a, b), "with ExcludeHardLinks off the pair is an ordinary duplicate");
	}

	// The gate's equal-duration prefilter is for videos only. Image entries normally carry no
	// duration, but one from a legacy database or an animated format may; that must not turn
	// the prefilter into a bypass that re-admits a hard-linked image pair.
	[Fact]
	public void Images_StillDroppedWhenOneSideCarriesADuration() {
		var (original, link) = CreateHardLinkedPair(".jpg");
		var a = AddImage(original);
		var b = AddImage(link, TimeSpan.FromMilliseconds(40)); // one frame at 25 fps, what ffprobe reports for a still

		var excluding = NewEngine(excludeHardLinks: true);
		excluding.ScanForDuplicates();
		Assert.False(PairReported(excluding, a, b), "an image pair's duration must not affect hard-link exclusion");
	}

	[Fact]
	public void SeparateCopies_StillReportedWhenExcluding() {
		var (pathA, pathB) = CreateSeparateCopies(".mp4");
		var a = AddVideo(pathA, 100);
		var b = AddVideo(pathB, 100);

		var excluding = NewEngine(excludeHardLinks: true);
		excluding.ScanForDuplicates();
		Assert.True(PairReported(excluding, a, b), "ExcludeHardLinks must only drop true hard links, not byte-identical copies");
	}

	// Libraries at or above ScanEngine.BucketActivationThreshold videos take the duration-bucketed
	// compare path instead of the linear loop; the gate has to hold there too. Filler entries sit
	// alone in their own whole-second buckets under a zero duration tolerance, so the only pair
	// ever compared is the hard-linked one.
	[Fact]
	public void Videos_BucketedPath_HardLinksDroppedOnlyWhenExcluded() {
		var (original, link) = CreateHardLinkedPair(".mp4");
		var a = AddVideo(original, 100);
		var b = AddVideo(link, 100);
		byte[] fillerFrame = Frame(0x10);
		for (int i = 0; i < ScanEngine.BucketActivationThreshold - 2; i++)
			AddVideo(Path.Combine(dir, "filler" + i + ".mp4"), 1000 + i, fillerFrame);

		var excluding = NewEngine(excludeHardLinks: true);
		excluding.Settings.PercentDurationDifference = 0;
		excluding.ScanForDuplicates();
		Assert.False(PairReported(excluding, a, b), "hard links must be dropped on the bucketed compare path too");
		Assert.Empty(excluding.Duplicates);

		var reporting = NewEngine(excludeHardLinks: false);
		reporting.Settings.PercentDurationDifference = 0;
		reporting.ScanForDuplicates();
		Assert.True(PairReported(reporting, a, b), "the bucketed path must still compare the pair when ExcludeHardLinks is off");
	}
}
