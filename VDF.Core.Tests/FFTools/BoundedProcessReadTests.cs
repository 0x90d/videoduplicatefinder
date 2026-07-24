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
using System.Runtime.InteropServices;
using System.Text;
using VDF.Core.FFTools;

namespace VDF.Core.Tests.FFTools;

/// <summary>
/// Regression tests for #865: every ffmpeg/ffprobe stdout read must be bounded. The child
/// stands in for a wedged FFmpeg — it holds the inherited stdout handle open and never
/// finishes, which is what made the old synchronous CopyTo/ReadToEnd (and therefore the
/// WaitForExit timeout behind it) block a scan worker forever.
/// </summary>
public class BoundedProcessReadTests {

	static Process StartShell(string windowsCommand, string unixCommand) {
		bool windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
		var psi = new ProcessStartInfo {
			FileName = windows ? "cmd.exe" : "/bin/sh",
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardOutput = true,
		};
		psi.ArgumentList.Add(windows ? "/c" : "-c");
		psi.ArgumentList.Add(windows ? windowsCommand : unixCommand);
		var process = new Process { StartInfo = psi };
		process.Start();
		return process;
	}

	/// <summary>A child that outlives the timeout without closing stdout (the wedged case).</summary>
	static Process StartSleeper() => StartShell("ping -n 60 127.0.0.1 > nul", "sleep 60");

	[Fact]
	public void WedgedChild_TimesOutInsteadOfBlockingForever() {
		using Process process = StartSleeper();
		var sw = Stopwatch.StartNew();
		using var ms = new MemoryStream();

		Assert.Throws<TimeoutException>(() =>
			FFToolsUtils.ReadStdoutBounded(process, ms, 1_000, "FFmpeg", "wedged.mkv"));

		sw.Stop();
		// The child sleeps for a minute; the old synchronous read returned only when it did.
		Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20),
			$"read was not bounded by the timeout (took {sw.Elapsed.TotalSeconds:F1}s)");
	}

	[Fact]
	public void WedgedChild_IsKilled_SoItCannotOutliveTheScan() {
		using Process process = StartSleeper();
		using var ms = new MemoryStream();

		Assert.Throws<TimeoutException>(() =>
			FFToolsUtils.ReadStdoutBounded(process, ms, 1_000, "FFmpeg", "wedged.mkv"));

		Assert.True(process.WaitForExit(10_000), "the timed-out child was left running");
	}

	[Fact]
	public void TimeoutMessage_NamesTheFile() {
		using Process process = StartSleeper();
		using var ms = new MemoryStream();

		var ex = Assert.Throws<TimeoutException>(() =>
			FFToolsUtils.ReadStdoutBounded(process, ms, 1_000, "FFprobe", @"E:\Media\stuck.mkv"));

		// The scan logs e.Message; without the path the report is unactionable.
		Assert.Contains(@"E:\Media\stuck.mkv", ex.Message);
		Assert.Contains("FFprobe", ex.Message);
	}

	[Fact]
	public void WellBehavedChild_OutputIsCopiedAndExitCodeObserved() {
		using Process process = StartShell("echo vdf-ok", "echo vdf-ok");
		using var ms = new MemoryStream();

		FFToolsUtils.ReadStdoutBounded(process, ms, 15_000, "FFmpeg", "fine.mkv");

		Assert.Contains("vdf-ok", Encoding.UTF8.GetString(ms.ToArray()));
		Assert.Equal(0, process.ExitCode);
	}

	[Fact]
	public void Cancellation_EndsTheReadAndKillsTheChild() {
		using Process process = StartSleeper();
		using var ms = new MemoryStream();
		using var cts = new CancellationTokenSource();
		cts.CancelAfter(300);
		var sw = Stopwatch.StartNew();

		// Stop must not have to wait out the full timeout of an in-flight decode.
		Assert.ThrowsAny<OperationCanceledException>(() =>
			FFToolsUtils.ReadStdoutBounded(process, ms, 60_000, "FFmpeg", "wedged.mkv", cts.Token));

		sw.Stop();
		Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20),
			$"cancellation was not observed (took {sw.Elapsed.TotalSeconds:F1}s)");
		Assert.True(process.WaitForExit(10_000), "the canceled child was left running");
	}
}
