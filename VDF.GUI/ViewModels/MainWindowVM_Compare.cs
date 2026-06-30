// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Text.Json.Nodes;
using ReactiveUI;
using VDF.Core.Utils;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	// "순회 비교": 현재 보이는(필터/정렬 적용) 중복 그룹을 매니페스트로 써서 GridPlayer(mpv 포크)에 넘긴다.
	// 실시간 나란한 재생·PgUp/PgDn 순회·DEL 삭제는 GridPlayer ComparisonManager 가 전부 처리 → VDF 쪽엔 런처만.
	// 계약: pythonw -m gridplayer --new-window <파일>.gpcompare.json  /  매니페스트 {"groups":[[path,...],...]}
	public partial class MainWindowVM {
		// ponytail: 이 머신 전용 통합이라 GridPlayer venv 경로 하드코딩. 옮기면 이 한 줄만 수정.
		const string GridPlayerPythonW = @"C:\Users\geech\dev\gridplayer\.venv\Scripts\pythonw.exe";

		public ReactiveCommand<Unit, Unit> CompareInPlayerCommand => ReactiveCommand.CreateFromTask(async () => {
			// 보이는 목록(필터/정렬 반영) = DataGridCollectionView 열거; 미초기화면 IsVisibleInFilter 폴백.
			IEnumerable<DuplicateItemVM> visible = view is not null
				? view.OfType<DuplicateItemVM>()
				: Duplicates.Where(d => d.IsVisibleInFilter);

			List<List<string>> groups = visible
				.Where(d => !d.ItemInfo.IsImage)                       // 영상만(GridPlayer 재생 대상)
				.GroupBy(d => d.ItemInfo.GroupId)                      // VDF 중복 그룹 = 한 비교 세트
				.Select(g => g.Select(d => d.ItemInfo.Path)
							  .Where(File.Exists)
							  .Distinct(StringComparer.OrdinalIgnoreCase)
							  .ToList())
				.Where(paths => paths.Count >= 2)                      // 2편+만 비교 의미 있음
				.ToList();

			if (groups.Count == 0) {
				await MessageBoxService.Show(App.Lang["Message.CompareNothingToCompare"]);
				return;
			}
			if (!File.Exists(GridPlayerPythonW)) {
				await MessageBoxService.Show(string.Format(App.Lang["Message.CompareGridPlayerMissing"], GridPlayerPythonW));
				return;
			}

			JsonArray groupsArr = new();
			foreach (List<string> g in groups) {
				JsonArray inner = new();
				foreach (string p in g)
					inner.Add(p);
				groupsArr.Add(inner);
			}
			string manifest = Path.Combine(Path.GetTempPath(), "vdf_compare.gpcompare.json");
			try {
				// utf-8(BOM 무); GridPlayer 는 utf-8-sig 로 읽어 BOM 유무 모두 허용.
				File.WriteAllText(manifest, new JsonObject { ["groups"] = groupsArr }.ToJsonString(), new UTF8Encoding(false));
				Process.Start(new ProcessStartInfo {
					FileName = GridPlayerPythonW,
					UseShellExecute = false,
					ArgumentList = { "-m", "gridplayer", "--new-window", manifest },
				});
			}
			catch (Exception ex) {
				Logger.Instance.Info($"Compare-in-player launch failed: {ex.Message}");
			}
		});

		// 순수 로직 자가검증(빌드와 별개, 수동): 그룹핑 규칙이 깨지면 실패.
		// dotnet 단위테스트 프로젝트 없이 확인하려면 호출부에서 BuildCompareGroups 를 직접 쓰면 됨.
		internal static List<List<string>> BuildCompareGroups(
			IEnumerable<(Guid group, string path, bool isImage)> items, Func<string, bool> exists) =>
			items.Where(i => !i.isImage)
				 .GroupBy(i => i.group)
				 .Select(g => g.Select(i => i.path).Where(exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
				 .Where(p => p.Count >= 2)
				 .ToList();
	}
}
