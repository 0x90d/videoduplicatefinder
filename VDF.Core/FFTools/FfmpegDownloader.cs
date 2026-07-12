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

using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FFmpeg.AutoGen;
using VDF.Core.FFTools.FFmpegNative;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {
	internal enum FfmpegDownloadPhase { Downloading, Verifying, Extracting }

	internal readonly record struct FfmpegDownloadProgress(FfmpegDownloadPhase Phase, string DisplayName, long BytesDone, long? BytesTotal);

	internal sealed record FfmpegDownloadPlan(Uri DownloadUrl, string ArchiveFileName, ArchiveKind ArchiveKind, string DisplayName);

	/// <summary>
	/// Downloads and installs the shared FFmpeg/FFprobe build matching the compiled-in
	/// FFmpeg.AutoGen binding version. One implementation for every frontend — the GUI
	/// and the Web UI previously each carried a full copy of this logic, and only the
	/// AI component downloader had the mid-transfer stall guard.
	/// </summary>
	internal static class FfmpegDownloader {
		internal const long MaxDownloadBytes = 500 * 1024 * 1024; // 500 MB safety cap

		internal enum DownloadOS { Windows, Linux, OSX }

		const string BtbNRepo = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";
		const string YtDlpRepo = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/";

		/// <summary>The FFmpeg major version the compiled FFmpeg.AutoGen binding expects, or 0 when unknown.</summary>
		internal static int MapToFfmpegMajor(int avcodecMajor, int avformatMajor, int avutilMajor) {
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

		internal static string VersionTagForMajor(int ffMajor) => ffMajor switch {
			// BtbN/yt-dlp builds publish only the latest minor per major; the n8.0 tag
			// was retired when 8.1 landed, so a hardcoded "8.0" 404s on every fresh
			// install. Keep this aligned with whatever minor BtbN currently ships.
			8 => "8.1",
			7 => "7.1",
			6 => "6.1",
			5 => "5.1",
			_ => "7.1"
		};

		static string GetVersionTag() => VersionTagForMajor(
			MapToFfmpegMajor(ffmpeg.LIBAVCODEC_VERSION_MAJOR, ffmpeg.LIBAVFORMAT_VERSION_MAJOR, ffmpeg.LIBAVUTIL_VERSION_MAJOR));

		internal static List<FfmpegDownloadPlan> GetDownloadPlans() {
			DownloadOS? os =
				RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? DownloadOS.Windows :
				RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? DownloadOS.Linux :
				RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? DownloadOS.OSX :
				null;
			if (os == null)
				return new List<FfmpegDownloadPlan>();
			return GetDownloadPlans(os.Value, RuntimeInformation.ProcessArchitecture, GetVersionTag());
		}

		internal static List<FfmpegDownloadPlan> GetDownloadPlans(DownloadOS os, Architecture arch, string versionTag) {
			(string Rid, ArchiveKind Kind, string Repo, string Display)? pick = os switch {
				DownloadOS.Windows => arch switch {
					Architecture.X64 => ("win64", ArchiveKind.Zip, BtbNRepo, "Windows x64"),
					Architecture.X86 => ("win32", ArchiveKind.Zip, BtbNRepo, "Windows x86"),
					Architecture.Arm64 => ("winarm64", ArchiveKind.Zip, BtbNRepo, "Windows ARM64"),
					_ => ((string, ArchiveKind, string, string)?)null
				},
				DownloadOS.Linux => arch switch {
					Architecture.X64 => ("linux64", ArchiveKind.TarXz, BtbNRepo, "Linux x64"),
					Architecture.X86 => ("linux32", ArchiveKind.TarXz, BtbNRepo, "Linux x86"),
					Architecture.Arm64 => ("linuxarm64", ArchiveKind.TarXz, BtbNRepo, "Linux ARM64"),
					Architecture.Arm => ("linuxarmhf", ArchiveKind.TarXz, BtbNRepo, "Linux ARMHF"),
					_ => ((string, ArchiveKind, string, string)?)null
				},
				DownloadOS.OSX => arch switch {
					Architecture.X64 => ("macos64", ArchiveKind.Zip, YtDlpRepo, "macOS x64"),
					Architecture.Arm64 => ("macosarm64", ArchiveKind.Zip, YtDlpRepo, "macOS ARM64"),
					_ => ((string, ArchiveKind, string, string)?)null
				},
				_ => null
			};
			if (pick == null)
				return new List<FfmpegDownloadPlan>();
			(string rid, ArchiveKind kind, string repo, string display) = pick.Value;
			string ext = kind == ArchiveKind.Zip ? "zip" : "tar.xz";
			string file = $"ffmpeg-n{versionTag}-latest-{rid}-gpl-shared-{versionTag}.{ext}";
			return new List<FfmpegDownloadPlan> {
				new(new Uri(repo + file), file, kind, $"{display} ({versionTag})")
			};
		}

		/// <summary>
		/// Downloads, verifies, extracts and installs FFmpeg/FFprobe into the app folder.
		/// Returns the bin target folder on success. Throws PlatformNotSupportedException
		/// when no build exists for this platform/architecture, the last
		/// HttpRequestException when every plan's download failed, and lets extraction/
		/// installation errors (IOException, UnauthorizedAccessException, checksum
		/// mismatch) propagate — callers map those onto their own user messaging.
		/// </summary>
		internal static async Task<string> DownloadAndInstallAsync(IProgress<FfmpegDownloadProgress>? progress, CancellationToken token) {
			List<FfmpegDownloadPlan> plans = GetDownloadPlans();
			if (plans.Count == 0)
				throw new PlatformNotSupportedException("No FFmpeg download is available for this platform/architecture.");

			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
			HttpRequestException? lastHttpError = null;
			foreach (FfmpegDownloadPlan plan in plans) {
				// Per-attempt temp dir: a fixed shared path let two VDF processes clobber
				// each other's in-progress download (same hardening as the AI downloader).
				string tempRoot = Path.Combine(Path.GetTempPath(), $"VDF.FFmpegDownload.{Guid.NewGuid():N}");
				try {
					string downloadPath = Path.Combine(tempRoot, plan.ArchiveFileName);
					string extractDir = Path.Combine(tempRoot, "extracted");
					Directory.CreateDirectory(extractDir);

					try {
						await DownloadUtils.DownloadFileAsync(http, plan.DownloadUrl, downloadPath, plan.DisplayName,
							(done, total) => progress?.Report(new FfmpegDownloadProgress(FfmpegDownloadPhase.Downloading, plan.DisplayName, done, total)),
							token, MaxDownloadBytes);
						progress?.Report(new FfmpegDownloadProgress(FfmpegDownloadPhase.Verifying, plan.DisplayName, 0, null));
						await VerifyChecksumAsync(http, plan.DownloadUrl, downloadPath, plan.ArchiveFileName, token);
						progress?.Report(new FfmpegDownloadProgress(FfmpegDownloadPhase.Extracting, plan.DisplayName, 0, null));
						ArchiveUtils.Extract(downloadPath, extractDir, plan.ArchiveKind);
						return InstallFromExtracted(extractDir);
					}
					catch (HttpRequestException ex) {
						lastHttpError = ex; // try the next plan (if any)
					}
				}
				finally {
					try { Directory.Delete(tempRoot, true); } catch { }
				}
			}
			throw lastHttpError!;
		}

		static string InstallFromExtracted(string extractedFolder) {
			string targetFolder = Path.Combine(CoreUtils.CurrentFolder, "bin");
			Directory.CreateDirectory(targetFolder);
			string targetLibFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
				? Path.Combine(CoreUtils.CurrentFolder, "lib")
				: targetFolder;
			Directory.CreateDirectory(targetLibFolder);
			CopyFfmpegFiles(extractedFolder, targetFolder, targetLibFolder);
			return targetFolder;
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

		/// <summary>Extracts the hash for <paramref name="archiveFileName"/> from a GNU sha256sum listing, or null.</summary>
		internal static string? TryParseExpectedChecksum(string checksumText, string archiveFileName) {
			foreach (var line in checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
				// Format: "hash  filename" (GNU sha256sum)
				var parts = line.Split("  ", 2, StringSplitOptions.None);
				if (parts.Length == 2 && parts[1].Trim().Equals(archiveFileName, StringComparison.OrdinalIgnoreCase))
					return parts[0].Trim().ToLowerInvariant();
			}
			return null;
		}

		/// <summary>
		/// Best-effort integrity check: an unreachable or slow checksums.sha256 only logs
		/// a warning (the install must not depend on a second endpoint being up), but a
		/// present-and-mismatching hash aborts with InvalidOperationException.
		/// </summary>
		static async Task VerifyChecksumAsync(HttpClient http, Uri downloadUrl, string filePath, string archiveFileName, CancellationToken token) {
			// Derive checksums URL from download URL (same directory, different file)
			var checksumUrl = new Uri(downloadUrl, "checksums.sha256");
			try {
				using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
				cts.CancelAfter(TimeSpan.FromSeconds(30));
				string checksumText = await http.GetStringAsync(checksumUrl, cts.Token);

				string? expectedHash = TryParseExpectedChecksum(checksumText, archiveFileName);
				if (expectedHash == null) {
					Logger.Instance.Warn($"FFmpeg download: no checksum entry found for '{archiveFileName}', skipping verification");
					return;
				}

				await using var fs = File.OpenRead(filePath);
				var actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, token));

				if (actualHash != expectedHash)
					throw new InvalidOperationException(
						$"Checksum mismatch for '{archiveFileName}': expected {expectedHash}, got {actualHash}. The download may be corrupted or tampered with.");
			}
			catch (HttpRequestException) {
				Logger.Instance.Warn("FFmpeg download: could not fetch checksums.sha256, skipping verification");
			}
			catch (OperationCanceledException) when (!token.IsCancellationRequested) {
				// The 30 s fetch budget elapsed. Previously this bubbled up as a
				// TaskCanceledException and aborted the whole install, defeating the
				// best-effort intent — a slow checksum host must not block FFmpeg.
				Logger.Instance.Warn("FFmpeg download: checksum fetch timed out, skipping verification");
			}
		}
	}
}
