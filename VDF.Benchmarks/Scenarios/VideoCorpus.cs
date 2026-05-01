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

using VDF.Core.FFTools;
using VDF.TestSupport;

namespace VDF.Benchmarks.Scenarios;

/// <summary>
/// Generates synthetic test videos lazily, caching them in a stable per-machine
/// folder so repeated benchmark runs don't pay the encode cost. Files are keyed
/// by (codec, resolution, duration) — change those and a new file is produced;
/// keep them and the cached file is reused.
/// </summary>
public static class VideoCorpus {
	/// <summary>Stable cache root, shared across runs. Cleaned manually when needed.</summary>
	public static string CacheDir { get; } =
		Path.Combine(Path.GetTempPath(), "vdf_bench_corpus");

	public static string FfmpegPath => FfmpegEngine.FFmpegPath;

	public static bool FfmpegAvailable =>
		!string.IsNullOrEmpty(FfmpegPath) && File.Exists(FfmpegPath);

	/// <summary>Codec families covered by the corpus.</summary>
	public enum Codec { H264, HEVC10, VP9 }

	public sealed record Spec(Codec Codec, int Width, int Height, int Duration) {
		public string FileName => Codec switch {
			Codec.H264 => $"h264_{Width}x{Height}_{Duration}s.mp4",
			Codec.HEVC10 => $"hevc10_{Width}x{Height}_{Duration}s.mp4",
			Codec.VP9 => $"vp9_{Width}x{Height}_{Duration}s.webm",
			_ => throw new ArgumentOutOfRangeException(nameof(Codec))
		};
	}

	/// <summary>
	/// Returns the path to the cached file for <paramref name="spec"/>, generating
	/// it via <see cref="TestVideoGenerator"/> if missing. Returns null if FFmpeg
	/// CLI is unavailable or the encoder for the requested codec is missing.
	/// </summary>
	public static string? Ensure(Spec spec) {
		if (!FfmpegAvailable) return null;
		Directory.CreateDirectory(CacheDir);
		string path = Path.Combine(CacheDir, spec.FileName);
		if (File.Exists(path) && new FileInfo(path).Length > 0)
			return path;

		bool ok = spec.Codec switch {
			Codec.H264 => TestVideoGenerator.GenerateH264(FfmpegPath, path, spec.Width, spec.Height, spec.Duration),
			Codec.HEVC10 =>
				TestVideoGenerator.HasEncoder(FfmpegPath, "libx265") &&
				TestVideoGenerator.GenerateHEVC10(FfmpegPath, path, spec.Width, spec.Height, spec.Duration),
			Codec.VP9 =>
				TestVideoGenerator.HasEncoder(FfmpegPath, "libvpx-vp9") &&
				TestVideoGenerator.GenerateVP9(FfmpegPath, path, spec.Width, spec.Height, spec.Duration),
			_ => false
		};
		if (!ok) {
			try { if (File.Exists(path)) File.Delete(path); } catch { }
			return null;
		}
		return path;
	}
}
