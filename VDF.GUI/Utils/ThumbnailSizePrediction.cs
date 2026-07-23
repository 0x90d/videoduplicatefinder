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

using VDF.Core.ViewModels;

namespace VDF.GUI.Utils {
	/// <summary>
	/// Predicted pixel size and grid layout of a row's composite preview BEFORE its
	/// bitmap has loaded. The extracted frames' size is fully determined by the media's
	/// FrameSize and the thumbnail settings (downscale-only fit into the max-width box,
	/// mirroring FfmpegEngine.ScaleToMaxWidth), and the storage grid is chosen by the
	/// same ThumbnailGridLayout call the loader uses - so a row can reserve its FINAL
	/// height immediately. Rows used to be sized by a flat estimate until the composite
	/// landed and then snap to the real aspect; with thumbnails resolving asynchronously
	/// above the viewport that shifted the scroll offset and the whole list bounced
	/// under the user (#862). Pure math, unit-tested.
	/// </summary>
	public sealed record ThumbnailSizePrediction(double Width, double Height, int FrameCount, int GridColumns) {

		/// <summary>
		/// <paramref name="knownFrameCount"/>/<paramref name="knownGridColumns"/> are the
		/// persisted composite geometry (restored results); when unset the frame count
		/// falls back to the configured thumbnail count (images always have one frame).
		/// Returns null when the media's frame size is unknown - the caller keeps the
		/// classic flat estimate then.
		/// </summary>
		internal static ThumbnailSizePrediction? For(DuplicateItem? item, int knownFrameCount, int knownGridColumns,
			int configuredThumbnailCount, int thumbnailMaxWidth) {
			string? frameSize = item?.FrameSize;
			if (item == null || string.IsNullOrEmpty(frameSize)) return null;
			int split = frameSize.IndexOf('x');
			if (split <= 0
				|| !int.TryParse(frameSize.AsSpan(0, split), out int mediaWidth)
				|| !int.TryParse(frameSize.AsSpan(split + 1), out int mediaHeight)
				|| mediaWidth <= 0 || mediaHeight <= 0)
				return null;

			int frameCount = knownFrameCount > 0 ? knownFrameCount
				: item.IsImage ? 1
				: Math.Max(1, configuredThumbnailCount);
			int columns = knownFrameCount > 0 && knownGridColumns >= 1
				? knownGridColumns
				: frameCount > 1 ? ThumbnailGridLayout.Columns(frameCount, (double)mediaWidth / mediaHeight) : 1;

			// Mirror of the extractor's downscale-only fit into the max-width box
			// (FfmpegEngine.ScaleToMaxWidth): small sources keep their natural size (#787).
			double frameWidth = mediaWidth, frameHeight = mediaHeight;
			if (thumbnailMaxWidth > 0 && (mediaWidth > thumbnailMaxWidth || mediaHeight > thumbnailMaxWidth)) {
				double factor = Math.Max(mediaWidth / (double)thumbnailMaxWidth, mediaHeight / (double)thumbnailMaxWidth);
				frameWidth = Math.Max(1, (int)Math.Round(mediaWidth / factor));
				frameHeight = Math.Max(1, (int)Math.Round(mediaHeight / factor));
			}

			int rows = ThumbnailGridLayout.Rows(frameCount, columns);
			return new ThumbnailSizePrediction(columns * frameWidth, rows * frameHeight, frameCount, columns);
		}
	}
}
