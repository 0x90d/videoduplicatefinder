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

public class FileEntryEmbeddingsTests {

	[Fact]
	public void Embeddings_RoundTripThroughMemoryPack() {
		var entry = new FileEntry { Folder = @"D:\media" };
		entry.Path = @"D:\media\clip.mp4";
		entry.grayBytes[12.5] = new byte[] { 1, 2, 3 };
		entry.Embeddings[12.5] = new byte[] { 10, 20, 30 };
		entry.Embeddings[37.5] = null; // embed failure marker must survive too

		byte[] payload = MemoryPackSerializer.Serialize(entry);
		FileEntry? restored = MemoryPackSerializer.Deserialize<FileEntry>(payload);

		Assert.NotNull(restored);
		Assert.Equal(new byte[] { 10, 20, 30 }, restored!.Embeddings[12.5]);
		Assert.True(restored.Embeddings.ContainsKey(37.5));
		Assert.Null(restored.Embeddings[37.5]);
	}

	[Fact]
	public void EntryWithoutEmbeddings_DeserializesWithEmptyDictionary() {
		// The field is version-tolerant append-only: an entry that never had embeddings
		// (or came from an older database) must come back with a usable empty dictionary.
		var entry = new FileEntry { Folder = @"D:\media" };
		entry.Path = @"D:\media\old.mp4";

		FileEntry? restored = MemoryPackSerializer.Deserialize<FileEntry>(MemoryPackSerializer.Serialize(entry));

		Assert.NotNull(restored);
		Assert.NotNull(restored!.Embeddings);
		Assert.Empty(restored.Embeddings);
	}
}
