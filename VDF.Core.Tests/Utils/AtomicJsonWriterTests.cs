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

using System.Text.Json;
using System.Text.Json.Serialization;
using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

sealed record AtomicWriterPayload(string Name, int Count);

sealed class AtomicWriterRecursive {
	public AtomicWriterRecursive? Self { get; set; }
}

[JsonSerializable(typeof(AtomicWriterPayload))]
[JsonSerializable(typeof(AtomicWriterRecursive))]
partial class AtomicWriterTestJsonContext : JsonSerializerContext { }

public class AtomicJsonWriterTests : IDisposable {
	readonly string _dir;

	public AtomicJsonWriterTests() {
		_dir = Path.Combine(Path.GetTempPath(), "VDF.AtomicJsonWriter." + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
	}

	public void Dispose() {
		try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
	}

	[Fact]
	public async Task RoundTrip_WritesValidJson() {
		string path = Path.Combine(_dir, "data.json");
		var value = new AtomicWriterPayload("hello", 42);

		await AtomicJsonWriter.WriteAsync(path, value, AtomicWriterTestJsonContext.Default.AtomicWriterPayload);

		Assert.True(File.Exists(path));
		var read = JsonSerializer.Deserialize<AtomicWriterPayload>(await File.ReadAllTextAsync(path));
		Assert.Equal(value, read);
	}

	[Fact]
	public async Task LeavesNoTempFile_OnSuccess() {
		string path = Path.Combine(_dir, "data.json");
		await AtomicJsonWriter.WriteAsync(path, new AtomicWriterPayload("ok", 1), AtomicWriterTestJsonContext.Default.AtomicWriterPayload);
		Assert.False(File.Exists(path + ".tmp"));
	}

	[Fact]
	public async Task OverwritesExistingFile() {
		string path = Path.Combine(_dir, "data.json");
		await File.WriteAllTextAsync(path, "stale contents that should be replaced");

		await AtomicJsonWriter.WriteAsync(path, new AtomicWriterPayload("new", 7), AtomicWriterTestJsonContext.Default.AtomicWriterPayload);

		var read = JsonSerializer.Deserialize<AtomicWriterPayload>(await File.ReadAllTextAsync(path));
		Assert.Equal(new AtomicWriterPayload("new", 7), read);
	}

	[Fact]
	public async Task FailedSerialize_LeavesOriginalUntouched_AndCleansTemp() {
		string path = Path.Combine(_dir, "data.json");
		await File.WriteAllTextAsync(path, "{\"name\":\"original\",\"count\":1}");

		// JsonSerializer cannot serialize an unbounded recursion: build one.
		var bad = new AtomicWriterRecursive();
		bad.Self = bad;

		await Assert.ThrowsAnyAsync<Exception>(async () =>
			await AtomicJsonWriter.WriteAsync(path, bad, AtomicWriterTestJsonContext.Default.AtomicWriterRecursive));

		Assert.True(File.Exists(path));
		Assert.Contains("original", await File.ReadAllTextAsync(path));
		Assert.False(File.Exists(path + ".tmp"));
	}
}
