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

namespace VDF.TestSupport;

/// <summary>
/// Generates small synthetic test videos via FFmpeg CLI using lavfi sources.
/// Shared between VDF.IntegrationTests (correctness) and VDF.Benchmarks (perf).
/// </summary>
public static class TestVideoGenerator {
	static bool RunFfmpeg(string ffmpegPath, string arguments, int timeoutMs = 60_000) {
		var psi = new ProcessStartInfo {
			FileName = ffmpegPath,
			Arguments = arguments,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			WorkingDirectory = Path.GetDirectoryName(ffmpegPath)!,
		};
		try {
			using var p = Process.Start(psi)!;
			var stderrTask = p.StandardError.ReadToEndAsync();
			p.StandardOutput.ReadToEnd();
			p.WaitForExit(timeoutMs);
			return p.ExitCode == 0;
		}
		catch {
			return false;
		}
	}

	public static bool HasEncoder(string ffmpegPath, string encoderName) {
		var psi = new ProcessStartInfo {
			FileName = ffmpegPath,
			Arguments = "-hide_banner -encoders",
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			WorkingDirectory = Path.GetDirectoryName(ffmpegPath)!,
		};
		try {
			using var p = Process.Start(psi)!;
			var stderrTask = p.StandardError.ReadToEndAsync();
			string output = p.StandardOutput.ReadToEnd();
			p.WaitForExit(10_000);
			return p.ExitCode == 0 && output.Contains(encoderName);
		}
		catch {
			return false;
		}
	}

	/// <summary>
	/// 2s 320x240 H.264 8-bit yuv420p with a deterministic test pattern.
	/// </summary>
	public static bool GenerateH264_8bit(string ffmpegPath, string outputPath) =>
		RunFfmpeg(ffmpegPath,
			$"-y -f lavfi -i testsrc2=duration=2:size=320x240:rate=25 " +
			$"-c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p \"{outputPath}\"");

	/// <summary>
	/// 2s 320x240 HEVC 10-bit yuv420p10le with a deterministic test pattern.
	/// </summary>
	public static bool GenerateHEVC_10bit(string ffmpegPath, string outputPath) =>
		RunFfmpeg(ffmpegPath,
			$"-y -f lavfi -i testsrc2=duration=2:size=320x240:rate=25 " +
			$"-c:v libx265 -preset ultrafast -crf 28 -pix_fmt yuv420p10le \"{outputPath}\"");

	/// <summary>
	/// 2s 320x240 VP9 8-bit with a deterministic test pattern.
	/// </summary>
	public static bool GenerateVP9(string ffmpegPath, string outputPath) =>
		RunFfmpeg(ffmpegPath,
			$"-y -f lavfi -i testsrc2=duration=2:size=320x240:rate=25 " +
			$"-c:v libvpx-vp9 -crf 30 -b:v 0 -pix_fmt yuv420p \"{outputPath}\"");

	/// <summary>
	/// 2s 320x240 H.264 with a visually different pattern (color bars).
	/// </summary>
	public static bool GenerateH264_Different(string ffmpegPath, string outputPath) =>
		RunFfmpeg(ffmpegPath,
			$"-y -f lavfi -i smptebars=duration=2:size=320x240:rate=25 " +
			$"-c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p \"{outputPath}\"");

	/// <summary>
	/// H.264 with the bitstream deliberately corrupted via the noise BSF, used to
	/// regression-test the AVERROR_INVALIDDATA tolerance in <c>VideoStreamDecoder.TryDecodeFrame</c>.
	/// </summary>
	public static bool GenerateH264_Corrupted(string ffmpegPath, string cleanInputPath, string outputPath) =>
		RunFfmpeg(ffmpegPath,
			$"-y -i \"{cleanInputPath}\" -c copy -bsf:v \"noise=amount=20:dropamount=8\" \"{outputPath}\"");

	/// <summary>
	/// Generic H.264 generator for benchmarks. Lets callers vary duration and resolution
	/// to expose decode/seek scaling characteristics. Output is yuv420p, ultrafast.
	/// </summary>
	public static bool GenerateH264(string ffmpegPath, string outputPath, int width, int height, int durationSeconds, int fps = 25) =>
		RunFfmpeg(ffmpegPath,
			string.Format(CultureInfo.InvariantCulture,
				"-y -f lavfi -i testsrc2=duration={0}:size={1}x{2}:rate={3} " +
				"-c:v libx264 -preset ultrafast -crf 23 -pix_fmt yuv420p \"{4}\"",
				durationSeconds, width, height, fps, outputPath),
			timeoutMs: Math.Max(30_000, durationSeconds * 2_000));

	/// <summary>
	/// Generic HEVC 10-bit generator for benchmarks.
	/// </summary>
	public static bool GenerateHEVC10(string ffmpegPath, string outputPath, int width, int height, int durationSeconds, int fps = 25) =>
		RunFfmpeg(ffmpegPath,
			string.Format(CultureInfo.InvariantCulture,
				"-y -f lavfi -i testsrc2=duration={0}:size={1}x{2}:rate={3} " +
				"-c:v libx265 -preset ultrafast -crf 28 -pix_fmt yuv420p10le \"{4}\"",
				durationSeconds, width, height, fps, outputPath),
			timeoutMs: Math.Max(30_000, durationSeconds * 4_000));

	/// <summary>
	/// Generic VP9 generator for benchmarks.
	/// </summary>
	public static bool GenerateVP9(string ffmpegPath, string outputPath, int width, int height, int durationSeconds, int fps = 25) =>
		RunFfmpeg(ffmpegPath,
			string.Format(CultureInfo.InvariantCulture,
				"-y -f lavfi -i testsrc2=duration={0}:size={1}x{2}:rate={3} " +
				"-c:v libvpx-vp9 -crf 30 -b:v 0 -pix_fmt yuv420p \"{4}\"",
				durationSeconds, width, height, fps, outputPath),
			timeoutMs: Math.Max(60_000, durationSeconds * 6_000));
}
