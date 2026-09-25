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
// #910: the display matrix decides how the native path turns a picture, the same way the
// FFmpeg command line's autorotate does.

using VDF.Core.FFTools.FFmpegNative;

namespace VDF.Core.Tests.FFTools;

public class FrameOrientationTests {
	/// <summary>av_display_rotation_set: the matrix FFmpeg writes for a counterclockwise angle.</summary>
	static int[] Rotation(double degrees, bool hflip = false) {
		double radians = -degrees * Math.PI / 180;
		double c = Math.Cos(radians), s = Math.Sin(radians);
		int F(double v) => (int)Math.Round(v * 65536);
		var m = new int[9];
		m[0] = F(c); m[1] = F(-s); m[3] = F(s); m[4] = F(c); m[8] = 1 << 30;
		if (hflip) { m[0] = -m[0]; m[3] = -m[3]; } // av_display_matrix_flip(m, 1, 0)
		return m;
	}

	[Fact]
	public void ReportersMatrix_IsAHalfTurn() {
		// ffprobe: displaymatrix -65536 0 0 / 0 -65536 0 / ..., rotation -180
		int[] m = { -65536, 0, 0, 0, -65536, 0, 251658240, 141557760, 1073741824 };
		Assert.Equal(new FrameOrientation(false, true, true), FrameOrientation.FromDisplayMatrix(m));
	}

	[Fact]
	public void Identity_AndDegenerateMatrices_LeaveThePictureAlone() {
		Assert.True(FrameOrientation.FromDisplayMatrix(Rotation(0)).IsIdentity);
		Assert.True(FrameOrientation.FromDisplayMatrix(new int[9]).IsIdentity);
		Assert.True(FrameOrientation.FromDisplayMatrix(Rotation(45)).IsIdentity); // not a multiple of 90
	}

	[Theory]
	[InlineData(90, true, true, false)]    // transpose=clock
	[InlineData(-90, true, false, true)]   // transpose=cclock
	[InlineData(270, true, false, true)]
	[InlineData(180, false, true, true)]
	public void QuarterAndHalfTurns(double degrees, bool transpose, bool flipH, bool flipV) =>
		Assert.Equal(new FrameOrientation(transpose, flipH, flipV), FrameOrientation.FromDisplayMatrix(Rotation(degrees)));

	[Fact]
	public void MirroredMatrices_AreMirrored() {
		// A mirrored picture is flipped back, a mirrored quarter turn still turns.
		Assert.False(FrameOrientation.FromDisplayMatrix(Rotation(0, hflip: true)).IsIdentity);
		Assert.True(FrameOrientation.FromDisplayMatrix(Rotation(90, hflip: true)).Transpose);
	}

	// 3 wide, 2 high:  a b c
	//                  d e f
	static readonly byte[] Picture = { 1, 2, 3, 4, 5, 6 };

	[Fact]
	public void Clockwise_TurnsTheLeftColumnIntoTheTopRow() {
		var turned = new FrameOrientation(true, true, false).Apply(Picture, 3, 2, 1);
		// d a
		// e b
		// f c
		Assert.Equal(new byte[] { 4, 1, 5, 2, 6, 3 }, turned);
		Assert.Equal((2, 3), new FrameOrientation(true, true, false).Apply(3, 2));
	}

	[Fact]
	public void Counterclockwise_TurnsTheRightColumnIntoTheTopRow() {
		// c f
		// b e
		// a d
		Assert.Equal(new byte[] { 3, 6, 2, 5, 1, 4 }, new FrameOrientation(true, false, true).Apply(Picture, 3, 2, 1));
	}

	[Fact]
	public void HalfTurn_ReversesThePicture_AndKeepsPixelsWhole() {
		Assert.Equal(new byte[] { 6, 5, 4, 3, 2, 1 }, new FrameOrientation(false, true, true).Apply(Picture, 3, 2, 1));
		// RGB: three bytes move together.
		byte[] rgb = { 1, 1, 1, 2, 2, 2 };
		Assert.Equal(new byte[] { 2, 2, 2, 1, 1, 1 }, new FrameOrientation(false, true, true).Apply(rgb, 2, 1, 3));
	}

	[Fact]
	public void Identity_ReturnsTheSameBuffer() =>
		Assert.Same(Picture, FrameOrientation.None.Apply(Picture, 3, 2, 1));
}
