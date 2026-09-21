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

using VDF.Core;
using VDF.Core.ViewModels;

namespace VDF.Web.Services {
	/// <summary>
	/// The frame endpoints of the results page. Both serve only files of the current
	/// results, at the position the page asks for with "t" (seconds); without one they
	/// serve the default frame, see <see cref="FramePositions"/>.
	/// </summary>
	internal static class ThumbnailEndpoints {
		/// <summary>
		/// HQ thumbnail — extracts a fresh frame using configurable resolution and quality.
		/// Used by the card-based results view for crisp thumbnails.
		/// </summary>
		public static async Task Hq(HttpContext ctx, ScanService scan, WebSettingsService webSettings) {
			if (!TryFindItem(ctx, scan, out var item)) return;

			// Honor the w/q the page requested (falling back to the current settings) so
			// cached browser URLs stay consistent with the bytes they were rendered from.
			int width = int.TryParse(ctx.Request.Query["w"], out int w) ? w : webSettings.ThumbnailWidth;
			int quality = int.TryParse(ctx.Request.Query["q"], out int q) ? q : webSettings.ThumbnailJpegQuality;
			width = Math.Clamp(width, 48, 960);
			quality = Math.Clamp(quality, 10, 95);

			var position = FramePositions.Resolve(item, ctx.Request.Query["t"], scan.Settings);
			string cacheKey = $"{item.Path}|{position.TotalSeconds:F2}|{width}|{quality}";

			if (!scan.HqThumbCache.TryGetValue(cacheKey, out var jpeg)) {
				// FFmpeg encodes at the requested quality directly — no re-encode pass needed.
				jpeg = await Task.Run(() => ScanEngine.ExtractThumbnailJpeg(item.Path, position, width, quality));
				if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }
				if (scan.HqThumbCache.Count >= 4096)
					scan.HqThumbCache.Clear();
				scan.HqThumbCache.TryAdd(cacheKey, jpeg);
			}

			await WriteJpeg(ctx, jpeg);
		}

		/// <summary>Full-resolution frame — extracts at original resolution for the comparison modal.</summary>
		public static async Task Full(HttpContext ctx, ScanService scan) {
			if (!TryFindItem(ctx, scan, out var item)) return;

			var position = FramePositions.Resolve(item, ctx.Request.Query["t"], scan.Settings);
			string cacheKey = $"{item.Path}|{position.TotalSeconds:F2}|full";

			if (!scan.FullThumbCache.TryGetValue(cacheKey, out var jpeg)) {
				jpeg = await Task.Run(() => ScanEngine.ExtractThumbnailJpeg(item.Path, position, 0));
				if (jpeg == null || jpeg.Length == 0) { ctx.Response.StatusCode = 204; return; }
				// Full-resolution frames are megabytes each — keep this cache small.
				if (scan.FullThumbCache.Count >= 64)
					scan.FullThumbCache.Clear();
				scan.FullThumbCache.TryAdd(cacheKey, jpeg);
			}

			await WriteJpeg(ctx, jpeg);
		}

		static bool TryFindItem(HttpContext ctx, ScanService scan, out DuplicateItem item) {
			item = null!;
			string? path = ctx.Request.Query["path"];
			if (string.IsNullOrEmpty(path)) { ctx.Response.StatusCode = 400; return false; }

			path = Path.GetFullPath(path);
			var found = scan.Duplicates.FirstOrDefault(d => d.Path == path);
			if (found == null) { ctx.Response.StatusCode = 404; return false; }
			item = found;
			return true;
		}

		static async Task WriteJpeg(HttpContext ctx, byte[] jpeg) {
			ctx.Response.ContentType = "image/jpeg";
			ctx.Response.Headers.CacheControl = "public, max-age=3600";
			await ctx.Response.Body.WriteAsync(jpeg);
		}
	}
}
