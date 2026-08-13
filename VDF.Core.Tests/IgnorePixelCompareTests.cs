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
// End-to-end cover for the IgnoreBlackPixels/IgnoreWhitePixels settings (#893): drives the
// real ScanForDuplicates over hand-built gray frames and asserts the toggles change the
// outcome. The pair is crafted so the shared black (or white) region drags the whole-frame
// difference under the threshold while the pixels that actually differ exceed it — exactly
// the letterboxed/dark-scene false-positive class the settings exist to suppress.

using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // ScanForDuplicates reads the shared static database
public class IgnorePixelCompareTests : IDisposable {

	readonly List<FileEntry> added = new();

	public void Dispose() {
		foreach (var e in added)
			DatabaseUtils.Database.Remove(e);
	}

	const int FrameLength = GrayBytesUtils.Side * GrayBytesUtils.Side; // 1024

	/// <summary>Frame whose first half is <paramref name="filler"/> and second half is <paramref name="content"/>.</summary>
	static byte[] HalfFrame(byte filler, byte content) {
		var frame = new byte[FrameLength];
		Array.Fill(frame, filler, 0, FrameLength / 2);
		Array.Fill(frame, content, FrameLength / 2, FrameLength / 2);
		return frame;
	}

	FileEntry Add(string name, byte[] frame, long fileSize) {
		var entry = new FileEntry {
			_Path = @"C:\vdf-ignorepixels-" + Guid.NewGuid().ToString("N") + "\\" + name,
			FileSize = fileSize,
			invalid = false,
			IsImage = false,
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(100) },
		};
		// ThumbnailCount is 1 with position 0.5 -> index = 100s * 0.5.
		entry.grayBytes[entry.GetGrayBytesIndex(0.5f)] = frame;
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	ScanEngine NewEngine(bool ignoreBlack, bool ignoreWhite) {
		var engine = new ScanEngine();
		engine.Settings.ThumbnailCount = 1;
		engine.positionList.Add(0.5f);
		engine.Settings.Percent = 95f;
		engine.Settings.IgnoreBlackPixels = ignoreBlack;
		engine.Settings.IgnoreWhitePixels = ignoreWhite;
		engine.ElapsedTimer.Start();
		return engine;
	}

	bool PairReported(ScanEngine engine) =>
		added.Any(e => engine.Duplicates.Any(d => d.Path == e.Path));

	// Half the frame is black in both files (letterbox/dark scene), the other half differs
	// by 16 gray levels. Whole-frame difference: 512*16/1024/256 = 3.125% (a 96.9% match,
	// above the 95% threshold). Non-black pixels only: 16/256 = 6.25% (93.75%, below it).

	[Fact]
	public void IgnoreBlackPixels_Off_SharedBlackAreasProduceTheMatch() {
		Add("a.mp4", HalfFrame(0x00, 0x80), 1);
		Add("b.mp4", HalfFrame(0x00, 0x90), 2);
		var engine = NewEngine(ignoreBlack: false, ignoreWhite: false);
		engine.ScanForDuplicates();
		Assert.True(PairReported(engine), "without the toggle the shared black half must carry the pair over the threshold");
	}

	[Fact]
	public void IgnoreBlackPixels_On_SuppressesTheBlackCarriedMatch() {
		Add("a.mp4", HalfFrame(0x00, 0x80), 1);
		Add("b.mp4", HalfFrame(0x00, 0x90), 2);
		var engine = NewEngine(ignoreBlack: true, ignoreWhite: false);
		engine.ScanForDuplicates();
		Assert.False(PairReported(engine), "ignoring black pixels must judge the pair only by the differing content half");
	}

	[Fact]
	public void IgnoreWhitePixels_On_SuppressesTheWhiteCarriedMatch() {
		Add("a.mp4", HalfFrame(0xFF, 0x80), 1);
		Add("b.mp4", HalfFrame(0xFF, 0x90), 2);

		var offEngine = NewEngine(ignoreBlack: false, ignoreWhite: false);
		offEngine.ScanForDuplicates();
		Assert.True(PairReported(offEngine), "without the toggle the shared white half must carry the pair over the threshold");

		var onEngine = NewEngine(ignoreBlack: false, ignoreWhite: true);
		onEngine.ScanForDuplicates();
		Assert.False(PairReported(onEngine), "ignoring white pixels must judge the pair only by the differing content half");
	}

	[Fact]
	public void IgnoreBlackPixels_AppliesToImagesToo() {
		var a = Add("a.jpg", HalfFrame(0x00, 0x80), 1);
		var b = Add("b.jpg", HalfFrame(0x00, 0x90), 2);
		a.IsImage = true;
		b.IsImage = true;
		// Image entries store their single frame under index 0.
		a.grayBytes.Clear();
		a.grayBytes[0] = HalfFrame(0x00, 0x80);
		b.grayBytes.Clear();
		b.grayBytes[0] = HalfFrame(0x00, 0x90);

		var offEngine = NewEngine(ignoreBlack: false, ignoreWhite: false);
		offEngine.Settings.IncludeImages = true;
		offEngine.ScanForDuplicates();
		Assert.True(PairReported(offEngine));

		var onEngine = NewEngine(ignoreBlack: true, ignoreWhite: false);
		onEngine.Settings.IncludeImages = true;
		onEngine.ScanForDuplicates();
		Assert.False(PairReported(onEngine));
	}
}
