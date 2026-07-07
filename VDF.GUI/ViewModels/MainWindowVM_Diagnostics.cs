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

using System.Reactive;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using ReactiveUI;
using VDF.Core.FFTools;
using VDF.Core.FFTools.FFmpegNative;
using VDF.Core.Utils;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	// Settings -> Test: environment/diagnostics helpers for bug reports.
	public partial class MainWindowVM : ReactiveObject {

		// Builds a copy-pasteable environment report: VDF/OS/ffmpeg versions, native-binding
		// health, and the settings that actually affect matching. This is the single most
		// useful artifact to attach to an issue.
		string BuildDiagnosticsReport() {
			SyncCoreSettings(); // make sure the report reflects the current (unsaved) settings
			var sb = new StringBuilder();
			sb.AppendLine($"VDF version:        {Utils.VersionInfo.LongDisplay}");
			sb.AppendLine($"OS:                 {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
			sb.AppendLine($".NET:               {RuntimeInformation.FrameworkDescription}");
			sb.AppendLine($"ffmpeg path:        {FfmpegEngine.FFmpegPath}");
			sb.AppendLine($"ffmpeg:             {FFToolsUtils.GetToolVersionLine(FFToolsUtils.FFTool.FFmpeg)}");
			sb.AppendLine($"ffprobe:            {FFToolsUtils.GetToolVersionLine(FFToolsUtils.FFTool.FFProbe)}");
			sb.AppendLine($"Native binding:     setting={Scanner.Settings.UseNativeFfmpegBinding}, libsPresent={FFmpegHelper.DoFFmpegLibraryFilesExist}, libsLoadable={FFmpegHelper.CanLoadNativeLibraries}");
			sb.AppendLine($"Expected libs:      {string.Join(", ", FFmpegHelper.GenerateLibraryFileNames())}");
			sb.AppendLine($"Hardware accel:     {Scanner.Settings.HardwareAccelerationMode}");
			sb.AppendLine($"Percent:            {Scanner.Settings.Percent}");
			sb.AppendLine($"Use pHash:          {Scanner.Settings.UsePHashing}");
			sb.AppendLine($"Compare flipped:    {Scanner.Settings.CompareHorizontallyFlipped}");
			sb.AppendLine($"Ignore black/white: {Scanner.Settings.IgnoreBlackPixels}/{Scanner.Settings.IgnoreWhitePixels}");
			sb.AppendLine($"Thumbnails:         {Scanner.Settings.ThumbnailCount}");
			sb.AppendLine($"Include images:     {Scanner.Settings.IncludeImages}");
			sb.AppendLine($"Parallelism:        {Scanner.Settings.MaxDegreeOfParallelism} (HDD cap: {Scanner.Settings.HddMaxDegreeOfParallelism}, drive overrides: {Scanner.Settings.DriveTypeOverrides.Count})");
			sb.AppendLine($"Custom FF args:     {(string.IsNullOrWhiteSpace(Scanner.Settings.CustomFFArguments) ? "(none)" : Scanner.Settings.CustomFFArguments)}");
			sb.AppendLine($"Database entries:   {DatabaseUtils.Database.Count}");
			return sb.ToString();
		}

		public ReactiveCommand<Unit, Unit> CopyDiagnosticsCommand => ReactiveCommand.Create(() => {
			string report = BuildDiagnosticsReport();
			FilePairTestResult = report; // show it too, so the user can see what's being shared
			ApplicationHelpers.MainWindow.Clipboard?.SetTextAsync(report);
		});

		public ReactiveCommand<Unit, Unit> ExportGrayBytesCommand => ReactiveCommand.CreateFromTask(async () => {
			var result = await Utils.PickerDialogUtils.SaveFilePicker(new FilePickerSaveOptions {
				Title = App.Lang["MainWindow.Settings.Diagnostics.ExportGrayBytes"],
				SuggestedFileName = "vdf-graybytes-diagnostic.json",
				DefaultExtension = ".json"
			});
			if (string.IsNullOrEmpty(result))
				return;
			bool ok = DatabaseUtils.ExportGrayBytesDiagnostic(result);
			await MessageBoxService.Show(ok
				? string.Format(App.Lang["MainWindow.Settings.Diagnostics.ExportGrayBytes.Done"], result)
				: App.Lang["MainWindow.Settings.Diagnostics.ExportGrayBytes.Failed"]);
		});
	}
}
