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
using FFmpeg.AutoGen;
using SixLabors.ImageSharp;
using VDF.Core;
using VDF.Core.FFTools;
using VDF.Core.FFTools.FFmpegNative;

namespace VDF.Benchmarks.Scenarios;

/// <summary>
/// Direct phase-timing probe — bypasses BenchmarkDotNet so we can see exactly how
/// much of <c>GetGrayBytesFromVideo</c>'s per-position cost is the file-open / sws-init
/// versus the seek + decode + scale work. Run with:
///
///   dotnet run -c Release --project VDF.Benchmarks -- --probe-decoder-reuse
///
/// Prints a table to stdout. Not a [Benchmark] — invoked from <c>Program.Main</c> on flag.
/// </summary>
public static class DecoderReuseProbe {
	const int Iterations = 8;

	public static int Run(string[] args) {
		if (!VideoCorpus.FfmpegAvailable) {
			Console.Error.WriteLine("FFmpeg CLI not on PATH; cannot provision corpus.");
			return 1;
		}
		// Probe two scenarios so we can see how the savings scale:
		//   long-file:  60s 1280x720 H.264 (typical)
		//   short-file: 10s 320x240 H.264 (open-cost share is bigger here)
		var specs = new[] {
			new VideoCorpus.Spec(VideoCorpus.Codec.H264, 1280, 720, 60),
			new VideoCorpus.Spec(VideoCorpus.Codec.H264, 320, 240, 10),
			new VideoCorpus.Spec(VideoCorpus.Codec.HEVC10, 1280, 720, 60),
		};
		var paths = specs.Select(s => (s, p: VideoCorpus.Ensure(s))).ToList();
		var firstPath = paths.FirstOrDefault(x => x.p != null).p;
		if (firstPath == null) {
			Console.Error.WriteLine("Could not provision any corpus video.");
			return 1;
		}

		// Mirror what GetGrayBytesFromVideo asks for: 4 evenly-spaced positions.
		List<TimeSpan> PositionsFor(double durationSeconds) {
			var list = new List<TimeSpan>();
			for (int i = 1; i <= 4; i++)
				list.Add(TimeSpan.FromSeconds(durationSeconds * i / 5.0));
			return list;
		}

		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.none;

		// Trigger FFmpeg.AutoGen lazy library lookup. ScanEngine normally does this; without
		// it the dynamic bindings fail with NotSupportedException on first call.
		if (!ScanEngine.NativeFFmpegExists) {
			Console.Error.WriteLine("Native FFmpeg libraries not found.");
			return 1;
		}

		Console.WriteLine($"== Decoder reuse probe ==");
		Console.WriteLine($"iterations:    {Iterations} (first warmup pair excluded)");
		Console.WriteLine();

		foreach (var (spec, path) in paths) {
			if (path == null) {
				Console.WriteLine($"-- {spec.Codec} {spec.Width}x{spec.Height} {spec.Duration}s: (encoder unavailable, skipped) --");
				Console.WriteLine();
				continue;
			}
			var positions = PositionsFor(spec.Duration);
			Console.WriteLine($"### {spec.Codec} {spec.Width}x{spec.Height} {spec.Duration}s");
			Console.WriteLine($"positions (s): {string.Join(", ", positions.Select(p => p.TotalSeconds.ToString("0.0")))}");

			_ = MeasureSeparateOpens(path, positions);
			_ = MeasureSharedDecoder(path, positions);

			var separateRuns = new List<PhaseTimings>();
			var sharedRuns = new List<PhaseTimings>();
			for (int i = 0; i < Iterations; i++) {
				separateRuns.Add(MeasureSeparateOpens(path, positions));
				sharedRuns.Add(MeasureSharedDecoder(path, positions));
			}

			PrintDelta(separateRuns, sharedRuns);
			Console.WriteLine();
		}
		return 0;
	}

	struct PhaseTimings {
		public long OpenMs;
		public long SwsInitMs;
		public long SeekDecodeScaleCopyMs;
		public long TotalMs;
	}

	/// <summary>Mimics the OLD per-position behavior: open + sws_getContext + decode + scale + copy, ×N.</summary>
	static unsafe PhaseTimings MeasureSeparateOpens(string path, List<TimeSpan> positions) {
		var total = Stopwatch.StartNew();
		long openMs = 0, swsMs = 0, decodeMs = 0;
		for (int i = 0; i < positions.Count; i++) {
			var sw = Stopwatch.StartNew();
			using var vsd = new VideoStreamDecoder(path);
			openMs += sw.ElapsedMilliseconds;

			sw.Restart();
			if (!vsd.TryDecodeFrame(out var srcFrame, positions[i]))
				throw new Exception("decode failed");
			AVPixelFormat srcPixFmt = vsd.PixelFormat;
			using var vfc = new VideoFrameConverter(
				vsd.FrameSize, srcPixFmt,
				new Size(32, 32), AVPixelFormat.AV_PIX_FMT_GRAY8,
				VideoFrameConverter.ScaleQuality.Bicubic, false);
			swsMs += sw.ElapsedMilliseconds;

			sw.Restart();
			AVFrame converted = vfc.Convert(srcFrame);
			byte[] outBuf = new byte[32 * 32];
			fixed (byte* dst = outBuf)
				Buffer.MemoryCopy(converted.data[0], dst, 32 * 32, 32 * 32);
			decodeMs += sw.ElapsedMilliseconds;
		}
		return new PhaseTimings { OpenMs = openMs, SwsInitMs = swsMs, SeekDecodeScaleCopyMs = decodeMs, TotalMs = total.ElapsedMilliseconds };
	}

	/// <summary>Mimics the NEW batch path: 1× open + 1× sws + N× (decode + scale + copy).</summary>
	static unsafe PhaseTimings MeasureSharedDecoder(string path, List<TimeSpan> positions) {
		var total = Stopwatch.StartNew();
		var sw = Stopwatch.StartNew();
		using var vsd = new VideoStreamDecoder(path);
		long openMs = sw.ElapsedMilliseconds;

		VideoFrameConverter? vfc = null;
		long swsMs = 0, decodeMs = 0;
		try {
			for (int i = 0; i < positions.Count; i++) {
				sw.Restart();
				if (!vsd.TryDecodeFrame(out var srcFrame, positions[i]))
					throw new Exception("decode failed");

				if (vfc == null) {
					AVPixelFormat srcPixFmt = vsd.PixelFormat;
					vfc = new VideoFrameConverter(
						vsd.FrameSize, srcPixFmt,
						new Size(32, 32), AVPixelFormat.AV_PIX_FMT_GRAY8,
						VideoFrameConverter.ScaleQuality.Bicubic, false);
					swsMs += sw.ElapsedMilliseconds;
					sw.Restart();
				}

				AVFrame converted = vfc.Convert(srcFrame);
				byte[] outBuf = new byte[32 * 32];
				fixed (byte* dst = outBuf)
					Buffer.MemoryCopy(converted.data[0], dst, 32 * 32, 32 * 32);
				decodeMs += sw.ElapsedMilliseconds;
			}
		}
		finally {
			vfc?.Dispose();
		}
		return new PhaseTimings { OpenMs = openMs, SwsInitMs = swsMs, SeekDecodeScaleCopyMs = decodeMs, TotalMs = total.ElapsedMilliseconds };
	}

	static void PrintComparison(string label, List<PhaseTimings> runs) {
		Console.WriteLine($"-- {label} --");
		Console.WriteLine("  iter  open  sws   decode  total (ms)");
		long openSum = 0, swsSum = 0, decSum = 0, totSum = 0;
		for (int i = 0; i < runs.Count; i++) {
			var r = runs[i];
			Console.WriteLine($"  {i,4}  {r.OpenMs,4}  {r.SwsInitMs,3}  {r.SeekDecodeScaleCopyMs,6}  {r.TotalMs,5}");
			openSum += r.OpenMs; swsSum += r.SwsInitMs; decSum += r.SeekDecodeScaleCopyMs; totSum += r.TotalMs;
		}
		int n = runs.Count;
		Console.WriteLine($"  mean  {openSum / n,4}  {swsSum / n,3}  {decSum / n,6}  {totSum / n,5}");
		Console.WriteLine();
	}

	static void PrintDelta(List<PhaseTimings> separate, List<PhaseTimings> shared) {
		long sepTotal = 0, shrTotal = 0;
		long sepOpen = 0, shrOpen = 0;
		long sepSws = 0, shrSws = 0;
		long sepDec = 0, shrDec = 0;
		for (int i = 0; i < separate.Count; i++) {
			sepTotal += separate[i].TotalMs; shrTotal += shared[i].TotalMs;
			sepOpen += separate[i].OpenMs; shrOpen += shared[i].OpenMs;
			sepSws += separate[i].SwsInitMs; shrSws += shared[i].SwsInitMs;
			sepDec += separate[i].SeekDecodeScaleCopyMs; shrDec += shared[i].SeekDecodeScaleCopyMs;
		}
		int n = separate.Count;
		double pct = sepTotal == 0 ? 0 : 100.0 * (sepTotal - shrTotal) / sepTotal;
		Console.WriteLine($"-- Delta (mean of {n} iterations) --");
		Console.WriteLine($"  open:    {sepOpen / n,4} -> {shrOpen / n,4} ms  (saved {(sepOpen - shrOpen) / n} ms)");
		Console.WriteLine($"  sws:     {sepSws / n,4} -> {shrSws / n,4} ms  (saved {(sepSws - shrSws) / n} ms)");
		Console.WriteLine($"  decode:  {sepDec / n,4} -> {shrDec / n,4} ms");
		Console.WriteLine($"  TOTAL:   {sepTotal / n,4} -> {shrTotal / n,4} ms  ({pct:0.0}% faster)");
	}
}
