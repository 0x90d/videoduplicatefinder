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
using Microsoft.ML.OnnxRuntime;
using VDF.Core.Utils;

namespace VDF.Core.AI {
	public enum AiComponentsState { Missing, RuntimeMissing, ModelMissing, Ready }

	public readonly record struct AiDownloadProgress(string Step, long BytesDone, long? BytesTotal);

	/// <summary>
	/// Locates the two components AI matching needs. The ONNX Runtime library ships with VDF:
	/// the Microsoft.ML.OnnxRuntime package puts the pinned version next to the app on publish.
	/// The DINOv2-small embedding model (SHA256-pinned) is an opt-in download on first use into
	/// <c>{StateFolder}/ai</c>. Under Native AOT the OnnxRuntime wrapper is pure P/Invoke; a
	/// DllImport resolver points its "onnxruntime" import at the shipped library by full path.
	/// </summary>
	public static class AiComponents {
		/// <summary>
		/// Pinned ONNX Runtime version. Must match the Microsoft.ML.OnnxRuntime
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

		public static string AiFolder => TestAiFolder.Value ?? Path.Combine(CoreUtils.StateFolder, "ai");
		public static string ModelPath => TestOverrideModelPath ?? Path.Combine(AiFolder, ModelFileName);

		/// <summary>
		/// Test hook: points ModelPath at the checked-in tiny embedder and makes
		/// EnsureReady succeed, so suites exercise the AI pipeline without downloads
		/// (the native runtime is the one VDF.Core ships, which the test projects get through it).
		/// </summary>
		internal static string? TestOverrideModelPath;

		/// <summary>
		/// Test hook: redirects AiFolder for the calling test's async flow only. AsyncLocal
		/// rather than a static field because OnnxEmbedderTests load the real runtime in
		/// parallel and must not resolve it from another test's folder.
		/// </summary>
		internal static readonly AsyncLocal<string?> TestAiFolder = new();

		/// <summary>Test hook: where to look for the shipped runtime instead of the app folder.</summary>
		internal static readonly AsyncLocal<string?> TestRuntimeFolder = new();

		static bool resolverInstalled;
		static readonly object resolverLock = new();

		/// <summary>
		/// The Visual C++ runtime the pinned Windows build links dynamically (dumpbin
		/// /dependents of onnxruntime.dll 1.23.2). VDF itself is Native AOT and its FFmpeg
		/// is static, so machines without the redistributable run VDF fine until AI matching
		/// tries to load the runtime. Recheck when bumping RuntimeVersion.
		/// </summary>
		static readonly string[] VcRuntimeLibraries = { "VCRUNTIME140.dll", "VCRUNTIME140_1.dll", "MSVCP140.dll", "MSVCP140_1.dll" };

		public static AiComponentsState GetState() {
			bool runtime = FindRuntimeLibrary() != null;
			bool model = File.Exists(ModelPath);
			if (runtime && model) return AiComponentsState.Ready;
			if (runtime) return AiComponentsState.ModelMissing;
			if (model) return AiComponentsState.RuntimeMissing;
			return AiComponentsState.Missing;
		}

		public static bool IsReady => GetState() == AiComponentsState.Ready;

		/// <summary>True when the shipped runtime is absent, so a model download cannot make AI matching work.</summary>
		public static bool IsRuntimeMissing => GetState() is AiComponentsState.Missing or AiComponentsState.RuntimeMissing;

		/// <summary>
		/// Throws with an actionable message when the components are not present, or when
		/// the runtime is present but cannot be loaded (the scan should fail at its start,
		/// not after the file list has been built).
		/// </summary>
		public static void EnsureReady() {
			if (TestOverrideModelPath != null)
				return;
			AiComponentsState state = GetState();
			if (state is AiComponentsState.Missing or AiComponentsState.RuntimeMissing)
				throw new InvalidOperationException(RuntimeMissingMessage);
			if (state != AiComponentsState.Ready)
				throw new InvalidOperationException(
					$"The AI model is not available. Download it in Settings → Matching, or place {ModelFileName} into '{AiFolder}'.");
			LoadRuntime();
		}

		public static string RuntimeMissingMessage =>
			$"The ONNX Runtime {RuntimeVersion} library that ships with VDF was not found in '{TestRuntimeFolder.Value ?? AppContext.BaseDirectory}'. Reinstall VDF.";

		/// <summary>The shipped ONNX Runtime library file, or null when absent.</summary>
		internal static string? FindRuntimeLibrary() {
			string name = OperatingSystem.IsWindows() ? "onnxruntime.dll"
				: OperatingSystem.IsMacOS() ? "libonnxruntime.dylib"
				: "libonnxruntime.so";
			string root = TestRuntimeFolder.Value ?? AppContext.BaseDirectory;
			// A publish for one platform (the releases) puts it next to the app; a build for any
			// platform (dotnet run, test runs) keeps it per platform.
			// The package lays the libraries out under portable RIDs (linux-x64, osx-arm64, ...),
			// but a distro-built .NET reports its own RID (e.g. ubuntu.24.04-x64), so try both.
			string rid = TestRuntimeIdentifier.Value ?? RuntimeInformation.RuntimeIdentifier;
			string[] candidates = {
				Path.Combine(root, name),
				Path.Combine(root, "runtimes", rid, "native", name),
				Path.Combine(root, "runtimes", PortableRuntimeIdentifier, "native", name),
			};
			string? found = candidates.FirstOrDefault(File.Exists);
			// Tests that fake a runtime folder must not delete from the developer's real AiFolder.
			if (found != null && TestRuntimeFolder.Value == null && !legacyRuntimeRemoved) {
				legacyRuntimeRemoved = true;
				RemoveDownloadedRuntime(AiFolder);
			}
			return found;
		}

		static bool legacyRuntimeRemoved;

		/// <summary>The RID the Microsoft.ML.OnnxRuntime package uses for this OS and architecture.</summary>
		internal static string PortableRuntimeIdentifier =>
			$"{(OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux")}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";

		/// <summary>Test hook: stands in for the RID the running .NET reports.</summary>
		internal static readonly AsyncLocal<string?> TestRuntimeIdentifier = new();

		/// <summary>
		/// Deletes the runtime that earlier VDF versions downloaded into <paramref name="folder"/>:
		/// the shipped one replaces it, so those 14-38 MB would otherwise stay for good. Uses the old
		/// downloader's own purge rule (any name containing "onnxruntime", which also catches the
		/// Windows import libraries it copied) plus its version marker, never the model; returns what
		/// it deleted.
		/// </summary>
		internal static List<string> RemoveDownloadedRuntime(string folder) {
			var removed = new List<string>();
			if (!Directory.Exists(folder))
				return removed;
			var files = Directory.EnumerateFiles(folder).Where(file => {
				string name = Path.GetFileName(file);
				return name.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase) || name == "runtime.version";
			});
			foreach (string file in files.ToList()) {
				try {
					File.Delete(file);
					removed.Add(Path.GetFileName(file));
				}
				catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
					Logger.Instance.Warn($"Could not remove the downloaded ONNX Runtime file {file}: {e.Message}");
				}
			}
			return removed;
		}

		/// <summary>
		/// Routes the wrapper's "onnxruntime" DllImport to the shipped library.
		/// Must run before the first OnnxRuntime native call (OnnxEmbedder does so).
		/// SetDllImportResolver may only be called once per assembly, hence the guard.
		/// </summary>
		internal static void EnsureResolverInstalled() {
			if (resolverInstalled) return;
			lock (resolverLock) {
				if (resolverInstalled) return;
				NativeLibrary.SetDllImportResolver(typeof(InferenceSession).Assembly,
					(name, _, _) => ResolveRuntimeImport(name, FindRuntimeLibrary()));
				resolverInstalled = true;
			}
		}

		/// <summary>
		/// Loads the shipped runtime before anything touches an OnnxRuntime type. A load
		/// failure inside the wrapper's static constructor would surface as a
		/// TypeInitializationException that stays cached for the rest of the process, so
		/// installing the missing piece would not help until VDF is restarted.
		/// </summary>
		internal static void LoadRuntime() {
			EnsureResolverInstalled();
			string? lib = FindRuntimeLibrary();
			if (lib != null)
				LoadRuntimeLibrary(lib);
		}

		/// <summary>
		/// The DllImport resolver. No runtime found falls through to default probing. A runtime
		/// that will not load must NOT fall through: Windows has its own onnxruntime.dll in
		/// System32 (Windows ML), and default probing binds to that one. It is an older version,
		/// so the wrapper died with "Unable to find an entry point named 'OrtGetApiBase'" on
		/// older Windows builds and with an access violation on newer ones.
		/// </summary>
		internal static IntPtr ResolveRuntimeImport(string libraryName, string? runtimeLibrary) {
			if (!libraryName.Contains("onnxruntime", StringComparison.OrdinalIgnoreCase) || runtimeLibrary == null)
				return IntPtr.Zero;
			return LoadRuntimeLibrary(runtimeLibrary);
		}

		// Loading an already loaded path just returns the same module, so no caching needed.
		static IntPtr LoadRuntimeLibrary(string lib) {
			try {
				return NativeLibrary.Load(lib);
			}
			catch (Exception e) when (e is DllNotFoundException or BadImageFormatException) {
				throw new InvalidOperationException(DescribeRuntimeLoadFailure(lib, e.Message, MissingVcRuntimeLibraries()), e);
			}
		}

		internal static string DescribeRuntimeLoadFailure(string lib, string reason, IReadOnlyList<string> missingVcRuntime) {
			string message = $"ONNX Runtime {RuntimeVersion} could not be loaded from '{lib}': {reason}";
			if (missingVcRuntime.Count == 0)
				return message;
			string arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
			return message + Environment.NewLine +
				$"The Microsoft Visual C++ Redistributable is missing or outdated (not found: {string.Join(", ", missingVcRuntime)}). " +
				$"Install the current one from https://aka.ms/vc14/vc_redist.{arch}.exe and restart VDF.";
		}

		/// <summary>The VC++ runtime libraries the Windows runtime needs that the OS cannot find.</summary>
		internal static List<string> MissingVcRuntimeLibraries() {
			var missing = new List<string>();
			if (!OperatingSystem.IsWindows())
				return missing;
			foreach (string dll in VcRuntimeLibraries) {
				if (NativeLibrary.TryLoad(dll, out IntPtr handle))
					NativeLibrary.Free(handle);
				else
					missing.Add(dll);
			}
			return missing;
		}

		/// <summary>
		/// Downloads the model if it is missing. Safe to call when Ready. A missing runtime can't
		/// be downloaded, so it fails up front with the reason instead of fetching a model that
		/// could not be used.
		/// </summary>
		public static async Task DownloadAsync(IProgress<AiDownloadProgress>? progress, CancellationToken token) {
			if (FindRuntimeLibrary() == null)
				throw new InvalidOperationException(RuntimeMissingMessage);
			if (!File.Exists(ModelPath)) {
				Directory.CreateDirectory(AiFolder);
				using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
				await DownloadModelAsync(http, progress, token);
			}
		}

		static async Task DownloadModelAsync(HttpClient http, IProgress<AiDownloadProgress>? progress, CancellationToken token) {
			// Unique temp name: a fixed one let two VDF processes (GUI + CLI, or Web + CLI in one
			// container) clobber each other's in-progress download. The final File.Move is atomic.
			string tempPath = ModelPath + $".{Guid.NewGuid():N}.download";
			Action<long, long?> onProgress = (done, total) => progress?.Report(new AiDownloadProgress("AI model", done, total));
			try {
				try {
					await DownloadUtils.DownloadFileAsync(http, new Uri(ModelPrimaryUrl), tempPath, "AI model", onProgress, token);
				}
				catch (Exception e) when (e is not OperationCanceledException) {
					Logger.Instance.Info($"AI model mirror unavailable ({e.Message}), falling back to upstream source.");
					await DownloadUtils.DownloadFileAsync(http, new Uri(ModelFallbackUrl), tempPath, "AI model", onProgress, token);
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

	}
}
