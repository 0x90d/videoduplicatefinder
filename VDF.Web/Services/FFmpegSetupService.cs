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
using VDF.Core.FFTools.FFmpegNative;
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

	/// <summary>
	/// Native binding on, but the FFmpeg libraries found are another version (typically the
	/// FFmpeg 8 an older VDF downloaded): why it cannot start and what to do. Shown on the
	/// scan page until it is fixed or the binding is turned off; null otherwise.
	/// </summary>
	public string? NativeMismatchMessage { get; private set; }
	/// <summary>The FFmpeg the native binding needs, for the download button ("FFmpeg 9").</summary>
	public string NativeBindingNeeds { get; private set; } = string.Empty;
	/// <summary>A matching download exists here (not in Docker, not on macOS).</summary>
	public bool CanDownloadForNativeBinding { get; private set; }

	public event Action? StateChanged;

	readonly Func<bool> nativeBindingOn;
	readonly Func<NativeLibraryMismatch?> findMismatch;
	readonly Func<bool> inDocker;
	NativeLibraryMismatch? loggedMismatch;

	public FFmpegSetupService(ScanService scan)
		: this(() => scan.Settings.UseNativeFfmpegBinding, NativeLibraryVersions.FindMismatch, IsRunningInDocker) { }

	internal FFmpegSetupService(Func<bool> nativeBindingOn, Func<NativeLibraryMismatch?> findMismatch, Func<bool> inDocker) {
		this.nativeBindingOn = nativeBindingOn;
		this.findMismatch = findMismatch;
		this.inDocker = inDocker;
	}

	void Notify() => StateChanged?.Invoke();

	/// <summary>
	/// Compares the native binding with the FFmpeg libraries it would load. Cheap (a few folder
	/// listings), so the scan page runs it whenever it opens: the setting may have changed.
	/// </summary>
	public void CheckNativeBinding() {
		var mismatch = nativeBindingOn() ? findMismatch() : null;
		string? message = null;
		bool canDownload = false;
		if (mismatch != null) {
			bool docker = inDocker();
			canDownload = !docker && FfmpegDownloader.GetDownloadPlans().Count > 0;
			message = FFmpegHelper.DescribeMismatch(mismatch) + " " + (
				docker ? $"Use an image with {mismatch.Needed}, or turn off the native FFmpeg binding in Settings."
				: canDownload ? $"Download {mismatch.Needed} here, or turn off the native FFmpeg binding in Settings."
				: $"Install the {mismatch.Needed} shared libraries (macOS: brew upgrade ffmpeg), or turn off the native FFmpeg binding in Settings.");
			if (mismatch != loggedMismatch) {
				loggedMismatch = mismatch;
				Logger.Instance.Warn(FFmpegHelper.DescribeMismatch(mismatch));
			}
		}
		if (message == NativeMismatchMessage && canDownload == CanDownloadForNativeBinding)
			return;
		NativeMismatchMessage = message;
		NativeBindingNeeds = mismatch?.Needed ?? string.Empty;
		CanDownloadForNativeBinding = canDownload;
		Notify();
	}

	/// <summary>The download the native-binding notice offers; checks again once it is in place.</summary>
	public async Task DownloadForNativeBindingAsync() {
		await DownloadFfmpegAsync();
		CheckNativeBinding();
	}

	public async Task CheckAndSetupAsync() {
		State = FFmpegSetupState.Checking;
		StatusMessage = "Checking FFmpeg availability...";
		Notify();

		await Task.Yield();

		if (ScanEngine.FFmpegExists && ScanEngine.FFprobeExists) {
			State = FFmpegSetupState.Ready;
			StatusMessage = "FFmpeg is available.";
			Notify();
			CheckNativeBinding();
			return;
		}

		if (IsRunningInDocker()) {
			State = FFmpegSetupState.DockerWarning;
			StatusMessage = "FFmpeg not found in Docker container. Add 'RUN apt-get update && apt-get install -y ffmpeg' to your Dockerfile, or mount FFmpeg binaries as a volume.";
			Notify();
			return;
		}

		await DownloadFfmpegAsync();
		CheckNativeBinding();
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
