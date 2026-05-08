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

using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	public sealed class BlacklistEntryVM {
		public HashSet<string> Source { get; }
		public string PathsDisplay { get; }
		public int Count => Source.Count;
		public BlacklistEntryVM(HashSet<string> source) {
			Source = source;
			PathsDisplay = string.Join(Environment.NewLine, source.OrderBy(p => p, StringComparer.Ordinal));
		}
	}

	public sealed class BlacklistManagerVM : ReactiveObject {
		readonly List<HashSet<string>> _liveList;
		readonly Func<Task> _persist;

		public ObservableCollection<BlacklistEntryVM> Entries { get; } = new();

		public ReactiveCommand<IList?, Unit> UnmarkSelectedCommand { get; }
		public ReactiveCommand<Unit, Unit> PruneMissingFilesCommand { get; }
		public ReactiveCommand<Unit, Unit> ClearAllCommand { get; }

		public BlacklistManagerVM(List<HashSet<string>> liveList, Func<Task> persist) {
			_liveList = liveList;
			_persist = persist;
			RebuildEntries();

			UnmarkSelectedCommand = ReactiveCommand.CreateFromTask<IList?>(UnmarkSelectedAsync);
			PruneMissingFilesCommand = ReactiveCommand.CreateFromTask(PruneMissingAsync);
			ClearAllCommand = ReactiveCommand.CreateFromTask(ClearAllAsync);
		}

		string _statusMessage = string.Empty;
		public string StatusMessage {
			get => _statusMessage;
			private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
		}

		void RebuildEntries() {
			Entries.Clear();
			foreach (var set in _liveList)
				Entries.Add(new BlacklistEntryVM(set));
			this.RaisePropertyChanged(nameof(GroupCountText));
			this.RaisePropertyChanged(nameof(IsEmpty));
		}

		public string GroupCountText =>
			string.Format(App.Lang["BlacklistManager.GroupCount"], _liveList.Count);

		public bool IsEmpty => _liveList.Count == 0;

		async Task UnmarkSelectedAsync(IList? selected) {
			if (selected == null || selected.Count == 0) return;
			var toRemove = selected.OfType<BlacklistEntryVM>().Select(e => e.Source).ToHashSet();
			_liveList.RemoveAll(s => toRemove.Contains(s));
			RebuildEntries();
			StatusMessage = string.Empty;
			await _persist();
		}

		async Task PruneMissingAsync() {
			int removed = BlacklistStore.PruneMissingFiles(_liveList);
			RebuildEntries();
			StatusMessage = removed > 0
				? string.Format(App.Lang["BlacklistManager.PruneResult"], removed)
				: App.Lang["BlacklistManager.NothingPruned"];
			if (removed > 0)
				await _persist();
		}

		async Task ClearAllAsync() {
			if (_liveList.Count == 0) return;
			var result = await MessageBoxService.Show(
				App.Lang["BlacklistManager.ClearAllConfirm"],
				MessageBoxButtons.Yes | MessageBoxButtons.No);
			if (result != MessageBoxButtons.Yes) return;
			_liveList.Clear();
			RebuildEntries();
			StatusMessage = string.Empty;
			await _persist();
		}
	}
}
