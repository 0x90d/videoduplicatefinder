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

using System.Runtime.InteropServices;
using VDF.Core.AI;

namespace VDF.Core.Tests.AI;

public class AiComponentsTests {

	[Fact]
	public void ModelSha256_IsPinned() {
		// The download verifies against this constant — a typo would brick the feature.
		Assert.Equal(64, AiComponents.ModelSha256.Length);
		Assert.All(AiComponents.ModelSha256, c => Assert.True(Uri.IsHexDigit(c)));
	}

	[Theory]
	[InlineData(false, false, false)]
	[InlineData(true, false, true)]
	[InlineData(false, true, true)]
	[InlineData(true, true, true)]
	public void NeedsAiComponents_TracksEveryAiFeature(bool union, bool partial, bool expected) {
		var settings = new Settings { UseAiMatching = union, EnableAiPartialDetection = partial };
		Assert.Equal(expected, settings.NeedsAiComponents);
	}

	[Fact]
	public void NeedsAiComponents_IsNotSerializedIntoSettingsJson() {
		// Computed gate only — persisting it would add a dead key that a hand-edited
		// settings file could set to a value the getter then silently contradicts.
		string json = System.Text.Json.JsonSerializer.Serialize(
			new Settings { UseAiMatching = true }, VDF.Core.Utils.CoreJsonContext.Default.Settings);
		Assert.DoesNotContain("NeedsAiComponents", json);
	}

	/// <summary>A folder holding a runtime library file that will not load.</summary>
	static string BrokenRuntimeFolder() {
		string folder = Directory.CreateTempSubdirectory("vdf-ai-broken").FullName;
		File.WriteAllBytes(Path.Combine(folder, RuntimeFileName), new byte[4096]);
		return folder;
	}

	static string RuntimeFileName => OperatingSystem.IsWindows() ? "onnxruntime.dll"
		: OperatingSystem.IsMacOS() ? "libonnxruntime.dylib"
		: "libonnxruntime.so";

	[Fact]
	public void FindRuntimeLibrary_FindsTheRuntimeShippedWithThisBuild() {
		// The test project is built for any platform, so this is the
		// runtimes/<rid>/native layout the Microsoft.ML.OnnxRuntime package produces.
		string? lib = AiComponents.FindRuntimeLibrary();

		Assert.NotNull(lib);
		Assert.Equal(RuntimeFileName, Path.GetFileName(lib));
	}

	[Fact]
	public void FindRuntimeLibrary_FindsThePortableLayout_WhenDotnetReportsADistroRid() {
		// Distro-built .NET reports a RID like ubuntu.24.04-x64; the package only has portable ones.
		string folder = Directory.CreateTempSubdirectory("vdf-ai-runtime").FullName;
		try {
			AiComponents.TestRuntimeFolder.Value = folder;
			AiComponents.TestRuntimeIdentifier.Value = "distro.1-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
			string portable = Path.Combine(folder, "runtimes", AiComponents.PortableRuntimeIdentifier, "native", RuntimeFileName);
			Directory.CreateDirectory(Path.GetDirectoryName(portable)!);
			File.WriteAllBytes(portable, new byte[16]);

			Assert.Equal(portable, AiComponents.FindRuntimeLibrary());
		}
		finally {
			AiComponents.TestRuntimeFolder.Value = null;
			AiComponents.TestRuntimeIdentifier.Value = null;
			Directory.Delete(folder, true);
		}
	}

	[Fact]
	public void FindRuntimeLibrary_PrefersTheCopyNextToTheApp() {
		string folder = Directory.CreateTempSubdirectory("vdf-ai-runtime").FullName;
		try {
			AiComponents.TestRuntimeFolder.Value = folder;
			Assert.Null(AiComponents.FindRuntimeLibrary());

			string perPlatform = Path.Combine(folder, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", RuntimeFileName);
			Directory.CreateDirectory(Path.GetDirectoryName(perPlatform)!);
			File.WriteAllBytes(perPlatform, new byte[16]);
			Assert.Equal(perPlatform, AiComponents.FindRuntimeLibrary());

			// A publish for one platform (the releases) puts it next to the app.
			string nextToApp = Path.Combine(folder, RuntimeFileName);
			File.WriteAllBytes(nextToApp, new byte[16]);
			Assert.Equal(nextToApp, AiComponents.FindRuntimeLibrary());
		}
		finally {
			AiComponents.TestRuntimeFolder.Value = null;
			Directory.Delete(folder, true);
		}
	}

	[Fact]
	public async Task MissingRuntime_AsksForAReinstallInsteadOfADownload() {
		string folder = Directory.CreateTempSubdirectory("vdf-ai-noruntime").FullName;
		File.WriteAllBytes(Path.Combine(folder, AiComponents.ModelFileName), new byte[16]);
		try {
			AiComponents.TestRuntimeFolder.Value = folder;
			AiComponents.TestAiFolder.Value = folder;
			Assert.Equal(AiComponentsState.RuntimeMissing, AiComponents.GetState());

			var ex = Assert.Throws<InvalidOperationException>(AiComponents.EnsureReady);
			Assert.Contains("Reinstall VDF", ex.Message);
			// The model is present, so nothing is fetched; the download must not report success.
			var downloadEx = await Assert.ThrowsAsync<InvalidOperationException>(() => AiComponents.DownloadAsync(null, CancellationToken.None));
			Assert.Contains("Reinstall VDF", downloadEx.Message);
		}
		finally {
			AiComponents.TestRuntimeFolder.Value = null;
			AiComponents.TestAiFolder.Value = null;
			Directory.Delete(folder, true);
		}
	}

	[Fact]
	public void RemoveDownloadedRuntime_DeletesTheOldRuntimeAndMarkerButKeepsTheModel() {
		string folder = Directory.CreateTempSubdirectory("vdf-ai-legacy").FullName;
		try {
			string[] legacy = { "onnxruntime.dll", "onnxruntime_providers_shared.dll", "onnxruntime.lib", "onnxruntime_providers_shared.lib", "libonnxruntime.so", "libonnxruntime.so.1.23.2", "libonnxruntime.dylib", "runtime.version" };
			string[] keep = { AiComponents.ModelFileName, "notes.txt" };
			foreach (string name in legacy.Concat(keep))
				File.WriteAllBytes(Path.Combine(folder, name), new byte[16]);

			List<string> removed = AiComponents.RemoveDownloadedRuntime(folder);

			Assert.Equal(legacy.Order(), removed.Order());
			Assert.All(legacy, name => Assert.False(File.Exists(Path.Combine(folder, name))));
			Assert.All(keep, name => Assert.True(File.Exists(Path.Combine(folder, name))));
			Assert.Empty(AiComponents.RemoveDownloadedRuntime(Path.Combine(folder, "missing")));
		}
		finally {
			Directory.Delete(folder, true);
		}
	}

	[Fact]
	public async Task MissingRuntimeAndModel_FailsBeforeDownloadingTheModel() {
		string folder = Directory.CreateTempSubdirectory("vdf-ai-nothing").FullName;
		string aiFolder = Path.Combine(folder, "ai");
		try {
			AiComponents.TestRuntimeFolder.Value = folder;
			AiComponents.TestAiFolder.Value = aiFolder;
			Assert.True(AiComponents.IsRuntimeMissing);

			var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AiComponents.DownloadAsync(null, CancellationToken.None));
			Assert.Contains("Reinstall VDF", ex.Message);
			Assert.False(Directory.Exists(aiFolder));
		}
		finally {
			AiComponents.TestRuntimeFolder.Value = null;
			AiComponents.TestAiFolder.Value = null;
			Directory.Delete(folder, true);
		}
	}

	/// <summary>
	/// The reported crash: the runtime would not load (its Visual C++ runtime
	/// was missing), the resolver fell through to default probing, and that bound Windows'
	/// own older System32\onnxruntime.dll, which then failed with "Unable to find an entry
	/// point named 'OrtGetApiBase'" (or an access violation on newer Windows builds).
	/// </summary>
	[Fact]
	public void ResolveRuntimeImport_RuntimeThatWillNotLoad_FailsInsteadOfFallingBackToAnotherOnnxRuntime() {
		string folder = BrokenRuntimeFolder();
		try {
			string lib = Directory.GetFiles(folder).Single();

			var ex = Assert.Throws<InvalidOperationException>(() => AiComponents.ResolveRuntimeImport("onnxruntime", lib));

			Assert.Contains(lib, ex.Message);
			Assert.Contains(AiComponents.RuntimeVersion, ex.Message);
		}
		finally {
			Directory.Delete(folder, true);
		}
	}

	[Fact]
	public void ResolveRuntimeImport_NoRuntime_FallsThroughToDefaultProbing() {
		Assert.Equal(IntPtr.Zero, AiComponents.ResolveRuntimeImport("onnxruntime", null));
		Assert.Equal(IntPtr.Zero, AiComponents.ResolveRuntimeImport("kernel32", @"C:\does\not\matter.dll"));
	}

	[Fact]
	public void EnsureReady_RuntimeThatWillNotLoad_FailsBeforeTheScanStarts() {
		string folder = BrokenRuntimeFolder();
		File.WriteAllBytes(Path.Combine(folder, AiComponents.ModelFileName), new byte[16]);
		try {
			AiComponents.TestRuntimeFolder.Value = folder;
			AiComponents.TestAiFolder.Value = folder;
			// Every file is in place, so the settings page says Ready...
			Assert.Equal(AiComponentsState.Ready, AiComponents.GetState());

			// ...but the scan start (PrepareSearch / PrepareCompare) must stop with the reason.
			var ex = Assert.Throws<InvalidOperationException>(AiComponents.EnsureReady);
			Assert.Contains("could not be loaded", ex.Message);
		}
		finally {
			AiComponents.TestRuntimeFolder.Value = null;
			AiComponents.TestAiFolder.Value = null;
			Directory.Delete(folder, true);
		}
	}

	[Fact]
	public void DescribeRuntimeLoadFailure_NamesTheMissingVcRuntimeAndWhereToGetIt() {
		string message = AiComponents.DescribeRuntimeLoadFailure(@"C:\vdf\ai\onnxruntime.dll", "The specified module could not be found.",
			new[] { "VCRUNTIME140_1.dll", "MSVCP140_1.dll" });

		Assert.Contains(@"C:\vdf\ai\onnxruntime.dll", message);
		Assert.Contains("The specified module could not be found.", message);
		Assert.Contains("Visual C++ Redistributable", message);
		Assert.Contains("VCRUNTIME140_1.dll, MSVCP140_1.dll", message);
		Assert.Contains("https://aka.ms/vc14/vc_redist.", message);
	}

	[Fact]
	public void DescribeRuntimeLoadFailure_DoesNotBlameTheVcRuntimeWhenItIsInstalled() {
		string message = AiComponents.DescribeRuntimeLoadFailure("/state/ai/libonnxruntime.so.1.23.2", "invalid ELF header", Array.Empty<string>());

		Assert.Contains("invalid ELF header", message);
		Assert.DoesNotContain("Visual C++", message);
	}

	[Fact]
	public void MissingVcRuntimeLibraries_ReportsExactlyWhatTheSystemLacks() {
		List<string> missing = AiComponents.MissingVcRuntimeLibraries();

		if (!OperatingSystem.IsWindows()) {
			Assert.Empty(missing);
			return;
		}
		// No false alarms where the redistributable is installed (a misspelled name would
		// show up here), and every absent one is named.
		string[] expected = new[] { "VCRUNTIME140.dll", "VCRUNTIME140_1.dll", "MSVCP140.dll", "MSVCP140_1.dll" }
			.Where(dll => !File.Exists(Path.Combine(Environment.SystemDirectory, dll)))
			.ToArray();
		Assert.Equal(expected, missing);
	}

	[Fact]
	public void EnsureReady_HonorsTestOverride() {
		string? prev = AiComponents.TestOverrideModelPath;
		try {
			AiComponents.TestOverrideModelPath = @"D:\some\model.onnx";
			AiComponents.EnsureReady(); // must not throw
			Assert.Equal(@"D:\some\model.onnx", AiComponents.ModelPath);
		}
		finally {
			AiComponents.TestOverrideModelPath = prev;
		}
	}
}
