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

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

// Regression coverage for issue #751.
//
// The original bug: JoinImages called WriteableBitmap construction (and the
// DangerousTryGetSinglePixelMemory check that gates it) BEFORE writing the
// JPEG. ImageSharp returns a multi-buffer pixel layout above ~4 MB per Bgra32
// image, so for a 5×500-wide portrait composite (e.g. 720×1280 source →
// 500×888 thumbnail → 2500×888 canvas = 8.88 MB Bgra32) the early check
// returned false, JoinImages returned null, and the JPEG was never written.
// AppendIfMissing then recorded a (offset, len=0) entry, the Thumbnail
// getter served back an empty slice, the UI rendered a blank cell, and the
// retry filter saw a populated ImageList so "Load thumbnails for group"
// no-oped forever.
//
// These tests pin the contract: TryWriteJoinedJpeg MUST produce a decodable
// JPEG for input sizes that span the multi-buffer boundary. If a future
// refactor reintroduces a single-buffer assumption, the multi-buffer test
// here will fail.
public class JpegCompositorTests {
	[Fact]
	public void EmptyList_WritesNothing_ReturnsFalse() {
		using var ms = new MemoryStream();
		bool ok = JpegCompositor.TryWriteJoinedJpeg(Array.Empty<Image>(), ms);
		Assert.False(ok);
		Assert.Equal(0, ms.Length);
	}

	[Fact]
	public void SingleSmallImage_WritesDecodableJpeg() {
		using var src = new Image<Rgba32>(100, 56);
		using var ms = new MemoryStream();

		bool ok = JpegCompositor.TryWriteJoinedJpeg(new Image[] { src }, ms);

		Assert.True(ok);
		AssertIsDecodableJpegOfAtLeast(ms, expectedWidth: 100, expectedHeight: 56);
	}

	[Fact]
	public void FivePortraitFrames_BugSizeComposite_StillProducesJpeg() {
		// This is the exact shape from samt108's bug report: 5 thumbnails ×
		// 500 px wide, portrait reddit-style source. Composite is 2500×888,
		// Bgra32 = 8.88 MB → ImageSharp uses multi-buffer pixel storage.
		// Pre-fix this would silently produce 0 bytes and poison the cache.
		var images = new Image[5];
		try {
			for (int i = 0; i < images.Length; i++)
				images[i] = new Image<Rgba32>(500, 888);

			using var ms = new MemoryStream();
			bool ok = JpegCompositor.TryWriteJoinedJpeg(images, ms);

			Assert.True(ok, "Composite write must succeed even when the canvas is large enough to use ImageSharp's multi-buffer pixel layout (~4 MB Bgra32 threshold).");
			Assert.True(ms.Length > 1024, $"JPEG should be substantial; got only {ms.Length} bytes — the empty-write regression is back.");
			AssertIsDecodableJpegOfAtLeast(ms, expectedWidth: 2500, expectedHeight: 888);
		}
		finally {
			foreach (var img in images) img?.Dispose();
		}
	}

	[Fact]
	public void DegenerateAspect_OneVeryWide_DoesNotThrow() {
		// Defensive: the GUI also resizes >4096-wide composites in
		// BuildComposite. Make sure the resize path itself doesn't trip
		// on extreme inputs.
		using var src = new Image<Rgba32>(8000, 100);
		using var ms = new MemoryStream();

		bool ok = JpegCompositor.TryWriteJoinedJpeg(new Image[] { src }, ms);

		Assert.True(ok);
		Assert.True(ms.Length > 0);
	}

	static void AssertIsDecodableJpegOfAtLeast(MemoryStream ms, int expectedWidth, int expectedHeight) {
		ms.Position = 0;
		using var decoded = Image.Load(ms);
		// Resize logic in BuildComposite may have shrunk a too-wide canvas, so
		// we only require the shape to be sensible (not zero, not larger than
		// what the compositor was asked to produce).
		Assert.True(decoded.Width > 0 && decoded.Height > 0, $"Decoded JPEG is degenerate: {decoded.Width}x{decoded.Height}");
		Assert.True(decoded.Width <= expectedWidth, $"Decoded width {decoded.Width} > expected ceiling {expectedWidth}");
		Assert.True(decoded.Height <= expectedHeight, $"Decoded height {decoded.Height} > expected ceiling {expectedHeight}");
	}
}
