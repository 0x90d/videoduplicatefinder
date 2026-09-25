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
	/// The one date the results work with: shown in the Size · Date column, sorted by, and
	/// used by Check oldest/newest and the custom selection's date rule. Created by default,
	/// modified when <see cref="SettingsFile.ResultsShowDateModified"/> is on (#907).
	/// </summary>
	static class ResultsDates {
		public static DateTime Of(DuplicateItem item) => Of(item, SettingsFile.Instance.ResultsShowDateModified);

		/// <summary>Results saved before the modified date was recorded fall back to the created date.</summary>
		internal static DateTime Of(DuplicateItem item, bool modified) =>
			modified && item.DateModified != default ? item.DateModified : item.DateCreated;
	}
}
