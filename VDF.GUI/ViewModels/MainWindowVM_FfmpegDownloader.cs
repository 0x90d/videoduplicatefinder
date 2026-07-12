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

using System.Globalization;
using System.Net.Http;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading;
using ReactiveUI;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
	// Thin UI shell over Core's FfmpegDownloader: busy-overlay progress text and the
	// localized install/troubleshooting messages. The download/verify/extract/install
	// logic lives in VDF.Core.FFTools.FfmpegDownloader, shared with the Web UI.
	public partial class MainWindowVM : ReactiveObject {
		bool _isFfmpegDownloadInProgress;
		public bool IsFfmpegDownloadInProgress {
			get => _isFfmpegDownloadInProgress;
			set => this.RaiseAndSetIfChanged(ref _isFfmpegDownloadInProgress, value);
		}

		public ReactiveCommand<Unit, Unit> DownloadSharedFfmpegCommand => ReactiveCommand.CreateFromTask(async () => {
			await DownloadSharedFfmpegAsync();
		});

		async Task DownloadSharedFfmpegAsync() {
			if (IsFfmpegDownloadInProgress) return;
			IsFfmpegDownloadInProgress = true;
			IsBusy = true;
			IsBusyOverlayText = App.Lang["Message.FfmpegDownloadPreparing"];
			string? errorMessage = null;
			string? targetFolder = null;
			CancellationToken cancelToken = BeginCancelableBusyOperation();

			try {
				var progress = new Progress<FfmpegDownloadProgress>(p => IsBusyOverlayText = p.Phase switch {
					FfmpegDownloadPhase.Verifying => App.Lang["Message.FfmpegDownloadVerifying"],
					FfmpegDownloadPhase.Extracting => App.Lang["Message.FfmpegDownloadExtracting"],
					_ => string.Format(
						CultureInfo.InvariantCulture,
						App.Lang["Message.FfmpegDownloadProgress"],
						p.DisplayName,
						Math.Round(p.BytesTotal > 0 ? p.BytesDone / (double)p.BytesTotal!.Value * 100 : 0, 1),
						DownloadUtils.FormatBytes(p.BytesDone),
						DownloadUtils.FormatBytes(p.BytesTotal)),
				});
				targetFolder = await FfmpegDownloader.DownloadAndInstallAsync(progress, cancelToken);
			}
			catch (OperationCanceledException) {
				// User pressed the overlay's Cancel button — the finally below restores
				// the idle state; returning skips the install-instructions message box.
				return;
			}
			catch (PlatformNotSupportedException) {
				errorMessage = App.Lang["Message.FfmpegDownloadUnsupported"];
			}
			catch (HttpRequestException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], ex.Message);
			}
			catch (IOException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadIoFailed"], ex.Message);
			}
			catch (UnauthorizedAccessException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadAccessFailed"], ex.Message);
			}
			catch (Exception ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], ex.Message);
			}
			finally {
				EndCancelableBusyOperation();
				IsBusy = false;
				IsBusyOverlayText = string.Empty;
				IsFfmpegDownloadInProgress = false;
			}

			bool ffmpegFound = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) != null;
			bool ffprobeFound = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFProbe) != null;
			await MessageBoxService.Show(BuildFfmpegInstallInstructions(ffmpegFound, ffprobeFound, targetFolder, errorMessage));
		}

		static string BuildFfmpegInstallInstructions(bool ffmpegFound, bool ffprobeFound, string? targetFolder, string? errorMessage) {
			var sb = new System.Text.StringBuilder();
			if (!string.IsNullOrWhiteSpace(errorMessage)) {
				sb.AppendLine(errorMessage);
				sb.AppendLine();
			}

			if (ffmpegFound && ffprobeFound) {
				// Everything is in place — the manual install/restart instructions below
				// would only contradict the success (the scan continues right away).
				sb.AppendLine(App.Lang["Message.FfmpegDownloadVerified"]);
				if (!string.IsNullOrWhiteSpace(targetFolder)) {
					sb.AppendLine();
					sb.AppendLine(string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadTargetFolder"], targetFolder));
				}
				return sb.ToString();
			}

			sb.AppendLine(App.Lang["Message.FfmpegDownloadMissing"]);

			if (!string.IsNullOrWhiteSpace(targetFolder)) {
				sb.AppendLine();
				sb.AppendLine(string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadTargetFolder"], targetFolder));
			}

			sb.AppendLine();

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				sb.AppendLine(App.Lang["Message.FfmpegDownloadWindowsInfo"]);
				sb.AppendLine();
				sb.AppendLine(App.Lang["Message.FfmpegDownloadWindowsRestart"]);
				return sb.ToString();
			}

			sb.AppendLine(App.Lang["Message.FfmpegDownloadStopApp"]);
			sb.AppendLine();

			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				sb.AppendLine(App.Lang["Message.FfmpegDownloadMacHeader"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadMacBrew"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadMacPorts"]);
				return sb.ToString();
			}

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxHeader"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxDeb"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxFedora"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxArch"]);
				sb.AppendLine(App.Lang["Message.FfmpegDownloadLinuxSuse"]);
				return sb.ToString();
			}

			sb.AppendLine(App.Lang["Message.FfmpegDownloadUnsupported"]);
			return sb.ToString();
		}
	}
}
