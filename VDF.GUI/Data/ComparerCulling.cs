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

namespace VDF.GUI.Data {

	/// <summary>
	/// Winner-stays pair walk over a group (redesign stage 4, locked decision 10):
	/// the current keeper sits LEFT, challengers walk through the remaining files in
	/// order. Keep-left checks the challenger; keep-right crowns the challenger and
	/// checks the old keeper; skip decides nothing. N files = N-1 pairs.
	/// Pure state machine — item checking is the caller's business.
	/// </summary>
	public sealed class CullingPairFlow {
		public enum Decision { KeepLeft, KeepRight, Skip }
		/// <summary>CheckIndex/KeepIndex are -1 when the decision touched nothing (skip).</summary>
		public readonly record struct StepResult(int CheckIndex, int KeepIndex, bool GroupFinished);

		public int Count { get; }
		/// <summary>Current keeper, shown in the left pane.</summary>
		public int LeftIndex { get; private set; }
		/// <summary>Current challenger, shown in the right pane.</summary>
		public int RightIndex { get; private set; }

		public int PairCount => Math.Max(Count - 1, 0);
		/// <summary>1-based position of the walk; the challenger cursor.</summary>
		public int PairNumber => Math.Min(Math.Max(LeftIndex, RightIndex), Math.Max(PairCount, 1));
		public bool HasPair => Count >= 2 && RightIndex < Count;

		public CullingPairFlow(int count) {
			Count = Math.Max(count, 0);
			LeftIndex = 0;
			RightIndex = Count >= 2 ? 1 : Count;
		}

		public StepResult Advance(Decision decision) {
			if (!HasPair)
				return new StepResult(-1, -1, true);

			int check = -1, keep = -1;
			switch (decision) {
				case Decision.KeepLeft:
					check = RightIndex;
					keep = LeftIndex;
					break;
				case Decision.KeepRight:
					check = LeftIndex;
					keep = RightIndex;
					LeftIndex = RightIndex;
					break;
			}
			RightIndex = Math.Max(LeftIndex, RightIndex) + 1;
			return new StepResult(check, keep, RightIndex >= Count);
		}

		/// <summary>Manually picking files re-anchors the walk at that pair.</summary>
		public void SetPair(int leftIndex, int rightIndex) {
			if (leftIndex < 0 || rightIndex < 0 || leftIndex >= Count || rightIndex >= Count || leftIndex == rightIndex)
				return;
			LeftIndex = leftIndex;
			RightIndex = rightIndex;
		}
	}

	public enum ChipState { Neutral, Better, Worse }

	public sealed record MetaChip(string Text, ChipState State) {
		public bool IsNeutral => State == ChipState.Neutral;
		public bool IsBetter => State == ChipState.Better;
		public bool IsWorse => State == ChipState.Worse;
	}

	/// <summary>
	/// The metadata chip row under each comparer pane. Strictly better values against
	/// the OTHER pane turn green, strictly worse red, equal stays neutral (mockup
	/// note: "equal values stay neutral"). Only resolution, size and HDR are judged —
	/// duration/codec/audio/date have no objective better.
	/// </summary>
	public static class ComparerChips {

		public static List<MetaChip> Build(DuplicateItem item, DuplicateItem? other) {
			var chips = new List<MetaChip>();

			if (!string.IsNullOrEmpty(item.FrameSize))
				chips.Add(new(item.FrameSize!, Compare(item.FrameSizeInt, other?.FrameSizeInt)));

			chips.Add(new(item.Size, Compare(item.SizeLong, other?.SizeLong)));

			if (!item.IsImage) {
				chips.Add(new(FormatDuration(item.Duration), ChipState.Neutral));

				string codec = item.Format ?? string.Empty;
				if (item.Fps > 0)
					codec = codec.Length > 0 ? $"{codec} · {item.Fps:0.##} fps" : $"{item.Fps:0.##} fps";
				if (codec.Length > 0)
					chips.Add(new(codec, ChipState.Neutral));

				if (!string.IsNullOrEmpty(item.AudioFormat)) {
					string audio = item.AudioFormat!;
					if (!string.IsNullOrEmpty(item.AudioChannel))
						audio += $" {item.AudioChannel}";
					if (item.AudioSampleRate > 0)
						audio += $" · {item.AudioSampleRate / 1000d:0.#} kHz";
					chips.Add(new(audio, ChipState.Neutral));
				}

				if (!string.IsNullOrEmpty(item.HdrFormat))
					chips.Add(new(item.HdrFormat, Compare(item.HdrFormatRank, other?.HdrFormatRank)));
			}

			if (item.DateCreated != default)
				chips.Add(new(item.DateCreated.ToString("d"), ChipState.Neutral));

			return chips;
		}

		internal static string FormatDuration(TimeSpan duration) =>
			duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");

		static ChipState Compare(long value, long? other) {
			if (other is null || value == other.Value) return ChipState.Neutral;
			return value > other.Value ? ChipState.Better : ChipState.Worse;
		}
	}
}
