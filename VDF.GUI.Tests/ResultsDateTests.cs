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
// #907: copying or downloading a file sets its creation date to that moment, so the
// results can show and use the modified date instead.

using System.Globalization;
using VDF.Core.ViewModels;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;

namespace VDF.GUI.Tests {
	public class ResultsDateTests {
		static readonly DateTime Copied = new(2026, 9, 1, 12, 0, 0);
		static readonly DateTime Recorded = new(2014, 6, 15, 18, 30, 0);

		static DuplicateItemVM Item(Guid group, string path, DateTime created, DateTime modified) => new() {
			ItemInfo = new DuplicateItem {
				GroupId = group, Path = path, SizeLong = 100, Similarity = 100f,
				DateCreated = created, DateModified = modified, Duration = TimeSpan.FromMinutes(1),
			}
		};

		static void WithSetting(bool modified, Action body) {
			bool previous = SettingsFile.Instance.ResultsShowDateModified;
			SettingsFile.Instance.ResultsShowDateModified = modified;
			try { body(); }
			finally { SettingsFile.Instance.ResultsShowDateModified = previous; }
		}

		[Fact]
		public void Default_IsTheCreatedDate() =>
			WithSetting(false, () => Assert.Equal(Copied, Item(Guid.Empty, "a", Copied, Recorded).ShownDate));

		[Fact]
		public void Setting_SwitchesToTheModifiedDate() =>
			WithSetting(true, () => Assert.Equal(Recorded, Item(Guid.Empty, "a", Copied, Recorded).ShownDate));

		[Fact]
		public void ResultsSavedBeforeTheModifiedDateExisted_FallBackToCreated() =>
			WithSetting(true, () => Assert.Equal(Copied, Item(Guid.Empty, "a", Copied, default).ShownDate));

		[Fact]
		public void DateSort_FollowsTheSetting() {
			Guid g = Guid.NewGuid();
			// a was copied first but recorded last; b the other way round.
			var a = Item(g, "a", created: new DateTime(2026, 1, 1), modified: new DateTime(2020, 1, 1));
			var b = Item(g, "b", created: new DateTime(2026, 2, 1), modified: new DateTime(2010, 1, 1));
			ResultsBuildRequest Request() => new() {
				Items = new[] { a, b }, SortMode = ResultsSortMode.DateCreated, SortDescending = true,
				IsTombstone = _ => false, IsOffline = _ => false,
			};

			WithSetting(false, () => Assert.Equal(new[] { "b", "a" },
				ResultsListBuilder.Build(Request()).Groups[0].Rows.Select(r => r.Item.ItemInfo.Path)));
			WithSetting(true, () => Assert.Equal(new[] { "a", "b" },
				ResultsListBuilder.Build(Request()).Groups[0].Rows.Select(r => r.Item.ItemInfo.Path)));
		}

		[Fact]
		public void DetailsLine_ShowsTheChosenDate() {
			var item = Item(Guid.Empty, "a", Copied, Recorded).ItemInfo;
			WithSetting(true, () => Assert.Contains(Recorded.ToString("g", CultureInfo.InvariantCulture),
				ResultsBadgeRules.BuildFileLine(item, CultureInfo.InvariantCulture)));
		}
	}
}
