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

using VDF.Core.FFTools;

namespace VDF.Core.Utils {
	/// <summary>
	/// Loads still images into an ImageSharp <see cref="Image"/>. Formats whose codec ImageSharp
	/// cannot decode natively (HEIC/HEIF — backed by HEVC, which is patent-encumbered and has no
	/// managed decoder) are first transcoded to JPEG via FFmpeg, which VDF already bundles.
	/// Every other format is handed straight to ImageSharp.
	/// </summary>
	internal static class ImageLoader {
		/// <summary>Extensions whose codec ImageSharp cannot decode and which must go through FFmpeg.</summary>
		internal static readonly string[] FfmpegOnlyImageExtensions = { ".heic", ".heif" };

		/// <summary>True if the file must be decoded via FFmpeg rather than ImageSharp directly.</summary>
		internal static bool RequiresFfmpegDecoding(string path) {
			string ext = Path.GetExtension(path);
			foreach (var e in FfmpegOnlyImageExtensions)
				if (ext.Equals(e, StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}

		/// <summary>
		/// Loads <paramref name="path"/> into an ImageSharp image. Throws when the file cannot be
		/// decoded (matching <see cref="Image.Load(string)"/>) so existing callers' try/catch
		/// blocks treat a HEIC failure exactly like any other unreadable image.
		/// </summary>
		internal static Image Load(string path) {
			if (!RequiresFfmpegDecoding(path))
				return Image.Load(path);

			byte[]? jpeg = FfmpegEngine.ExtractThumbnailJpeg(path, TimeSpan.Zero);
			if (jpeg == null || jpeg.Length == 0)
				throw new InvalidDataException($"FFmpeg could not decode image '{path}'.");
			using var ms = new MemoryStream(jpeg);
			return Image.Load(ms);
		}
	}
}
