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

using MemoryPack;

namespace VDF.Core.Tests.AI;

/// <summary>
/// Neural embeddings deliberately live in the UnionEmbeddingStore sidecar, NOT on
/// FileEntry — these tests pin the database-schema consequences of that decision.
/// </summary>
public partial class FileEntryEmbeddingsTests {

	/// <summary>
	/// Wire-compatible mirror of the interim FileEntry schema that carried embeddings as
	/// MemoryPackOrder(11) — the shape a database written by that build has on disk.
	/// </summary>
	[MemoryPackable(GenerateType.VersionTolerant)]
	public partial class FileEntryWithEmbeddingsMember {
		[MemoryPackOrder(0)] public string _Path = string.Empty;
		[MemoryPackOrder(1)] public string Folder = string.Empty;
		[MemoryPackOrder(2)] public Dictionary<double, byte[]?> grayBytes = new();
		[MemoryPackOrder(3)] public MediaInfo? mediaInfo;
		[MemoryPackOrder(4)] public EntryFlags Flags;
		[MemoryPackOrder(5)] public DateTime DateCreated;
		[MemoryPackOrder(6)] public DateTime DateModified;
		[MemoryPackOrder(7)] public long FileSize;
		[MemoryPackOrder(8)] public Dictionary<double, ulong?> PHashes = new();
		[MemoryPackOrder(9)] public uint[]? AudioFingerprint;
		[MemoryPackOrder(10)] public string? OsHash;
		[MemoryPackOrder(11)] public Dictionary<double, byte[]?> Embeddings = new();
	}

	[Fact]
	public void PayloadWithEmbeddingsMember_LoadsIntoCurrentSchema() {
		// A database written by the interim build that stored embeddings on FileEntry must
		// keep loading after the member's removal: version-tolerant deserialization skips
		// the extra trailing member and every remaining field survives.
		var legacy = new FileEntryWithEmbeddingsMember {
			_Path = @"D:\media\clip.mp4",
			Folder = @"D:\media",
			FileSize = 1234,
			OsHash = "abc123",
			AudioFingerprint = new uint[] { 1, 2, 3 },
		};
		legacy.grayBytes[12.5] = new byte[] { 1, 2, 3 };
		legacy.PHashes[12.5] = 42UL;
		legacy.Embeddings[12.5] = new byte[] { 10, 20, 30 };
		legacy.Embeddings[37.5] = null;

		byte[] payload = MemoryPackSerializer.Serialize(legacy);
		FileEntry? restored = MemoryPackSerializer.Deserialize<FileEntry>(payload);

		Assert.NotNull(restored);
		Assert.Equal(@"D:\media\clip.mp4", restored!.Path);
		Assert.Equal(new byte[] { 1, 2, 3 }, restored.grayBytes[12.5]);
		Assert.Equal(42UL, restored.PHashes[12.5]);
		Assert.Equal(1234, restored.FileSize);
		Assert.Equal("abc123", restored.OsHash);
		Assert.Equal(new uint[] { 1, 2, 3 }, restored.AudioFingerprint);
	}

	[Fact]
	public void CurrentSchema_RoundTripsAndLoadsIntoEmbeddingsBuild() {
		// The reverse direction: a database written by the current schema is a valid
		// prefix of the interim embeddings schema (its Embeddings member is simply
		// missing), and of course round-trips through itself.
		var entry = new FileEntry { Folder = @"D:\media" };
		entry.Path = @"D:\media\old.mp4";
		entry.grayBytes[12.5] = new byte[] { 1, 2, 3 };

		byte[] payload = MemoryPackSerializer.Serialize(entry);

		FileEntry? roundTripped = MemoryPackSerializer.Deserialize<FileEntry>(payload);
		Assert.NotNull(roundTripped);
		Assert.Equal(new byte[] { 1, 2, 3 }, roundTripped!.grayBytes[12.5]);

		FileEntryWithEmbeddingsMember? asEmbeddingsBuild =
			MemoryPackSerializer.Deserialize<FileEntryWithEmbeddingsMember>(payload);
		Assert.NotNull(asEmbeddingsBuild);
		Assert.Equal(@"D:\media\old.mp4", asEmbeddingsBuild!._Path);
	}
}
