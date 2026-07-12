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

using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using VDF.Core.Utils;

namespace VDF.Core.AI {
	public enum AiComponentsState { Missing, RuntimeMissing, ModelMissing, Ready }

	public readonly record struct AiDownloadProgress(string Step, long BytesDone, long? BytesTotal);

	/// <summary>
	/// Locates and downloads the two native components AI matching needs: the ONNX Runtime
	/// library (per-RID archive from the official microsoft/onnxruntime GitHub release,
	/// pinned version) and the DINOv2-small embedding model (SHA256-pinned). Nothing is
	/// bundled with VDF releases — the FFmpeg pattern: opt-in download on first use into
	/// <c>{StateFolder}/ai</c>. Under Native AOT the OnnxRuntime *Managed* wrapper is pure
	/// P/Invoke; a DllImport resolver points its "onnxruntime" import at the downloaded
	/// library, so no native lib has to sit next to the executable.
	/// </summary>
	public static class AiComponents {
		/// <summary>
		/// Pinned ONNX Runtime version. Must match the Microsoft.ML.OnnxRuntime.Managed
		/// PackageReference. 1.23.2 is the newest release that still ships osx-x86_64
		/// binaries (1.24+ dropped Intel macs, which VDF releases still target).
		/// </summary>
		public const string RuntimeVersion = "1.23.2";
		public const string ModelFileName = "dinov2-small-int8.onnx";
		/// <summary>SHA256 of the model file (Xenova/dinov2-small ONNX export, quantized, Apache-2.0).</summary>
		public const string ModelSha256 = "3afdc8bc63b50558d6e5770f5b799bb82455c2311183a2de43803f343a29d917";
		// Primary: mirrored release asset on the VDF repo (stable bytes, hash-pinned).
		// Fallback: the upstream HuggingFace export the mirror was taken from.
		const string ModelPrimaryUrl = "https://github.com/0x90d/videoduplicatefinder/releases/download/ai-models-v1/dinov2-small-int8.onnx";
		const string ModelFallbackUrl = "https://huggingface.co/Xenova/dinov2-small/resolve/main/onnx/model_quantized.onnx";
		const string VersionMarkerFileName = "runtime.version";

		public static string AiFolder => Path.Combine(CoreUtils.StateFolder, "ai");
		public static string ModelPath => TestOverrideModelPath ?? Path.Combine(AiFolder, ModelFileName);

		/// <summary>
		/// Test hook: points ModelPath at the checked-in tiny embedder and makes
		/// EnsureReady succeed, so suites exercise the AI pipeline without downloads
		/// (the native runtime comes from the test projects' full OnnxRuntime package).
		/// </summary>
		internal static string? TestOverrideModelPath;

		static bool resolverInstalled;
		static readonly object resolverLock = new();

		public static AiComponentsState GetState() {
			bool runtime = FindRuntimeLibrary() != null && HasCurrentRuntimeVersion();
			bool model = File.Exists(ModelPath);
			if (runtime && model) return AiComponentsState.Ready;
			if (runtime) return AiComponentsState.ModelMissing;
			if (model) return AiComponentsState.RuntimeMissing;
			return AiComponentsState.Missing;
		}

		public static bool IsReady => GetState() == AiComponentsState.Ready;

		/// <summary>Throws with an actionable message when the components are not present.</summary>
		public static void EnsureReady() {
			if (TestOverrideModelPath != null)
				return;
			AiComponentsState state = GetState();
			if (state == AiComponentsState.Ready) return;
			throw new InvalidOperationException(
				$"AI matching components are not available ({state}). " +
				$"Download them in Settings → Matching, or place onnxruntime {RuntimeVersion} and {ModelFileName} into '{AiFolder}'.");
		}

		static bool HasCurrentRuntimeVersion() {
			try {
				string marker = Path.Combine(AiFolder, VersionMarkerFileName);
				return File.Exists(marker) && File.ReadAllText(marker).Trim() == RuntimeVersion;
			}
			catch { return false; }
		}

		/// <summary>The downloaded ONNX Runtime library file, or null when absent.</summary>
		internal static string? FindRuntimeLibrary() {
			try {
				if (!Directory.Exists(AiFolder)) return null;
				// Windows: onnxruntime.dll — Linux: libonnxruntime.so.<ver> — macOS: libonnxruntime.<ver>.dylib
				return Directory.EnumerateFiles(AiFolder)
					.Where(f => {
						string name = Path.GetFileName(f);
						return name.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase) &&
							!name.Contains("providers", StringComparison.OrdinalIgnoreCase) &&
							(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
							 name.Contains(".so", StringComparison.OrdinalIgnoreCase) ||
							 name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase));
					})
					.OrderByDescending(f => Path.GetFileName(f).Length) // prefer the fully-versioned real file over a bare symlink copy
					.FirstOrDefault();
			}
			catch { return null; }
		}

		/// <summary>
		/// Routes the Managed wrapper's "onnxruntime" DllImport to the downloaded library.
		/// Must run before the first OnnxRuntime native call (OnnxEmbedder does so).
		/// SetDllImportResolver may only be called once per assembly, hence the guard.
		/// </summary>
		internal static void EnsureResolverInstalled() {
			if (resolverInstalled) return;
			lock (resolverLock) {
				if (resolverInstalled) return;
				NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly, (name, _, _) => {
					if (!name.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase))
						return IntPtr.Zero;
					string? lib = FindRuntimeLibrary();
					if (lib != null && NativeLibrary.TryLoad(lib, out IntPtr handle))
						return handle;
					return IntPtr.Zero; // fall through to default probing (PATH / app dir)
				});
				resolverInstalled = true;
			}
		}

		internal static (Uri Url, string ArchiveFileName) GetRuntimeDownloadPlan() {
			string os =
				RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
				RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" :
				RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
				throw new PlatformNotSupportedException("AI matching is not supported on this operating system.");
			string arch = RuntimeInformation.ProcessArchitecture switch {
				Architecture.X64 => os == "osx" ? "x86_64" : "x64",
				Architecture.Arm64 => os == "linux" ? "aarch64" : "arm64",
				_ => throw new PlatformNotSupportedException($"AI matching is not supported on {RuntimeInformation.ProcessArchitecture}.")
			};
			string ext = os == "win" ? "zip" : "tgz";
			string file = $"onnxruntime-{os}-{arch}-{RuntimeVersion}.{ext}";
			return (new Uri($"https://github.com/microsoft/onnxruntime/releases/download/v{RuntimeVersion}/{file}"), file);
		}

		/// <summary>Downloads whatever is missing (runtime and/or model). Safe to call when Ready.</summary>
		public static async Task DownloadAsync(IProgress<AiDownloadProgress>? progress, CancellationToken token) {
			Directory.CreateDirectory(AiFolder);
			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

			if (FindRuntimeLibrary() == null || !HasCurrentRuntimeVersion())
				await DownloadRuntimeAsync(http, progress, token);

			if (!File.Exists(ModelPath))
				await DownloadModelAsync(http, progress, token);
		}

		static async Task DownloadRuntimeAsync(HttpClient http, IProgress<AiDownloadProgress>? progress, CancellationToken token) {
			(Uri url, string archiveName) = GetRuntimeDownloadPlan();
			// Per-attempt temp dir: a fixed shared path let two VDF processes (GUI + CLI,
			// or Web + CLI in one container) clobber each other's in-progress download —
			// FileShare.None collisions, or one process's cleanup deleting the other's files.
			string tempRoot = Path.Combine(Path.GetTempPath(), $"VDF.AiDownload.{Guid.NewGuid():N}");
			Directory.CreateDirectory(tempRoot);
			string archivePath = Path.Combine(tempRoot, archiveName);
			try {
				await DownloadFileAsync(http, url, archivePath, $"ONNX Runtime {RuntimeVersion}", progress, token);

				string extractDir = Path.Combine(tempRoot, "extracted");
				Directory.CreateDirectory(extractDir);
				if (archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
					ZipFile.ExtractToDirectory(archivePath, extractDir);
				else
					ExtractTarGz(archivePath, extractDir);

				// Purge any previously installed runtime BEFORE copying: the Linux/macOS
				// library names carry the version (libonnxruntime.so.1.23.2), so upgraded
				// installs would otherwise accumulate versions and FindRuntimeLibrary's
				// tie-break could keep loading the stale one while the marker claims the
				// new version — with GetState() reporting Ready, only deleting the ai
				// folder by hand would have recovered.
				foreach (string old in Directory.EnumerateFiles(AiFolder)) {
					string oldName = Path.GetFileName(old);
					if (oldName.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase))
						try { File.Delete(old); } catch { /* loaded/locked — overwritten below */ }
				}

				// Archives lay out onnxruntime-<rid>-<ver>/lib/<libraries>; flatten lib/ into AiFolder.
				string? libDir = Directory.EnumerateDirectories(extractDir, "lib", SearchOption.AllDirectories).FirstOrDefault();
				if (libDir == null)
					throw new IOException($"Downloaded ONNX Runtime archive has no lib/ directory ({archiveName}).");
				foreach (string file in Directory.EnumerateFiles(libDir)) {
					string name = Path.GetFileName(file);
					if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
					File.Copy(file, Path.Combine(AiFolder, name), overwrite: true);
				}
				await File.WriteAllTextAsync(Path.Combine(AiFolder, VersionMarkerFileName), RuntimeVersion, token);
				if (FindRuntimeLibrary() == null)
					throw new IOException("ONNX Runtime archive extracted but no runtime library was found in it.");
			}
			finally {
				try { Directory.Delete(tempRoot, true); } catch { }
			}
		}

		static async Task DownloadModelAsync(HttpClient http, IProgress<AiDownloadProgress>? progress, CancellationToken token) {
			// Unique temp name for the same two-process reason as the runtime download;
			// the final File.Move is atomic either way.
			string tempPath = ModelPath + $".{Guid.NewGuid():N}.download";
			try {
				try {
					await DownloadFileAsync(http, new Uri(ModelPrimaryUrl), tempPath, "AI model", progress, token);
				}
				catch (Exception e) when (e is not OperationCanceledException) {
					Logger.Instance.Info($"AI model mirror unavailable ({e.Message}), falling back to upstream source.");
					await DownloadFileAsync(http, new Uri(ModelFallbackUrl), tempPath, "AI model", progress, token);
				}

				string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(tempPath, token)));
				if (!hash.Equals(ModelSha256, StringComparison.OrdinalIgnoreCase))
					throw new IOException($"AI model download failed the integrity check (SHA256 {hash}, expected {ModelSha256}).");
				File.Move(tempPath, ModelPath, overwrite: true);
			}
			finally {
				try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
			}
		}

		// With ResponseHeadersRead, HttpClient.Timeout only covers the headers — the body
		// reads below are otherwise unguarded, and a connection that stalls mid-transfer
		// (no data, no FIN) would hang forever; the GUI shows a modal busy overlay with no
		// cancel during this download, so "forever" meant killing the app.
		static readonly TimeSpan ReadStallTimeout = TimeSpan.FromSeconds(90);

		static async Task DownloadFileAsync(HttpClient http, Uri url, string destination, string displayName, IProgress<AiDownloadProgress>? progress, CancellationToken token) {
			using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
			response.EnsureSuccessStatusCode();
			long? total = response.Content.Headers.ContentLength;
			await using Stream source = await response.Content.ReadAsStreamAsync(token);
			await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
			var buffer = new byte[81920];
			long readTotal = 0;
			while (true) {
				int read;
				using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(token)) {
					readCts.CancelAfter(ReadStallTimeout);
					try {
						read = await source.ReadAsync(buffer, readCts.Token);
					}
					catch (OperationCanceledException) when (!token.IsCancellationRequested) {
						throw new TimeoutException($"The {displayName} download stalled (no data received for {ReadStallTimeout.TotalSeconds:0} seconds).");
					}
				}
				if (read == 0)
					break;
				await target.WriteAsync(buffer.AsMemory(0, read), token);
				readTotal += read;
				progress?.Report(new AiDownloadProgress(displayName, readTotal, total));
			}
		}

		static void ExtractTarGz(string archivePath, string targetFolder) {
			// Only reached on Linux/macOS, where tar is part of the base system —
			// the same approach the FFmpeg downloader uses for .tar.xz archives.
			var psi = new ProcessStartInfo {
				FileName = "tar",
				CreateNoWindow = true,
				RedirectStandardError = true,
			};
			psi.ArgumentList.Add("-xzf");
			psi.ArgumentList.Add(archivePath);
			psi.ArgumentList.Add("-C");
			psi.ArgumentList.Add(targetFolder);
			using Process process = Process.Start(psi) ?? throw new IOException("Failed to start tar.");
			string error = process.StandardError.ReadToEnd();
			process.WaitForExit();
			if (process.ExitCode != 0)
				throw new IOException(string.IsNullOrWhiteSpace(error) ? "Failed to extract archive." : error);
		}
	}
}
