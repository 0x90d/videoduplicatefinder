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

using System.Text.Json;
using VDF.GUI.Data;

namespace VDF.GUI.Tests;

// A settings file that cannot be read (torn write during save, disk corruption) used to
// throw out of SettingsFile.LoadSettings inside the MainWindow constructor — before any
// exception handler or window existed — so the app silently never launched again until
// the user found and deleted Settings.json by hand (issue #830, reported as "cannot
// launch after switching language": the language switch is simply what rewrote the file).
// Startup must survive any settings-file content.
public class SettingsLoadRecoveryTests : IDisposable {
	readonly string dir = Directory.CreateTempSubdirectory("vdf-settings-tests-").FullName;

	public void Dispose() {
		SettingsFile.SetSettingsPath(null);
		try { Directory.Delete(dir, recursive: true); }
		catch { }
	}

	string WriteSettingsFile(string name, string content) {
		string path = Path.Combine(dir, name);
		File.WriteAllText(path, content);
		return path;
	}

	[Fact]
	public void StartupLoad_TruncatedSettingsFile_DoesNotThrowAndKeepsCorruptCopy() {
		// Round-trip real settings, then truncate to simulate a torn write.
		string path = Path.Combine(dir, "Settings.json");
		SettingsFile.SaveSettings(path);
		byte[] bytes = File.ReadAllBytes(path);
		File.WriteAllBytes(path, bytes.AsSpan(0, bytes.Length / 2).ToArray());
		SettingsFile.SetSettingsPath(path);

		Exception? ex = Record.Exception(SettingsFile.LoadSettingsAtStartup);

		Assert.Null(ex);
		Assert.NotNull(SettingsFile.StartupLoadError);
		Assert.True(File.Exists(path + ".corrupt"));
		Assert.NotNull(SettingsFile.Instance); // app continues on defaults
	}

	[Fact]
	public void StartupLoad_GarbageContent_DoesNotThrow() {
		string path = WriteSettingsFile("Settings.json", "\0\0\0 not json at all");
		SettingsFile.SetSettingsPath(path);

		Assert.Null(Record.Exception(SettingsFile.LoadSettingsAtStartup));
		Assert.NotNull(SettingsFile.StartupLoadError);
	}

	[Fact]
	public void StartupLoad_NullDocument_FallsBackToDefaultsInsteadOfNullInstance() {
		string path = WriteSettingsFile("Settings.json", "null");
		SettingsFile.SetSettingsPath(path);

		Assert.Null(Record.Exception(SettingsFile.LoadSettingsAtStartup));
		Assert.NotNull(SettingsFile.StartupLoadError);
		Assert.NotNull(SettingsFile.Instance);
	}

	[Fact]
	public void StartupLoad_CorruptLegacyXml_DoesNotThrow() {
		string path = WriteSettingsFile("Settings.xml", "<Settings><Includes>");
		SettingsFile.SetSettingsPath(path);

		Assert.Null(Record.Exception(SettingsFile.LoadSettingsAtStartup));
		Assert.NotNull(SettingsFile.StartupLoadError);
	}

	[Fact]
	public void StartupLoad_ValidZhHansSettings_LoadsWithoutError() {
		// The #830 report itself: LanguageCode zh-Hans must never prevent a launch.
		string path = WriteSettingsFile("Settings.json", "{\"LanguageCode\":\"zh-Hans\"}");
		SettingsFile.SetSettingsPath(path);

		SettingsFile.LoadSettingsAtStartup();

		Assert.Null(SettingsFile.StartupLoadError);
		Assert.Equal("zh-Hans", SettingsFile.Instance.LanguageCode);
	}

	[Fact]
	public void ExplicitLoad_InvalidFile_StillThrowsSoTheImportDialogCanReportIt() {
		// The settings-import command relies on the exception to tell the user the file
		// failed to load; startup resilience must not swallow that path.
		string path = WriteSettingsFile("import.json", "{ not json");
		Assert.ThrowsAny<JsonException>(() => SettingsFile.LoadSettings(path));
	}

	[Fact]
	public void ExplicitLoad_NullDocument_ThrowsInsteadOfClearingAllSettings() {
		string path = WriteSettingsFile("import.json", "null");
		Assert.ThrowsAny<JsonException>(() => SettingsFile.LoadSettings(path));
	}
}
