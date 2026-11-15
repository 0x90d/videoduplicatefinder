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
// The GUI's notice for "native binding on, but the FFmpeg found is another version" (startup and
// scan start). Its text is formatted from the locale files, so a translation with a broken
// placeholder would throw at the user instead of telling them anything.

using Avalonia.Controls;
using Avalonia.LogicalTree;
using VDF.Core.Utils;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

public class NativeFfmpegNoticeTests {

	const string Folder = @"C:\Tools\VDF\bin";

	/// <summary>FFmpeg 8's libraries as an older VDF downloaded them, spelled for this OS.</summary>
	static IEnumerable<string> Ffmpeg8Libraries() =>
		new[] { ("avcodec", 62), ("avdevice", 62), ("avfilter", 11), ("avformat", 62), ("avutil", 60), ("swresample", 6), ("swscale", 9) }
			.Select(l => OperatingSystem.IsWindows() ? $"{l.Item1}-{l.Item2}.dll"
				: OperatingSystem.IsMacOS() ? $"lib{l.Item1}.{l.Item2}.dylib"
				: $"lib{l.Item1}.so.{l.Item2}");

	[Fact]
	public Task Startup_NativeBindingNextToAnOlderFfmpeg_SaysSo() => HeadlessUi.Run(async () => {
		// The update path: native binding on, VDF's folder still holding the FFmpeg 8 an older
		// VDF downloaded. The startup check has to tell the user, not leave them to find out.
		var (window, vm) = HeadlessUi.Shell();
		string bin = Path.Combine(CoreUtils.CurrentFolder, "bin");
		Directory.CreateDirectory(bin);
		var planted = Ffmpeg8Libraries().Select(f => Path.Combine(bin, f)).Where(f => !File.Exists(f)).ToList();
		bool nativeBefore = SettingsFile.Instance.UseNativeFfmpegBinding;
		MessageBoxView? dialog = null;
		try {
			foreach (string file in planted)
				File.WriteAllBytes(file, new byte[] { 1 });
			SettingsFile.Instance.UseNativeFfmpegBinding = true;

			vm.NotifyNativeFfmpegMismatch();
			for (int i = 0; i < 200 && dialog == null; i++) {
				await Task.Delay(25);
				HeadlessUi.Pump();
				dialog = window.OwnedWindows.OfType<MessageBoxView>().FirstOrDefault();
			}

			Assert.NotNull(dialog);
			string text = string.Join("\n", dialog!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
			Assert.Contains(bin, text);
			Assert.Contains("FFmpeg 8", text);
			Assert.Contains("FFmpeg 9", text);
		}
		finally {
			dialog?.Close(null);
			HeadlessUi.Pump();
			SettingsFile.Instance.UseNativeFfmpegBinding = nativeBefore;
			foreach (string file in planted)
				File.Delete(file);
		}
	});

	[Fact]
	public Task WhereADownloadExists_ItIsOffered() => HeadlessUi.Run(() => {
		string text = MainWindowVM.NativeMismatchText(Folder, "FFmpeg 8", "FFmpeg 9", canDownload: true);

		Assert.Contains(Folder, text);
		Assert.Contains("are FFmpeg 8", text);
		Assert.Contains("Download FFmpeg 9 now?", text);
		// The way out without downloading names the switch exactly as Settings shows it.
		Assert.Contains($"\"{App.Lang["MainWindow.Settings.UseNativeFfmpegBinding"]}\"", text);
	});

	[Fact]
	public Task WhereNoneExists_ItSaysHowToInstall() => HeadlessUi.Run(() => {
		string text = MainWindowVM.NativeMismatchText(Folder, "FFmpeg 8", "FFmpeg 9", canDownload: false);

		Assert.Contains("brew upgrade ffmpeg", text);
		Assert.DoesNotContain("Download", text);
	});

	[Fact]
	public Task EveryLanguage_FormatsBothNotices() => HeadlessUi.Run(() => {
		try {
			// Every shipped locale, not a fixed list: a fixed list silently skipped each
			// language added after it was written (ru in #951).
			var languages = App.Lang.AvailableLanguages;
			Assert.Contains("zh-Hans", languages); // discovery worked, not the "en" fallback
			foreach (string lang in languages) {
				App.Lang.LoadLanguage(lang);
				foreach (bool canDownload in new[] { true, false }) {
					string text = MainWindowVM.NativeMismatchText(Folder, "FFmpeg 8", "FFmpeg 9", canDownload);
					Assert.True(text.Contains(Folder) && text.Contains("FFmpeg 8") && text.Contains("FFmpeg 9") &&
						text.Contains(App.Lang["MainWindow.Settings.UseNativeFfmpegBinding"]),
						$"{lang} ({(canDownload ? "download" : "manual")}) is missing a placeholder: {text}");
				}
			}
		}
		finally { App.Lang.LoadLanguage("en"); }
	});
}
