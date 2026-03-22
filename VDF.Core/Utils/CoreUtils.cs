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

using System.Runtime.InteropServices;

namespace VDF.Core.Utils {
	public static class CoreUtils {
		public static bool IsWindows;
		public static string CurrentFolder;
		static CoreUtils() {
			IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
			CurrentFolder = Path.GetDirectoryName(Environment.ProcessPath)!;
		}
		public static bool CanWriteToDirectory(string path) {
			try {
				Directory.CreateDirectory(path);
				var testPath = Path.Combine(path, $".vdf_write_test_{Guid.NewGuid():N}");
				using var stream = new FileStream(testPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
				stream.WriteByte(0);
				return true;
			}
			catch {
				return false;
			}
		}
		public static string GetDefaultStateFolder() {
			string? baseFolder;
			if (CoreUtils.IsWindows) {
				// LocalApplicationData = %LOCALAPPDATA% (local, non-roaming — appropriate for state/logs)
				baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");
			}
			else {
				baseFolder = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
				if (string.IsNullOrWhiteSpace(baseFolder))
					baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
			}

			var stateFolder = Path.Combine(baseFolder, "VDF");
			Directory.CreateDirectory(stateFolder);
			return stateFolder;
		}
		public static string GetDefaultSettingsFolder() {
			string? baseFolder;
			if (CoreUtils.IsWindows) {
				baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Preferences");
			}
			else {
				baseFolder = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
				if (string.IsNullOrWhiteSpace(baseFolder))
					baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
			}

			var settingsFolder = Path.Combine(baseFolder, "VDF");
			Directory.CreateDirectory(settingsFolder);
			return settingsFolder;
		}

	}
}
