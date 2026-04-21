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

		static readonly Lazy<string> _StateFolder = new(ResolveStateFolder);
		static readonly Lazy<string> _SettingsFolder = new(ResolveSettingsFolder);
		static readonly Lazy<bool> _CurrentFolderWritable = new(() => CanWriteToDirectory(CurrentFolder!));
		// Portable mode (keep data next to the executable) is a convenience for desktop/unzip installs.
		// In Docker the working directory is writable by coincidence but is ephemeral, so skip it there.
		static readonly Lazy<bool> _RunningInContainer = new(() =>
			string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase));

		public static string StateFolder => _StateFolder.Value;
		public static string SettingsFolder => _SettingsFolder.Value;
		public static bool IsCurrentFolderWritable => _CurrentFolderWritable.Value;
		public static bool IsRunningInContainer => _RunningInContainer.Value;

		public static bool CanWriteToDirectory(string path) {
			try {
				if (!Directory.Exists(path))
					return false;
				var testFile = Path.Combine(path, $".vdf_write_test_{Guid.NewGuid():N}");
				using var stream = new FileStream(testFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
				stream.WriteByte(0);
				return true;
			}
			catch {
				return false;
			}
		}

		public static string ResolveDatabaseFolder(string? customFolder) {
			if (Directory.Exists(customFolder))
				return customFolder!;
			return StateFolder;
		}

		static string ResolveStateFolder() {
			if (!IsRunningInContainer && IsCurrentFolderWritable)
				return CurrentFolder;
			return GetDefaultStateFolder();
		}

		static string ResolveSettingsFolder() {
			if (!IsRunningInContainer && IsCurrentFolderWritable)
				return CurrentFolder;
			return GetDefaultSettingsFolder();
		}

		public static string GetDefaultStateFolder() {
			string baseFolder;
			if (IsWindows) {
				baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				baseFolder = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					"Library", "Application Support");
			}
			else {
				baseFolder = Environment.GetEnvironmentVariable("XDG_STATE_HOME")
					?? Path.Combine(
						Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
						".local", "state");
			}
			var folder = Path.Combine(baseFolder, "VDF");
			Directory.CreateDirectory(folder);
			return folder;
		}

		public static string GetDefaultSettingsFolder() {
			string baseFolder;
			if (IsWindows) {
				baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				baseFolder = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
					"Library", "Preferences");
			}
			else {
				baseFolder = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
					?? Path.Combine(
						Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
						".config");
			}
			var folder = Path.Combine(baseFolder, "VDF");
			Directory.CreateDirectory(folder);
			return folder;
		}
	}
}
