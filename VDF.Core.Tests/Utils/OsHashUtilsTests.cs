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

namespace VDF.Core.Tests.Utils;

public class OsHashUtilsTests : IDisposable {
	readonly string _dir;

	public OsHashUtilsTests() {
		_dir = Path.Combine(Path.GetTempPath(), "VDF.OsHash." + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
	}

	public void Dispose() {
		try { Directory.Delete(_dir, true); } catch { }
	}

	string WriteFile(string name, byte[] content) {
		string p = Path.Combine(_dir, name);
		File.WriteAllBytes(p, content);
		return p;
	}

	static byte[] Pattern(int size, int seed) {
		var b = new byte[size];
		for (int i = 0; i < size; i++)
			b[i] = (byte)((i * 31 + seed) & 0xFF);
		return b;
	}

	[Fact]
	public void SameContentAtDifferentPaths_YieldsSameHash() {
		// The whole point: a moved/renamed file keeps its identity.
		byte[] content = Pattern(200_000, 7);
		string a = WriteFile("a.bin", content);
		string b = WriteFile("subdir_b.bin", content);

		string? ha = OsHashUtils.TryCompute(a);
		string? hb = OsHashUtils.TryCompute(b);

		Assert.NotNull(ha);
		Assert.Equal(ha, hb);
	}

	[Fact]
	public void DifferentContent_YieldsDifferentHash() {
		string a = WriteFile("a.bin", Pattern(200_000, 1));
		string b = WriteFile("b.bin", Pattern(200_000, 2));
		Assert.NotEqual(OsHashUtils.TryCompute(a), OsHashUtils.TryCompute(b));
	}

	[Fact]
	public void SameSizeDifferentTail_YieldsDifferentHash() {
		// oshash mixes the last 64 KiB, so an edit near the end must change the hash even
		// when the size is identical — this is what stops a wrong relink.
		byte[] c1 = Pattern(200_000, 5);
		byte[] c2 = (byte[])c1.Clone();
		c2[^1] ^= 0xFF;
		Assert.NotEqual(OsHashUtils.TryCompute(WriteFile("c1.bin", c1)),
						OsHashUtils.TryCompute(WriteFile("c2.bin", c2)));
	}

	[Fact]
	public void TooSmallOrMissing_ReturnsNull() {
		Assert.Null(OsHashUtils.TryCompute(WriteFile("tiny.bin", Pattern(1000, 0)))); // < 64 KiB
		Assert.Null(OsHashUtils.TryCompute(Path.Combine(_dir, "does-not-exist.bin")));
	}
}
