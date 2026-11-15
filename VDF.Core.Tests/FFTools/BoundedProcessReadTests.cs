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
/// Regression tests for #865: every ffmpeg/ffprobe run must be bounded. The child stands in
/// for a wedged FFmpeg: it holds the inherited stdout handle open and never finishes, which is
/// what made the old synchronous CopyTo/ReadToEnd (and therefore the WaitForExit timeout behind
/// it) block a scan worker forever. Since .NET 11, FFToolsUtils.RunCaptured does the bounded
/// read with Process.ReadAllBytes.
/// </summary>
public class BoundedProcessReadTests : IDisposable {

	static readonly bool Windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
	readonly string dir = Path.Combine(Path.GetTempPath(), "vdf-bounded-" + Guid.NewGuid().ToString("N"));

	public BoundedProcessReadTests() => Directory.CreateDirectory(dir);

	public void Dispose() {
		try { Directory.Delete(dir, recursive: true); }
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}

	static ProcessStartInfo Shell(string windowsCommand, string unixCommand) {
		var psi = new ProcessStartInfo { FileName = Windows ? "cmd.exe" : "/bin/sh", CreateNoWindow = true };
		if (Windows) {
			// cmd parses its own command line; ArgumentList would escape quotes as \" and break paths.
			psi.Arguments = "/c " + windowsCommand;
		}
		else {
			psi.ArgumentList.Add("-c");
			psi.ArgumentList.Add(unixCommand);
		}
		return psi;
	}

	ProcessStartInfo MarkerAfterFourSeconds(out string marker) {
		marker = Path.Combine(dir, "still-running");
		return Shell($"ping -n 5 127.0.0.1 > nul & echo x > \"{marker}\"", $"sleep 4; touch \"{marker}\"");
	}

	/// <summary>A child that outlives the timeout without closing stdout (the wedged case).</summary>
	static ProcessStartInfo Sleeper() => Shell("ping -n 60 127.0.0.1 > nul", "sleep 60");

	[Fact]
	public void WedgedChild_TimesOutInsteadOfBlockingForever() {
		var sw = Stopwatch.StartNew();

		Assert.Throws<TimeoutException>(() => FFToolsUtils.RunCaptured(Sleeper(), 1_000, "FFmpeg", "wedged.mkv"));

		sw.Stop();
		// The child sleeps for a minute; the old synchronous read returned only when it did.
		Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20),
			$"read was not bounded by the timeout (took {sw.Elapsed.TotalSeconds:F1}s)");
	}

	[Fact]
	public void WedgedChild_IsKilled_SoItCannotOutliveTheScan() {
		// The child would write a marker after 4 s; killed at the 1 s timeout, it never does.
		var psi = MarkerAfterFourSeconds(out string marker);

		Assert.Throws<TimeoutException>(() => FFToolsUtils.RunCaptured(psi, 1_000, "FFmpeg", "wedged.mkv"));

		Thread.Sleep(7_000);
		Assert.False(File.Exists(marker), "the timed-out child was left running");
	}

	[Fact]
	public void Control_TheSameChildLeftAlone_WritesItsMarker() {
		// Without this the test above would pass for a command that never writes anything.
		FFToolsUtils.RunCaptured(MarkerAfterFourSeconds(out string marker), 30_000, "FFmpeg", "slow.mkv");
		Assert.True(File.Exists(marker));
	}

	[Fact]
	public void TimeoutMessage_NamesTheToolAndTheFile() {
		var ex = Assert.Throws<TimeoutException>(() => FFToolsUtils.RunCaptured(Sleeper(), 1_000, "FFprobe", @"E:\Media\stuck.mkv"));

		// The scan logs e.Message; without the path the report is unactionable.
		Assert.Contains(@"E:\Media\stuck.mkv", ex.Message);
		Assert.Contains("FFprobe", ex.Message);
	}

	[Fact]
	public void WellBehavedChild_OutputAndExitCode() {
		var output = FFToolsUtils.RunCaptured(Shell("echo vdf-ok", "echo vdf-ok"), 15_000, "FFmpeg", "fine.mkv");

		Assert.Contains("vdf-ok", Encoding.UTF8.GetString(output.Stdout));
		Assert.Equal(0, output.ExitCode);
	}

	[Fact]
	public void FailingChild_ReportsItsExitCodeAndStderr() {
		// What the frame grabs log on failure: ffmpeg's own words and its exit code.
		var output = FFToolsUtils.RunCaptured(
			Shell("echo broken stream 1>&2 & exit /b 3", "echo broken stream >&2; exit 3"), 15_000, "FFmpeg", "bad.mkv");

		Assert.Equal(3, output.ExitCode);
		Assert.Contains("broken stream", output.Stderr);
	}

	[Fact]
	public void BothPipesFull_NoDeadlock() {
		// A corrupt file can make ffmpeg write a lot of stderr before (or while) it writes the
		// frame. Each pipe holds only a few KB: reading one to the end before the other would
		// stall the child on the full one and time out a perfectly healthy run.
		string big = Path.Combine(dir, "big.txt");
		File.WriteAllText(big, new string('x', 1 << 20)); // text: cmd's type stops at binary bytes
		var psi = Shell($"type \"{big}\" 1>&2 & type \"{big}\"", $"cat \"{big}\" >&2; cat \"{big}\"");

		var output = FFToolsUtils.RunCaptured(psi, 15_000, "FFmpeg", "noisy.mkv");

		Assert.Equal(1 << 20, output.Stdout.Length);
		Assert.Equal(0, output.ExitCode);
	}

	[Fact]
	public void ChildrenDieWithVdf_WhereTheOsCanDoIt() {
		var psi = new ProcessStartInfo("ffmpeg");
		FFToolsUtils.DieWithVdf(psi); // on macOS (no equivalent) it must not throw
		// Windows: job object; Linux: PR_SET_PDEATHSIG.
		if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
			Assert.True(psi.KillOnParentExit);
	}

	[Fact]
	public void FormatStderr_CollapsesRepeats_OneLinePerMessage() {
		string raw = "[hevc] Error constructing the frame RPS\r\n" +
			"[hevc] Error constructing the frame RPS\r\n" +
			"[hevc] Error constructing the frame RPS\r\n" +
			"\r\n" +
			"Invalid data found\n" +
			"[hevc] Error constructing the frame RPS\n" +
			"[hevc] Error constructing the frame RPS\n";

		string nl = Environment.NewLine;
		Assert.Equal(
			$"{nl}[hevc] Error constructing the frame RPS (repeated 2 more times)" +
			$"{nl}Invalid data found" +
			$"{nl}[hevc] Error constructing the frame RPS (repeated 1 more time)",
			FFToolsUtils.FormatStderr(raw));
	}

	[Fact]
	public void FormatStderr_DropsWhatTheCallerFilters_AndNothingIsNothing() {
		Assert.Equal(string.Empty, FFToolsUtils.FormatStderr(string.Empty));
		Assert.Equal(Environment.NewLine + "real error",
			FFToolsUtils.FormatStderr("Referenced QT chapter track not found\nreal error\n", l => !l.Contains("chapter")));
	}
}
