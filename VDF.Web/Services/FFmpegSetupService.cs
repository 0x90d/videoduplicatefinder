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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FFmpeg.AutoGen;
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

		string? extractedFolder = null;

		try {
			var plans = GetSharedFfmpegDownloadPlans();
			if (plans.Count == 0) {
				State = FFmpegSetupState.Failed;
				StatusMessage = "No FFmpeg download available for this platform/architecture.";
				Notify();
				return;
			}

			string? lastError = null;
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

					StatusMessage = "Verifying checksum...";
					DownloadProgress = 87;
					Notify();
					await VerifyChecksumAsync(plan.DownloadUrl, downloadPath, plan.ArchiveFileName);

					StatusMessage = "Extracting FFmpeg...";
					DownloadProgress = 90;
					Notify();

					ExtractArchive(downloadPath, extractedFolder, plan.ArchiveKind);

					string targetFolder = Path.Combine(CoreUtils.CurrentFolder, "bin");
					Directory.CreateDirectory(targetFolder);
					var targetLibFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
						? Path.Combine(CoreUtils.CurrentFolder, "lib")
						: targetFolder;
					Directory.CreateDirectory(targetLibFolder);
					CopyFfmpegFiles(extractedFolder, targetFolder, targetLibFolder);

					State = FFmpegSetupState.Ready;
					StatusMessage = "FFmpeg downloaded and installed successfully.";
					DownloadProgress = 100;
					Notify();
					return;
				}
				catch (HttpRequestException ex) {
					lastError = $"Download failed: {ex.Message}";
				}
			}

			State = FFmpegSetupState.Failed;
			StatusMessage = lastError ?? "FFmpeg download failed.";
			Notify();
		}
		catch (Exception ex) {
			State = FFmpegSetupState.Failed;
			StatusMessage = $"FFmpeg setup failed: {ex.Message}";
			Notify();
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
			// BtbN/yt-dlp builds publish only the latest minor per major; the n8.0 tag
			// was retired when 8.1 landed, so a hardcoded "8.0" 404s on every fresh
			// install. Keep this aligned with whatever minor BtbN currently ships.
			8 => "8.1",
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
					$"ffmpeg-n{versionTag}-latest-macos64-gpl-shared-{versionTag}.zip",
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

	const long MaxDownloadBytes = 500 * 1024 * 1024; // 500 MB safety cap

	async Task DownloadFileAsync(Uri downloadUrl, string destinationPath, FfmpegDownloadPlan plan) {
		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
		if (!response.IsSuccessStatusCode)
			throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}");

		var totalBytes = response.Content.Headers.ContentLength;
		if (totalBytes > MaxDownloadBytes)
			throw new HttpRequestException($"Download too large ({totalBytes} bytes, max {MaxDownloadBytes})");

		await using var sourceStream = await response.Content.ReadAsStreamAsync();
		await using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

		var buffer = new byte[81920];
		long totalRead = 0;
		int read;
		while ((read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0) {
			await destinationStream.WriteAsync(buffer.AsMemory(0, read));
			totalRead += read;
			if (totalRead > MaxDownloadBytes)
				throw new HttpRequestException($"Download exceeded size limit ({MaxDownloadBytes} bytes)");

			double percent = totalBytes.HasValue && totalBytes.Value > 0
				? totalRead / (double)totalBytes.Value * 85
				: 0;
			DownloadProgress = percent;
			StatusMessage = string.Format(CultureInfo.InvariantCulture,
				"Downloading FFmpeg ({0})... {1} / {2}",
				plan.DisplayName, FormatBytes(totalRead), FormatBytes(totalBytes));
			Notify();
		}
	}

	static async Task VerifyChecksumAsync(Uri downloadUrl, string filePath, string archiveFileName) {
		// Derive checksums URL from download URL (same directory, different file)
		var checksumUrl = new Uri(downloadUrl, "checksums.sha256");
		try {
			using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
			var checksumText = await client.GetStringAsync(checksumUrl);

			string? expectedHash = null;
			foreach (var line in checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
				// Format: "hash  filename" (GNU sha256sum)
				var parts = line.Split("  ", 2, StringSplitOptions.None);
				if (parts.Length == 2 && parts[1].Trim().Equals(archiveFileName, StringComparison.OrdinalIgnoreCase)) {
					expectedHash = parts[0].Trim().ToLowerInvariant();
					break;
				}
			}

			if (expectedHash == null) {
				// Archive not listed in checksums file — warn but continue
				Logger.Instance.Info($"FFmpeg download: no checksum entry found for '{archiveFileName}', skipping verification");
				return;
			}

			await using var fs = File.OpenRead(filePath);
			var hashBytes = await SHA256.HashDataAsync(fs);
			var actualHash = Convert.ToHexStringLower(hashBytes);

			if (actualHash != expectedHash)
				throw new InvalidOperationException(
					$"Checksum mismatch for '{archiveFileName}': expected {expectedHash}, got {actualHash}. The download may be corrupted or tampered with.");
		}
		catch (HttpRequestException) {
			// Checksums file not available — warn but continue (don't block install)
			Logger.Instance.Info("FFmpeg download: could not fetch checksums.sha256, skipping verification");
		}
	}

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
			SafeExtractZip(archivePath, targetFolder);
			return;
		}

		var psi = new ProcessStartInfo {
			FileName = "tar",
			UseShellExecute = false,
			RedirectStandardError = true,
			RedirectStandardOutput = true
		};
		if (type == ArchiveType.TarXz) {
			psi.ArgumentList.Add("-xJf");
		}
		else if (type == ArchiveType.TarGz) {
			psi.ArgumentList.Add("-xzf");
		}
		else {
			throw new InvalidOperationException("Unsupported archive type");
		}
		psi.ArgumentList.Add(archivePath);
		psi.ArgumentList.Add("-C");
		psi.ArgumentList.Add(targetFolder);
		psi.ArgumentList.Add("--no-absolute-filenames");

		using var process = new Process { StartInfo = psi };
		process.Start();
		process.WaitForExit();
		if (process.ExitCode != 0) {
			string error = process.StandardError.ReadToEnd();
			throw new IOException(string.IsNullOrWhiteSpace(error) ? "Failed to extract archive." : error);
		}
	}

	static void CopyFfmpegFiles(string sourceRoot, string targetFolder, string targetLibFolder) {
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

		var libraryFiles = FFmpegHelper.GenerateLibraryFileNames();
		foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)) {
			var fileName = Path.GetFileName(file);
			if (libraryFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase)) {
				CopyFile(file, Path.Combine(targetLibFolder, fileName));
			}
		}
	}

	static void CopyFile(string sourcePath, string destinationPath) {
		string targetPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? FFToolsUtils.LongPathFix(destinationPath)
			: destinationPath;
		File.Copy(sourcePath, targetPath, true);
	}

	static void SafeExtractZip(string archivePath, string targetFolder) {
		string fullTarget = Path.GetFullPath(targetFolder);
		using var zip = ZipFile.OpenRead(archivePath);
		foreach (var entry in zip.Entries) {
			string dest = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
			if (!dest.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.Ordinal)
				&& dest != fullTarget)
				throw new InvalidOperationException($"ZIP entry '{entry.FullName}' would extract outside target directory");
			if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) {
				Directory.CreateDirectory(dest);
			}
			else {
				Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
				entry.ExtractToFile(dest, true);
			}
		}
	}

	static int MapToFfmpegMajor(int avcodecMajor, int avformatMajor, int avutilMajor) {
		int[] majors = { avcodecMajor, avformatMajor, avutilMajor };
		int want = 0;
		foreach (var m in majors) {
			int v = m switch {
				62 => 8,
				61 => 7,
				60 => 6,
				59 => 5,
				_ => 0
			};
			if (v > want) want = v;
		}
		return want;
	}
}
