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

using System.Net;
using System.Net.Http;
using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class DownloadUtilsTests : IDisposable {
	readonly string tempDir;

	public DownloadUtilsTests() {
		tempDir = Path.Combine(Path.GetTempPath(), $"VDF.DownloadUtilsTests.{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
	}

	public void Dispose() {
		try { Directory.Delete(tempDir, true); } catch { }
	}

	string TempFile() => Path.Combine(tempDir, Guid.NewGuid().ToString("N"));

	sealed class StubHandler : HttpMessageHandler {
		readonly Func<HttpResponseMessage> responseFactory;
		public StubHandler(Func<HttpResponseMessage> responseFactory) => this.responseFactory = responseFactory;
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(responseFactory());
	}

	/// <summary>Non-seekable stream whose reads never complete until the read is canceled.</summary>
	sealed class StallingStream : Stream {
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return 0;
		}
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	/// <summary>Non-seekable stream that yields zero bytes forever (unknown content length).</summary>
	sealed class EndlessStream : Stream {
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => count;
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(buffer.Length);
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	[Fact]
	public async Task DownloadFileAsync_WritesContentAndReportsProgress() {
		byte[] payload = new byte[200_000];
		new Random(42).NextBytes(payload);
		using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) {
			Content = new ByteArrayContent(payload)
		}));
		string destination = TempFile();
		long lastDone = 0;
		long? reportedTotal = null;

		await DownloadUtils.DownloadFileAsync(http, new Uri("http://localhost/test.bin"), destination, "test",
			(done, total) => { lastDone = done; reportedTotal = total; }, CancellationToken.None);

		Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
		Assert.Equal(payload.Length, lastDone);
		Assert.Equal(payload.Length, reportedTotal);
	}

	[Fact]
	public async Task DownloadFileAsync_ThrowsOnNonSuccessStatus() {
		using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.NotFound)));
		var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
			DownloadUtils.DownloadFileAsync(http, new Uri("http://localhost/missing.bin"), TempFile(), "test", null, CancellationToken.None));
		Assert.Contains("404", ex.Message);
	}

	[Fact]
	public async Task DownloadFileAsync_RejectsAdvertisedOversize() {
		var content = new StreamContent(new EndlessStream());
		content.Headers.ContentLength = 10_000;
		using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

		var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
			DownloadUtils.DownloadFileAsync(http, new Uri("http://localhost/big.bin"), TempFile(), "test", null, CancellationToken.None, maxBytes: 1024));
		Assert.Contains("too large", ex.Message);
	}

	[Fact]
	public async Task DownloadFileAsync_RejectsStreamedOversizeWithUnknownLength() {
		// No Content-Length header — the cap must trip while streaming.
		using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) {
			Content = new StreamContent(new EndlessStream())
		}));

		var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
			DownloadUtils.DownloadFileAsync(http, new Uri("http://localhost/big.bin"), TempFile(), "test", null, CancellationToken.None, maxBytes: 500_000));
		Assert.Contains("size limit", ex.Message);
	}

	[Fact]
	public async Task DownloadFileAsync_TurnsAMidTransferStallIntoTimeoutException() {
		// The regression the AI downloader fixed and the FFmpeg downloaders shared:
		// with ResponseHeadersRead a stalled body read hung forever behind the GUI's
		// modal busy overlay.
		using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) {
			Content = new StreamContent(new StallingStream())
		}));

		var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
			DownloadUtils.DownloadFileAsync(http, new Uri("http://localhost/stall.bin"), TempFile(), "test", null,
				CancellationToken.None, stallTimeout: TimeSpan.FromMilliseconds(100)));
		Assert.Contains("stalled", ex.Message);
	}

	[Fact]
	public async Task DownloadFileAsync_UserCancellationIsNotReportedAsAStall() {
		using var http = new HttpClient(new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) {
			Content = new StreamContent(new StallingStream())
		}));
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

		// A real cancel must surface as OperationCanceledException so callers can
		// distinguish "user aborted" from "network died".
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			DownloadUtils.DownloadFileAsync(http, new Uri("http://localhost/stall.bin"), TempFile(), "test", null,
				cts.Token, stallTimeout: TimeSpan.FromSeconds(30)));
	}

	[Theory]
	[InlineData(null, "?")]
	[InlineData(0L, "0 B")]
	[InlineData(1024L, "1 KB")]
	[InlineData(1536L, "1.5 KB")]
	[InlineData(104_857_600L, "100 MB")]
	[InlineData(3_221_225_472L, "3 GB")]
	public void FormatBytes_FormatsInvariant(long? bytes, string expected) =>
		Assert.Equal(expected, DownloadUtils.FormatBytes(bytes));
}
