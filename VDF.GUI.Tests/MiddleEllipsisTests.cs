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

using VDF.GUI.Utils;

namespace VDF.GUI.Tests {
	public class MiddleEllipsisTests {
		// 1 unit per char makes widths trivially predictable.
		static double CharWidth(string s) => s.Length;

		[Fact]
		public void TextThatFits_IsUnchanged() {
			Assert.Equal("short", MiddleEllipsis.Trim("short", CharWidth, 10));
			Assert.Equal("exact", MiddleEllipsis.Trim("exact", CharWidth, 5));
		}

		[Fact]
		public void LongText_KeepsPrefixAndSuffixAroundEllipsis() {
			string path = @"D:\Media\Camera Roll\2024\Vacation\old\beach sunset 4k.mp4";
			string trimmed = MiddleEllipsis.Trim(path, CharWidth, 30);

			Assert.True(trimmed.Length <= 30);
			Assert.Contains(MiddleEllipsis.EllipsisChar, trimmed);
			Assert.StartsWith(@"D:\", trimmed);           // drive stays visible
			Assert.EndsWith("4k.mp4", trimmed);           // tail stays visible
		}

		[Fact]
		public void Result_UsesTheFullBudget() {
			string text = new string('x', 100);
			string trimmed = MiddleEllipsis.Trim(text, CharWidth, 21);
			// 20 kept chars + 1 ellipsis is the widest fit
			Assert.Equal(21, trimmed.Length);
		}

		[Fact]
		public void SuffixGetsTheOddCharacter() {
			string trimmed = MiddleEllipsis.Compose("abcdefghij", 5);
			Assert.Equal("ab…hij", trimmed);
		}

		[Fact]
		public void HopelesslyNarrow_ReturnsEllipsisOnly() {
			Assert.Equal("…", MiddleEllipsis.Trim("something long enough", CharWidth, 0.5));
		}

		[Fact]
		public void EmptyOrNull_ReturnsInput() {
			Assert.Equal("", MiddleEllipsis.Trim("", CharWidth, 10));
		}
	}
}
