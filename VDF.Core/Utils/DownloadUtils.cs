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

using System.Globalization;
using System.Net.Http;

namespace VDF.Core.Utils {
	/// <summary>
	/// Shared HTTP file download used by the AI component and FFmpeg downloaders.
	/// With ResponseHeadersRead, HttpClient.Timeout only covers the headers — the body
	/// reads must be guarded separately, or a connection that stalls mid-transfer
	/// (no data, no FIN) hangs forever; these downloads run behind the GUI's modal
	/// busy overlay, so "forever" meant killing the app.
	/// </summary>
	internal static class DownloadUtils {
		internal static readonly TimeSpan ReadStallTimeout = TimeSpan.FromSeconds(90);

		/// <param name="onProgress">Invoked after every chunk with (bytesDone, bytesTotal).</param>
		/// <param name="maxBytes">Fail with HttpRequestException when the advertised or received size exceeds this; 0 = unlimited.</param>
		/// <param name="stallTimeout">Test hook; production callers leave the 90 s default.</param>
		internal static async Task DownloadFileAsync(HttpClient http, Uri url, string destination, string displayName,
			Action<long, long?>? onProgress, CancellationToken token, long maxBytes = 0, TimeSpan? stallTimeout = null) {
			TimeSpan readStallTimeout = stallTimeout ?? ReadStallTimeout;
			using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");
			long? total = response.Content.Headers.ContentLength;
			if (maxBytes > 0 && total > maxBytes)
				throw new HttpRequestException($"Download too large ({total} bytes, max {maxBytes})");
			await using Stream source = await response.Content.ReadAsStreamAsync(token);
			await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
			var buffer = new byte[81920];
			long readTotal = 0;
			while (true) {
				int read;
				using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(token)) {
					readCts.CancelAfter(readStallTimeout);
					try {
						read = await source.ReadAsync(buffer, readCts.Token);
					}
					catch (OperationCanceledException) when (!token.IsCancellationRequested) {
						throw new TimeoutException($"The {displayName} download stalled (no data received for {readStallTimeout.TotalSeconds:0} seconds).");
					}
				}
				if (read == 0)
					break;
				await target.WriteAsync(buffer.AsMemory(0, read), token);
				readTotal += read;
				if (maxBytes > 0 && readTotal > maxBytes)
					throw new HttpRequestException($"Download exceeded size limit ({maxBytes} bytes)");
				onProgress?.Invoke(readTotal, total);
			}
			onProgress?.Invoke(readTotal, total);
		}

		internal static string FormatBytes(long? bytes) {
			if (bytes == null) return "?";
			double size = bytes.Value;
			string[] units = { "B", "KB", "MB", "GB" };
			int unit = 0;
			while (size >= 1024 && unit < units.Length - 1) {
				size /= 1024;
				unit++;
			}
			return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unit]);
		}
	}
}
