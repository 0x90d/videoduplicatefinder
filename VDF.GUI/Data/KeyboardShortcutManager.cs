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

using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using ReactiveUI;

namespace VDF.GUI.Data {
	public class KeyboardShortcutManager : ReactiveObject {
		public static readonly KeyboardShortcutManager Instance = new();

		readonly Dictionary<string, string> _effectiveMap = new();

		KeyboardShortcutManager() => Reload();

		public void Reload() {
			_effectiveMap.Clear();
			foreach (var kvp in KeyboardShortcutDefaults.MainWindowDefaults)
				_effectiveMap[kvp.Key] = kvp.Value;
			foreach (var kvp in KeyboardShortcutDefaults.DatabaseViewerDefaults)
				_effectiveMap[kvp.Key] = kvp.Value;

			var overrides = SettingsFile.Instance.KeyboardShortcuts;
			foreach (var kvp in overrides) {
				if (_effectiveMap.ContainsKey(kvp.Key))
					_effectiveMap[kvp.Key] = kvp.Value;
			}

			this.RaisePropertyChanged("Item[]");
			ShortcutsChanged?.Invoke();
		}

		public event Action? ShortcutsChanged;

		public KeyGesture? this[string actionId] => GetEffectiveGesture(actionId);

		public KeyGesture? GetEffectiveGesture(string actionId) {
			if (!_effectiveMap.TryGetValue(actionId, out var gesture) || string.IsNullOrEmpty(gesture))
				return null;
			try {
				return KeyGesture.Parse(gesture);
			}
			catch {
				return null;
			}
		}

		public string GetEffectiveGestureString(string actionId) =>
			_effectiveMap.TryGetValue(actionId, out var gesture) ? gesture : string.Empty;

		public string GetDefaultGestureString(string actionId) {
			if (KeyboardShortcutDefaults.MainWindowDefaults.TryGetValue(actionId, out var gesture))
				return gesture;
			if (KeyboardShortcutDefaults.DatabaseViewerDefaults.TryGetValue(actionId, out gesture))
				return gesture;
			return string.Empty;
		}

		public void SetGesture(string actionId, string gestureString) {
			_effectiveMap[actionId] = gestureString;

			var defaultGesture = GetDefaultGestureString(actionId);
			var overrides = SettingsFile.Instance.KeyboardShortcuts;

			if (gestureString == defaultGesture)
				overrides.Remove(actionId);
			else
				overrides[actionId] = gestureString;

			SettingsFile.Instance.KeyboardShortcuts = new Dictionary<string, string>(overrides);

			this.RaisePropertyChanged("Item[]");
			ShortcutsChanged?.Invoke();
		}

		public string? GetConflict(string actionId, string gestureString) {
			if (string.IsNullOrEmpty(gestureString))
				return null;

			foreach (var kvp in _effectiveMap) {
				if (kvp.Key == actionId)
					continue;
				if (string.Equals(kvp.Value, gestureString, StringComparison.OrdinalIgnoreCase))
					return kvp.Key;
			}
			return null;
		}

		public void ResetToDefault(string actionId) {
			var defaultGesture = GetDefaultGestureString(actionId);
			SetGesture(actionId, defaultGesture);
		}

		public void ResetAll() {
			SettingsFile.Instance.KeyboardShortcuts = new Dictionary<string, string>();
			Reload();
		}

		public void ApplyBindings(Control target, Dictionary<string, ICommand> commandMap) {
			target.KeyBindings.Clear();
			foreach (var kvp in commandMap) {
				var gesture = GetEffectiveGesture(kvp.Key);
				if (gesture != null)
					target.KeyBindings.Add(new KeyBinding { Command = kvp.Value, Gesture = gesture });
			}
		}

		static readonly HashSet<Key> ReservedKeys = [Key.Tab, Key.Escape];

		public static bool IsReservedKey(Key key, KeyModifiers modifiers) =>
			modifiers == KeyModifiers.None && ReservedKeys.Contains(key);
	}
}
