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
using System.Text;
using VDF.Core.ViewModels;

namespace VDF.Web.Services {
	/// <summary>
	/// CSV export of the results page. Same layout and encoding as the GUI export
	/// (MainWindowVM.WriteScanResultsCsv), including its Checked column, so one parser
	/// reads both.
	/// </summary>
	internal static class ResultsCsv {
		internal const string FileName = "vdf-results.csv";

		internal static byte[] Build(IEnumerable<DuplicateItem> items, IReadOnlySet<DuplicateItem> selected) {
			static string Escape(string? s) {
				s ??= string.Empty;
				return s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
					? "\"" + s.Replace("\"", "\"\"") + "\""
					: s;
			}
			var inv = CultureInfo.InvariantCulture;
			var sb = new StringBuilder();
			sb.AppendLine("GroupId,Path,SizeBytes,Duration,Resolution,Fps,BitrateKbs,AudioFormat,AudioSampleRate,Similarity,DateCreated,IsImage,Checked,AudioLanguages,SubtitleLanguages");
			// Keep group members on adjacent rows regardless of list order.
			foreach (var group in items.GroupBy(i => i.GroupId))
				foreach (var item in group)
					sb.AppendLine(string.Join(',',
						item.GroupId.ToString(),
						Escape(item.Path),
						item.SizeLong.ToString(inv),
						item.Duration.ToString(null, inv),
						Escape(item.FrameSize),
						item.Fps.ToString(inv),
						item.BitRateKbs.ToString(inv),
						Escape(item.AudioFormat),
						item.AudioSampleRate.ToString(inv),
						item.Similarity.ToString(inv),
						item.DateCreated.ToString("yyyy-MM-dd HH:mm:ss", inv),
						item.IsImage.ToString(),
						selected.Contains(item).ToString(),
						Escape(item.AudioLanguages),
						Escape(item.SubtitleLanguages)));
			// UTF-8 BOM so Excel detects the encoding.
			var utf8 = Encoding.UTF8;
			return [.. utf8.GetPreamble(), .. utf8.GetBytes(sb.ToString())];
		}
	}
}
