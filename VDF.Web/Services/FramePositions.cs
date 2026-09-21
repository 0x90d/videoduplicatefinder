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
using VDF.Core;
using VDF.Core.ViewModels;

namespace VDF.Web.Services {
	/// <summary>
	/// Which moment of a file the results page and the compare window show. The Web UI
	/// extracts frames on demand instead of keeping the scan's thumbnails, so the
	/// positions are computed here from the same definition the scan samples with.
	/// </summary>
	public static class FramePositions {
		/// <summary>
		/// A partial clip was not matched on the sampled frames, so their count says nothing
		/// about it. It gets at least the three moments the visual verification looks at
		/// (25, 50 and 75 percent of the clip).
		/// </summary>
		const int MinPartialFrames = 3;
		/// <summary>Seeking onto the very end of a file yields no frame.</summary>
		const double EndMarginSeconds = 0.1;

		/// <summary>The clip of a partial match, or the source it was found in (which carries no flag).</summary>
		static bool IsPartialPair(DuplicateItem a, DuplicateItem b) =>
			a.Flags.HasFlag(DuplicateFlags.PartialClip) || b.Flags.HasFlag(DuplicateFlags.PartialClip);

		/// <summary>Where the item starts on the timeline of its group's source video.</summary>
		static double TimelineOffset(DuplicateItem item) =>
			item.Flags.HasFlag(DuplicateFlags.PartialClip) ? item.PartialClipOffset.TotalSeconds : 0d;

		/// <summary>How many frames the compare window can step through for this pair.</summary>
		public static int FrameCount(DuplicateItem a, DuplicateItem b, Settings settings) {
			if (a.IsImage || b.IsImage) return 1;
			int sampled = Math.Max(1, settings.ThumbnailCount);
			return IsPartialPair(a, b) ? Math.Max(MinPartialFrames, sampled) : sampled;
		}

		/// <summary>The frame a pair opens on: the middle one, furthest from intros and credits.</summary>
		public static int MiddleFrame(int frameCount) => Math.Max(0, (frameCount - 1) / 2);

		/// <summary>
		/// The moment the scan sampled at a relative position, as FileEntry.GetGrayBytesIndex
		/// computes it: within the sampling limit when one is set.
		/// </summary>
		static TimeSpan Sampled(DuplicateItem item, float position, Settings settings) {
			if (item.IsImage) return TimeSpan.Zero;
			double seconds = item.Duration.TotalSeconds;
			if (settings.MaxSamplingDurationSeconds > 0d && seconds > settings.MaxSamplingDurationSeconds)
				seconds = settings.MaxSamplingDurationSeconds;
			return Clamp(item, seconds * position);
		}

		static TimeSpan Clamp(DuplicateItem item, double seconds) {
			if (item.IsImage) return TimeSpan.Zero;
			double last = Math.Max(0d, item.Duration.TotalSeconds - EndMarginSeconds);
			return TimeSpan.FromSeconds(Math.Clamp(seconds, 0d, last));
		}

		/// <summary>The frame shown where only one is: the middle one of the sampled frames.</summary>
		public static TimeSpan DefaultTimestamp(DuplicateItem item, Settings settings) {
			if (item.IsImage) return TimeSpan.Zero;
			int count = Math.Max(1, settings.ThumbnailCount);
			return Sampled(item, ScanEngine.BuildSamplePositions(count)[MiddleFrame(count)], settings);
		}

		/// <summary>
		/// The two moments frame <paramref name="index"/> of a pair shows. Duplicates were
		/// compared at the same relative position of each file, so that is what they show.
		/// A partial clip lies somewhere inside its source: both sides are placed on the
		/// source's timeline and show the same moment of the part they share, which shifts
		/// the source by the clip's offset. Two clips of one source that share nothing fall
		/// back to relative positions.
		/// </summary>
		public static (TimeSpan A, TimeSpan B) AlignedPair(DuplicateItem a, DuplicateItem b, int index, Settings settings) {
			int count = FrameCount(a, b, settings);
			float position = ScanEngine.BuildSamplePositions(count)[Math.Clamp(index, 0, count - 1)];

			if (IsPartialPair(a, b) && !a.IsImage && !b.IsImage) {
				double offsetA = TimelineOffset(a), offsetB = TimelineOffset(b);
				double start = Math.Max(offsetA, offsetB);
				double end = Math.Min(offsetA + a.Duration.TotalSeconds, offsetB + b.Duration.TotalSeconds);
				if (end > start) {
					double moment = start + (end - start) * position;
					return (Clamp(a, moment - offsetA), Clamp(b, moment - offsetB));
				}
			}
			return (Sampled(a, position, settings), Sampled(b, position, settings));
		}

		/// <summary>
		/// The position a thumbnail request asks for: its "t" (seconds, invariant culture)
		/// kept inside the file, or the default frame when there is none or it is not a number.
		/// </summary>
		public static TimeSpan Resolve(DuplicateItem item, string? requestedSeconds, Settings settings) {
			if (!string.IsNullOrEmpty(requestedSeconds) &&
				double.TryParse(requestedSeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
				double.IsFinite(seconds))
				return Clamp(item, seconds);
			return DefaultTimestamp(item, settings);
		}

		/// <summary>The "t" query value for a position, as <see cref="Resolve"/> reads it back.</summary>
		public static string ToQueryValue(TimeSpan position) =>
			position.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture);
	}
}
