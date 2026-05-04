// /*
//     Copyright (C) 2025 0x90d
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

using BenchmarkDotNet.Attributes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using VDF.Core.Utils;

namespace VDF.Benchmarks.Micro;

/// <summary>
/// Benchmarks the ImageSharp gray-bytes path used for image (not video) inputs.
/// Establishes a baseline for evaluating the redundant-Grayscale-call removal and
/// resize-then-CloneAs reordering.
/// </summary>
[MemoryDiagnoser]
public class GetGrayScaleValuesBench {
	[Params(320, 1280, 1920)]
	public int Width;

	Image<Rgba32> _image = null!;

	[GlobalSetup]
	public void Setup() {
		// Source dimensions chosen to mirror typical thumbnail/photo sizes; height
		// follows a 16:9-ish aspect to exercise resize on both axes.
		int height = Width * 9 / 16;
		_image = new Image<Rgba32>(Width, height);
		var rng = new Random(42);
		_image.ProcessPixelRows(accessor => {
			for (int y = 0; y < accessor.Height; y++) {
				var row = accessor.GetRowSpan(y);
				for (int x = 0; x < row.Length; x++) {
					byte v = (byte)rng.Next(256);
					row[x] = new Rgba32(v, v, v, 255);
				}
			}
		});
	}

	[GlobalCleanup]
	public void Cleanup() => _image?.Dispose();

	[Benchmark]
	public byte[]? GetGrayScaleValues_32x32() => GrayBytesUtils.GetGrayScaleValues(_image);

	[Benchmark]
	public byte[]? GetGrayScaleValues_16x16() => GrayBytesUtils.GetGrayScaleValues16x16(_image);
}
