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

public class BlacklistStoreTests : IDisposable {
	readonly string _dir;
	readonly string _path;

	public BlacklistStoreTests() {
		_dir = Path.Combine(Path.GetTempPath(), "VDF.BlacklistStore." + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
		_path = Path.Combine(_dir, "BlacklistedGroups.json");
	}

	public void Dispose() {
		try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
	}

	[Fact]
	public void Load_MissingFile_ReturnsEmpty() {
		Assert.Empty(BlacklistStore.Load(_path));
	}

	[Fact]
	public async Task Load_EmptyFile_ReturnsEmpty() {
		await File.WriteAllTextAsync(_path, "");
		Assert.Empty(BlacklistStore.Load(_path));
	}

	[Fact]
	public async Task Load_LegacyRawArray_v0_IsRead() {
		// Old format produced by builds before BlacklistStore existed.
		await File.WriteAllTextAsync(_path, "[[\"a\",\"b\"],[\"x\",\"y\",\"z\"]]");

		var groups = BlacklistStore.Load(_path);

		Assert.Equal(2, groups.Count);
		Assert.True(groups[0].SetEquals(new[] { "a", "b" }));
		Assert.True(groups[1].SetEquals(new[] { "x", "y", "z" }));
	}

	[Fact]
	public async Task Load_v1Envelope_IsRead() {
		await File.WriteAllTextAsync(_path,
			"{\"version\":1,\"groups\":[[\"a\",\"b\"],[\"c\"]]}");

		var groups = BlacklistStore.Load(_path);

		Assert.Equal(2, groups.Count);
		Assert.True(groups[0].SetEquals(new[] { "a", "b" }));
		Assert.True(groups[1].SetEquals(new[] { "c" }));
	}

	[Fact]
	public async Task SaveAsync_WritesV1Envelope() {
		var groups = new List<HashSet<string>> {
			new() { "a", "b" },
			new() { "x" }
		};
		await BlacklistStore.SaveAsync(_path, groups);

		string text = await File.ReadAllTextAsync(_path);
		Assert.Contains("\"version\":1", text);
		Assert.Contains("\"groups\":", text);

		// Round-trip via Load to confirm format compatibility.
		var loaded = BlacklistStore.Load(_path);
		Assert.Equal(2, loaded.Count);
		Assert.True(loaded[0].SetEquals(new[] { "a", "b" }));
		Assert.True(loaded[1].SetEquals(new[] { "x" }));
	}

	[Fact]
	public async Task Load_CorruptJson_QuarantinesAndReturnsEmpty() {
		await File.WriteAllTextAsync(_path, "{ this is not valid json");

		var warnings = new List<string>();
		var groups = BlacklistStore.Load(_path, warnings.Add);

		Assert.Empty(groups);
		Assert.False(File.Exists(_path)); // moved aside
		Assert.Single(warnings);
		Assert.Contains("unreadable", warnings[0]);

		var quarantined = Directory.GetFiles(_dir, "BlacklistedGroups.json.corrupt-*");
		Assert.Single(quarantined);
	}

	[Fact]
	public async Task Load_UnknownObjectShape_QuarantinesAndReturnsEmpty() {
		// Valid JSON but neither an array nor an envelope with a "groups" array.
		await File.WriteAllTextAsync(_path, "{\"unexpected\":true}");

		var warnings = new List<string>();
		var groups = BlacklistStore.Load(_path, warnings.Add);

		Assert.Empty(groups);
		Assert.False(File.Exists(_path));
		Assert.Single(warnings);
	}

	[Fact]
	public async Task Load_AppliesPlatformPathComparer() {
		// The on-disk file may have been written by a build that used the default
		// (ordinal) comparer or by another OS. After Load, sets must use the
		// current platform's path comparer so a re-scan with different-cased
		// paths still matches on Windows/macOS.
		await File.WriteAllTextAsync(_path, "[[\"C:\\\\Foo\\\\bar.mp4\"]]");

		var groups = BlacklistStore.Load(_path);

		Assert.Single(groups);
		Assert.Same(PathComparer.ForCurrentPlatform, groups[0].Comparer);
	}

	[Fact]
	public async Task Quarantine_DoesNotOverwriteExistingCorruptSibling() {
		// Two corruption events in the same UTC second must not collide.
		await File.WriteAllTextAsync(_path, "garbage 1");
		BlacklistStore.Load(_path);

		await File.WriteAllTextAsync(_path, "garbage 2");
		BlacklistStore.Load(_path);

		var quarantined = Directory.GetFiles(_dir, "BlacklistedGroups.json.corrupt-*");
		Assert.Equal(2, quarantined.Length);
	}
}
