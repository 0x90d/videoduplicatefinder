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
using VDF.Core;
using VDF.Core.FFTools;
using VDF.Core.Utils;

namespace VDF.Web.Services;

public enum FFmpegSetupState {
	Idle,
	Checking,
	Ready,
	Downloading,
	Failed,
	DockerWarning
}

// Thin state machine over Core's FfmpegDownloader: maps its progress onto the setup
// page's status text/percent. The download/verify/extract/install logic lives in
// VDF.Core.FFTools.FfmpegDownloader, shared with the GUI.
public sealed class FFmpegSetupService {
	public FFmpegSetupState State { get; private set; }
	public string StatusMessage { get; private set; } = string.Empty;
	public double DownloadProgress { get; private set; }
	public bool IsReady => State == FFmpegSetupState.Ready;

	public event Action? StateChanged;

	void Notify() => StateChanged?.Invoke();

	public async Task CheckAndSetupAsync() {
		State = FFmpegSetupState.Checking;
		StatusMessage = "Checking FFmpeg availability...";
		Notify();

		await Task.Yield();

		if (ScanEngine.FFmpegExists && ScanEngine.FFprobeExists) {
			State = FFmpegSetupState.Ready;
			StatusMessage = "FFmpeg is available.";
			Notify();
			return;
		}

		if (IsRunningInDocker()) {
			State = FFmpegSetupState.DockerWarning;
			StatusMessage = "FFmpeg not found in Docker container. Add 'RUN apt-get update && apt-get install -y ffmpeg' to your Dockerfile, or mount FFmpeg binaries as a volume.";
			Notify();
			return;
		}

		await DownloadFfmpegAsync();
	}

	static bool IsRunningInDocker() {
		if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
			return true;
		try {
			if (File.Exists("/.dockerenv"))
				return true;
		}
		catch { }
		return false;
	}

	async Task DownloadFfmpegAsync() {
		State = FFmpegSetupState.Downloading;
		StatusMessage = "Preparing FFmpeg download...";
		DownloadProgress = 0;
		Notify();

		try {
			var progress = new Progress<FfmpegDownloadProgress>(p => {
				switch (p.Phase) {
				case FfmpegDownloadPhase.Verifying:
					StatusMessage = "Verifying checksum...";
					DownloadProgress = 87;
					break;
				case FfmpegDownloadPhase.Extracting:
					StatusMessage = "Extracting FFmpeg...";
					DownloadProgress = 90;
					break;
				default:
					DownloadProgress = p.BytesTotal > 0 ? p.BytesDone / (double)p.BytesTotal!.Value * 85 : 0;
					StatusMessage = string.Format(CultureInfo.InvariantCulture,
						"Downloading FFmpeg ({0})... {1} / {2}",
						p.DisplayName, DownloadUtils.FormatBytes(p.BytesDone), DownloadUtils.FormatBytes(p.BytesTotal));
					break;
				}
				Notify();
			});
			await FfmpegDownloader.DownloadAndInstallAsync(progress, CancellationToken.None);

			State = FFmpegSetupState.Ready;
			StatusMessage = "FFmpeg downloaded and installed successfully.";
			DownloadProgress = 100;
			Notify();
		}
		catch (PlatformNotSupportedException) {
			State = FFmpegSetupState.Failed;
			StatusMessage = "No FFmpeg download available for this platform/architecture.";
			Notify();
		}
		catch (HttpRequestException ex) {
			State = FFmpegSetupState.Failed;
			StatusMessage = $"Download failed: {ex.Message}";
			Notify();
		}
		catch (Exception ex) {
			State = FFmpegSetupState.Failed;
			StatusMessage = $"FFmpeg setup failed: {ex.Message}";
			Notify();
		}
	}
}
