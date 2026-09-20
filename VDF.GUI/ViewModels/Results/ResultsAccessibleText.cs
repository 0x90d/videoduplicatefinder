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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using VDF.Core.ViewModels;

namespace VDF.GUI.ViewModels {

	/// <summary>Localizable words used in a results row's spoken text. Defaults are English.</summary>
	public sealed record RowSpeechWords {
		public string Best { get; init; } = "best";
		public string AlreadyDeleted { get; init; } = "already deleted";
		public string Offline { get; init; } = "offline";
		public string AiMatched { get; init; } = "AI match";
		public string Checked { get; init; } = "checked";
		public static readonly RowSpeechWords Default = new();
	}

	/// <summary>
	/// What a screen reader says for one row of the results list. A row is a grid of loose
	/// text cells to the eye; for a list item the reader speaks a single name, which without
	/// this was the view model's type name. Order follows what decides keep-or-delete: which
	/// file, how similar, how big, how good, then the flags, and the folder last because it is
	/// long and mostly repeats between rows.
	/// </summary>
	public static class ResultsAccessibleText {

		public static string DescribeItem(DuplicateItem info, bool isBest, bool isTombstone, bool isOffline,
			RowSpeechWords words, CultureInfo culture) {
			var parts = new List<string> {
				Path.GetFileName(info.Path),
				Data.ResultsBadgeRules.FormatSimilarity(info.Similarity, culture),
				info.Size,
			};
			if (!string.IsNullOrEmpty(info.FrameSize))
				parts.Add(info.FrameSize);
			if (!info.IsImage && info.Duration > TimeSpan.Zero)
				parts.Add(info.Duration.ToString(@"hh\:mm\:ss", culture));
			if (isBest) parts.Add(words.Best);
			if (info.IsAiMatched) parts.Add(words.AiMatched);
			if (isTombstone) parts.Add(words.AlreadyDeleted);
			if (isOffline) parts.Add(words.Offline);
			string? folder = Path.GetDirectoryName(info.Path);
			if (!string.IsNullOrEmpty(folder))
				parts.Add(folder);
			return string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
		}

		public static string DescribeGroup(ResultsGroupHeader header) =>
			string.Join(", ", new[] { header.Title, header.Summary, header.SimilarityRangeDisplay }
				.Where(p => !string.IsNullOrWhiteSpace(p)));

		public static string DescribeDetails(ResultsDetailsRow details) =>
			string.Join(", ", new[] { Path.GetFileName(details.Item.ItemInfo.Path), details.VideoText, details.AudioText, details.FileText }
				.Where(p => !string.IsNullOrWhiteSpace(p)));

		/// <summary>
		/// The checked state goes first: it changes while the row has focus (Space toggles it),
		/// and it is the one thing that must not be missed before a delete.
		/// </summary>
		public static string WithCheckedState(string name, bool isChecked, string checkedWord) =>
			isChecked ? $"{checkedWord}, {name}" : name;
	}
}
