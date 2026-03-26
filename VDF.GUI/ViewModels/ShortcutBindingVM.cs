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

using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using VDF.GUI.Data;

namespace VDF.GUI.ViewModels {
	public class ShortcutBindingVM : ReactiveObject {
		readonly ObservableCollection<ShortcutBindingVM> _siblings;

		public string ActionId { get; }
		public string DisplayName { get; }
		public string DefaultGesture { get; }

		string _currentGesture;
		public string CurrentGesture {
			get => _currentGesture;
			set {
				if (_currentGesture == value) return;
				this.RaiseAndSetIfChanged(ref _currentGesture, value);
				this.RaisePropertyChanged(nameof(IsModified));
			}
		}

		string? _conflictText;
		public string? ConflictText {
			get => _conflictText;
			set => this.RaiseAndSetIfChanged(ref _conflictText, value);
		}

		public bool IsModified => CurrentGesture != DefaultGesture;

		public ReactiveCommand<Unit, Unit> ResetCommand { get; }
		public ReactiveCommand<Unit, Unit> ClearCommand { get; }

		public ShortcutBindingVM(string actionId, ObservableCollection<ShortcutBindingVM> siblings) {
			ActionId = actionId;
			_siblings = siblings;
			DisplayName = App.Lang[$"Shortcut.{actionId}"];
			DefaultGesture = KeyboardShortcutManager.Instance.GetDefaultGestureString(actionId);
			_currentGesture = KeyboardShortcutManager.Instance.GetEffectiveGestureString(actionId);

			ResetCommand = ReactiveCommand.Create(() => {
				ApplyGesture(DefaultGesture);
			});

			ClearCommand = ReactiveCommand.Create(() => {
				ApplyGesture(string.Empty);
			});
		}

		public void ApplyGesture(string gestureString) {
			var conflict = KeyboardShortcutManager.Instance.GetConflict(ActionId, gestureString);
			if (conflict != null) {
				KeyboardShortcutManager.Instance.SetGesture(conflict, string.Empty);
				var conflictingVm = _siblings.FirstOrDefault(s => s.ActionId == conflict);
				if (conflictingVm != null) {
					conflictingVm.CurrentGesture = string.Empty;
					conflictingVm.ConflictText = null;
				}
			}

			KeyboardShortcutManager.Instance.SetGesture(ActionId, gestureString);
			CurrentGesture = gestureString;
			ConflictText = null;
		}

		public void CheckConflict(string gestureString) {
			var conflict = KeyboardShortcutManager.Instance.GetConflict(ActionId, gestureString);
			if (conflict != null) {
				var displayName = App.Lang[$"Shortcut.{conflict}"];
				ConflictText = string.Format(App.Lang["MainWindow.Settings.KeyboardShortcuts.Conflict"], displayName);
			}
			else {
				ConflictText = null;
			}
		}
	}
}
