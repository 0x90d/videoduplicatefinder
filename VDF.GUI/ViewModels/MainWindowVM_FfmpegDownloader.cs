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

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using FFmpeg.AutoGen;
using ReactiveUI;
using VDF.Core.FFTools;
using VDF.Core.Utils;
using System.Reactive;
using VDF.GUI.Views;

namespace VDF.GUI.ViewModels {
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
			string? extractedFolder = null;
			string? targetFolder = null;
			bool downloadSucceeded = false;

			try {
				var plans = GetSharedFfmpegDownloadPlans();
				if (plans.Count == 0) {
					errorMessage = App.Lang["Message.FfmpegDownloadUnsupported"];
					await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, null, errorMessage));
					return;
				}

				foreach (var plan in plans) {
					string tempRoot = Path.Combine(Path.GetTempPath(), "VDF.FFmpegDownload");
					string downloadPath = Path.Combine(tempRoot, plan.ArchiveFileName);
					extractedFolder = Path.Combine(tempRoot, "extracted");

					Directory.CreateDirectory(tempRoot);
					if (Directory.Exists(extractedFolder))
						Directory.Delete(extractedFolder, true);
					Directory.CreateDirectory(extractedFolder);

					try {
						await DownloadFileAsync(plan.DownloadUrl, downloadPath, plan);
						IsBusyOverlayText = App.Lang["Message.FfmpegDownloadExtracting"];
						ExtractArchive(downloadPath, extractedFolder, plan.ArchiveKind);

						targetFolder = Path.Combine(CoreUtils.CurrentFolder, "bin");
						Directory.CreateDirectory(targetFolder);
						CopyFfmpegFiles(extractedFolder, targetFolder);
						downloadSucceeded = true;
						break;
					}
					catch (HttpRequestException ex) {
						errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], ex.Message);
					}
				}

				bool ffmpegFound = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFmpeg) != null;
				bool ffprobeFound = FFToolsUtils.GetPath(FFToolsUtils.FFTool.FFProbe) != null;
				if (!downloadSucceeded && !string.IsNullOrWhiteSpace(errorMessage)) {
					await MessageBoxService.Show(BuildFfmpegInstallInstructions(ffmpegFound, ffprobeFound, targetFolder, errorMessage));
					return;
				}

				await MessageBoxService.Show(BuildFfmpegInstallInstructions(ffmpegFound, ffprobeFound, targetFolder, null));
			}
			catch (HttpRequestException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], ex.Message);
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, extractedFolder, errorMessage));
			}
			catch (IOException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadIoFailed"], ex.Message);
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, extractedFolder, errorMessage));
			}
			catch (UnauthorizedAccessException ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadAccessFailed"], ex.Message);
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, extractedFolder, errorMessage));
			}
			catch (Exception ex) {
				errorMessage = string.Format(CultureInfo.InvariantCulture, App.Lang["Message.FfmpegDownloadFailed"], ex.Message);
				await MessageBoxService.Show(BuildFfmpegInstallInstructions(false, false, extractedFolder, errorMessage));
			}
			finally {
				IsBusy = false;
				IsBusyOverlayText = string.Empty;
				IsFfmpegDownloadInProgress = false;
			}
		}

		record FfmpegDownloadPlan(Uri DownloadUrl, string ArchiveFileName, ArchiveType ArchiveKind, string DisplayName);

		enum ArchiveType {
			Zip,
			TarXz,
			TarGz
		}

		List<FfmpegDownloadPlan> GetSharedFfmpegDownloadPlans() {
			var plans = new List<FfmpegDownloadPlan>();
			int ffMajor = MapToFfmpegMajor(ffmpeg.LIBAVCODEC_VERSION_MAJOR, ffmpeg.LIBAVFORMAT_VERSION_MAJOR, ffmpeg.LIBAVUTIL_VERSION_MAJOR);
			string versionTag = ffMajor switch {
				8 => "8.0",
				7 => "7.1",
				6 => "6.1",
				5 => "5.1",
				_ => "7.1"
			};

			Architecture arch = RuntimeInformation.ProcessArchitecture;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				switch (arch) {
				case Architecture.X64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-win64-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-win64-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"Windows x64 ({versionTag})"));
					break;
				case Architecture.X86:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-win32-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-win32-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"Windows x86 ({versionTag})"));
					break;
				case Architecture.Arm64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-winarm64-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-winarm64-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"Windows ARM64 ({versionTag})"));
					break;
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				switch (arch) {
				case Architecture.X64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-linux64-gpl-shared-{versionTag}.tar.xz"),
						$"ffmpeg-n{versionTag}-latest-linux64-gpl-shared-{versionTag}.tar.xz",
						ArchiveType.TarXz,
						$"Linux x64 ({versionTag})"));
					break;
				case Architecture.X86:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-linux32-gpl-shared-{versionTag}.tar.xz"),
						$"ffmpeg-n{versionTag}-latest-linux32-gpl-shared-{versionTag}.tar.xz",
						ArchiveType.TarXz,
						$"Linux x86 ({versionTag})"));
					break;
				case Architecture.Arm64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-linuxarm64-gpl-shared-{versionTag}.tar.xz"),
						$"ffmpeg-n{versionTag}-latest-linuxarm64-gpl-shared-{versionTag}.tar.xz",
						ArchiveType.TarXz,
						$"Linux ARM64 ({versionTag})"));
					break;
				case Architecture.Arm:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-linuxarmhf-gpl-shared-{versionTag}.tar.xz"),
						$"ffmpeg-n{versionTag}-latest-linuxarmhf-gpl-shared-{versionTag}.tar.xz",
						ArchiveType.TarXz,
						$"Linux ARMHF ({versionTag})"));
					break;
				}
			}
			else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
				switch (arch) {
				case Architecture.X64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-macos64-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-nn{versionTag}-latest-macos64-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"macOS x64 ({versionTag})"));
					break;
				case Architecture.Arm64:
					plans.Add(new FfmpegDownloadPlan(
						new Uri($"https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-n{versionTag}-latest-macosarm64-gpl-shared-{versionTag}.zip"),
						$"ffmpeg-n{versionTag}-latest-macosarm64-gpl-shared-{versionTag}.zip",
						ArchiveType.Zip,
						$"macOS ARM64 ({versionTag})"));
					break;
				}
			}

			return plans;
		}

		async Task DownloadFileAsync(Uri downloadUrl, string destinationPath, FfmpegDownloadPlan plan) {
			using var client = new HttpClient();
			using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
			if (!response.IsSuccessStatusCode)
				throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");

			var totalBytes = response.Content.Headers.ContentLength;
			await using var sourceStream = await response.Content.ReadAsStreamAsync();
			await using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

			var buffer = new byte[81920];
			long totalRead = 0;
			int read;
			while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
				await destinationStream.WriteAsync(buffer.AsMemory(0, read));
				totalRead += read;
				UpdateDownloadProgress(plan.DisplayName, totalRead, totalBytes);
			}
			UpdateDownloadProgress(plan.DisplayName, totalRead, totalBytes);
		}

		void UpdateDownloadProgress(string displayName, long totalRead, long? totalBytes) =>
			Dispatcher.UIThread.Post(() => {
				double percent = totalBytes.HasValue && totalBytes.Value > 0
					? totalRead / (double)totalBytes.Value * 100
					: 0;
				IsBusyOverlayText = string.Format(
					CultureInfo.InvariantCulture,
					App.Lang["Message.FfmpegDownloadProgress"],
					displayName,
					Math.Round(percent, 1),
					FormatBytes(totalRead),
					FormatBytes(totalBytes));
			});

		static string FormatBytes(long? bytes) {
			if (bytes == null) return "?";
			double size = bytes.Value;
			string[] units = { "B", "KB", "MB", "GB" };
			int unit = 0;
			while (size >= 1024 && unit < units.Length - 1) {
				size /= 1024;
				unit++;
			}
			return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, units[unit]);
		}

		static void ExtractArchive(string archivePath, string targetFolder, ArchiveType type) {
			if (type == ArchiveType.Zip) {
				ZipFile.ExtractToDirectory(archivePath, targetFolder, true);
				return;
			}

			string arguments = type switch {
				ArchiveType.TarXz => $"-xJf \"{archivePath}\" -C \"{targetFolder}\"",
				ArchiveType.TarGz => $"-xzf \"{archivePath}\" -C \"{targetFolder}\"",
				_ => throw new InvalidOperationException("Unsupported archive type")
			};

			using var process = new Process {
				StartInfo = new ProcessStartInfo {
					FileName = "tar",
					Arguments = arguments,
					UseShellExecute = false,
					RedirectStandardError = true,
					RedirectStandardOutput = true
				}
			};
			process.Start();
			process.WaitForExit();
			if (process.ExitCode != 0) {
				string error = process.StandardError.ReadToEnd();
				throw new IOException(string.IsNullOrWhiteSpace(error) ? "Failed to extract archive." : error);
			}
		}

		static void CopyFfmpegFiles(string sourceRoot, string targetFolder) {
			string ffmpegName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
			string ffprobeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";

			string? ffmpegPath = Directory.EnumerateFiles(sourceRoot, ffmpegName, SearchOption.AllDirectories).FirstOrDefault();
			string? ffprobePath = Directory.EnumerateFiles(sourceRoot, ffprobeName, SearchOption.AllDirectories).FirstOrDefault();

			if (ffmpegPath == null || ffprobePath == null)
				throw new FileNotFoundException("ffmpeg/ffprobe not found in the extracted archive.");

			string? binFolder = Path.GetDirectoryName(ffmpegPath);
			if (string.IsNullOrEmpty(binFolder))
				throw new DirectoryNotFoundException("Failed to locate ffmpeg folder in the archive.");

			foreach (var file in Directory.EnumerateFiles(binFolder)) {
				string fileName = Path.GetFileName(file);
				CopyFile(file, Path.Combine(targetFolder, fileName));
			}

			string[] libraryPrefixes = { "avcodec", "avformat", "avutil", "swresample", "swscale" };
			string[] libraryExtensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? new[] { ".dll" }
				: RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
					? new[] { ".dylib" }
					: new[] { ".so" };

			foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)) {
				string fileName = Path.GetFileName(file);
				if (!libraryExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
					continue;
				if (!libraryPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
					continue;
				CopyFile(file, Path.Combine(targetFolder, fileName));
			}
		}

		static void CopyFile(string sourcePath, string destinationPath) {
			string targetPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
				? FFToolsUtils.LongPathFix(destinationPath)
				: destinationPath;
			File.Copy(sourcePath, targetPath, true);
		}

		static string BuildFfmpegInstallInstructions(bool ffmpegFound, bool ffprobeFound, string? targetFolder, string? errorMessage) {
			var sb = new System.Text.StringBuilder();
			if (!string.IsNullOrWhiteSpace(errorMessage)) {
				sb.AppendLine(errorMessage);
				sb.AppendLine();
			}

			if (ffmpegFound && ffprobeFound) {
				sb.AppendLine(App.Lang["Message.FfmpegDownloadVerified"]);
			}
			else {
				sb.AppendLine(App.Lang["Message.FfmpegDownloadMissing"]);
			}

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
