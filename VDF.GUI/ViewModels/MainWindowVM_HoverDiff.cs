// /*
//     Copyright (C) 2025 0x90d
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

using System.Linq;
using ReactiveUI;

namespace VDF.GUI.ViewModels {
	public partial class MainWindowVM : ReactiveObject {

		Guid _lastHoveredGroupId;
		static string BestLabel => App.Lang["DuplicateList.HoverDiff.Best"];

		public void SetHoveredMetric(DuplicateItemVM item, string metric) {
			var groupId = item.ItemInfo.GroupId;
			_lastHoveredGroupId = groupId;
			var groupItems = Duplicates.Where(d => d.ItemInfo.GroupId == groupId).ToList();

			foreach (var gi in groupItems) {
				switch (metric) {
					case "duration":
						if (gi.ItemInfo.IsBestDuration)
							gi.DurationDiff = BestLabel;
						else {
							var bestDuration = groupItems.FirstOrDefault(i => i.ItemInfo.IsBestDuration)?.ItemInfo.Duration
								?? groupItems.Max(i => i.ItemInfo.Duration);
							var diff = gi.ItemInfo.Duration - bestDuration;
							gi.DurationDiff = FormatDurationDiff(diff);
						}
						break;

					case "framesize":
						if (gi.ItemInfo.IsBestFrameSize)
							gi.FrameSizeDiff = BestLabel;
						else {
							int bestPixels = groupItems.FirstOrDefault(i => i.ItemInfo.IsBestFrameSize)?.ItemInfo.FrameSizeInt
								?? groupItems.Max(i => i.ItemInfo.FrameSizeInt);
							if (bestPixels > 0) {
								double pct = (double)(gi.ItemInfo.FrameSizeInt - bestPixels) / bestPixels * 100;
								gi.FrameSizeDiff = FormatPercentDiff(pct);
							}
						}
						break;

					case "size":
						if (gi.ItemInfo.IsBestSize)
							gi.SizeDiff = BestLabel;
						else {
							long bestSize = groupItems.FirstOrDefault(i => i.ItemInfo.IsBestSize)?.ItemInfo.SizeLong
								?? groupItems.Max(i => i.ItemInfo.SizeLong);
							if (bestSize > 0) {
								double pct = (double)(gi.ItemInfo.SizeLong - bestSize) / bestSize * 100;
								gi.SizeDiff = FormatPercentDiff(pct);
							}
						}
						break;

					case "fps":
						if (gi.ItemInfo.IsBestFps)
							gi.FpsDiff = BestLabel;
						else {
							float bestFps = groupItems.FirstOrDefault(i => i.ItemInfo.IsBestFps)?.ItemInfo.Fps
								?? groupItems.Max(i => i.ItemInfo.Fps);
							if (bestFps > 0.01f) {
								double pct = (gi.ItemInfo.Fps - bestFps) / bestFps * 100;
								gi.FpsDiff = FormatPercentDiff(pct);
							}
						}
						break;

					case "bitrate":
						if (gi.ItemInfo.IsBestBitRateKbs)
							gi.BitRateDiff = BestLabel;
						else {
							decimal bestBitRate = groupItems.FirstOrDefault(i => i.ItemInfo.IsBestBitRateKbs)?.ItemInfo.BitRateKbs
								?? groupItems.Max(i => i.ItemInfo.BitRateKbs);
							if (bestBitRate > 0) {
								double pct = (double)(gi.ItemInfo.BitRateKbs - bestBitRate) / (double)bestBitRate * 100;
								gi.BitRateDiff = FormatPercentDiff(pct);
							}
						}
						break;

					case "audiosamplerate":
						if (gi.ItemInfo.IsBestAudioSampleRate)
							gi.AudioSampleRateDiff = BestLabel;
						else {
							int bestRate = groupItems.FirstOrDefault(i => i.ItemInfo.IsBestAudioSampleRate)?.ItemInfo.AudioSampleRate
								?? groupItems.Max(i => i.ItemInfo.AudioSampleRate);
							if (bestRate > 0) {
								double pct = (double)(gi.ItemInfo.AudioSampleRate - bestRate) / bestRate * 100;
								gi.AudioSampleRateDiff = FormatPercentDiff(pct);
							}
						}
						break;

					case "audiobitrate":
						if (gi.ItemInfo.IsBestAudioBitRateKbs)
							gi.AudioBitRateDiff = BestLabel;
						else {
							decimal bestAudioBitRate = groupItems.FirstOrDefault(i => i.ItemInfo.IsBestAudioBitRateKbs)?.ItemInfo.AudioBitRateKbs
								?? groupItems.Max(i => i.ItemInfo.AudioBitRateKbs);
							if (bestAudioBitRate > 0) {
								double pct = (double)(gi.ItemInfo.AudioBitRateKbs - bestAudioBitRate) / (double)bestAudioBitRate * 100;
								gi.AudioBitRateDiff = FormatPercentDiff(pct);
							}
						}
						break;
				}
			}
		}

		public void ClearHoveredMetric(DuplicateItemVM item) {
			var groupId = item.ItemInfo.GroupId;
			foreach (var gi in Duplicates.Where(d => d.ItemInfo.GroupId == groupId)) {
				gi.DurationDiff = null;
				gi.FrameSizeDiff = null;
				gi.SizeDiff = null;
				gi.FpsDiff = null;
				gi.BitRateDiff = null;
				gi.AudioSampleRateDiff = null;
				gi.AudioBitRateDiff = null;
			}
		}

		static string FormatPercentDiff(double pct) {
			if (Math.Abs(pct) < 0.5)
				return "=";
			return $"{pct:+0;-0}%";
		}

		static string FormatDurationDiff(TimeSpan diff) {
			if (diff == TimeSpan.Zero)
				return "=";
			string sign = diff < TimeSpan.Zero ? "-" : "+";
			var abs = diff.Duration();
			if (abs.TotalHours >= 1)
				return $"{sign}{(int)abs.TotalHours}h{abs.Minutes:D2}m{abs.Seconds:D2}s";
			if (abs.TotalMinutes >= 1)
				return $"{sign}{(int)abs.TotalMinutes}m{abs.Seconds:D2}s";
			return $"{sign}{abs.Seconds}s";
		}
	}
}
