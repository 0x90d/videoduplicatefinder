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
using System.Runtime.InteropServices;
using VDF.Core.Chromaprint;
using VDF.Core.Chromaprint.Pipeline;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {

	/// <summary>
	/// Extracts audio from a video file via FFmpeg and computes a Chromaprint-style
	/// audio fingerprint stored as an array of aggregated 1-second <c>uint</c> blocks.
	/// </summary>
	internal static class ChromaprintEngine {
		private const int TimeoutMs = 120_000; // 2 minutes max per file
		private const int TargetSampleRate = 11025;
		private const int TargetChannels = 1;

		/// <summary>
		/// Extracts the audio fingerprint for <paramref name="filePath"/>.
		/// Returns <c>null</c> when the file has no audio stream or extraction fails.
		/// </summary>
		internal static uint[]? ExtractFingerprint(string filePath, bool extendedLogging) {
			var psi = new ProcessStartInfo {
				FileName = FfmpegEngine.FFmpegPath,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = extendedLogging,
				WindowStyle = ProcessWindowStyle.Hidden,
				WorkingDirectory = Path.GetDirectoryName(FfmpegEngine.FFmpegPath) ?? string.Empty
			};

			psi.ArgumentList.Add("-hide_banner");
			psi.ArgumentList.Add("-loglevel");
			psi.ArgumentList.Add(extendedLogging ? "error" : "quiet");
			psi.ArgumentList.Add("-nostdin");
			psi.ArgumentList.Add("-i");
			psi.ArgumentList.Add(FFToolsUtils.LongPathFix(filePath));
			psi.ArgumentList.Add("-vn");                                // drop video
			psi.ArgumentList.Add("-ac"); psi.ArgumentList.Add(TargetChannels.ToString());
			psi.ArgumentList.Add("-ar"); psi.ArgumentList.Add(TargetSampleRate.ToString());
			psi.ArgumentList.Add("-f");  psi.ArgumentList.Add("s16le"); // raw 16-bit LE PCM
			psi.ArgumentList.Add("pipe:1");

			using var process = new Process { StartInfo = psi };
			string errOutput = string.Empty;

			try {
				process.Start();

				if (extendedLogging) {
					process.ErrorDataReceived += (_, e) => {
						if (e.Data?.Length > 0)
							errOutput += Environment.NewLine + e.Data;
					};
					process.BeginErrorReadLine();
				}

				// Read all PCM bytes from stdout before waiting for exit to avoid deadlock
				byte[] pcmBytes;
				using (var ms = new MemoryStream()) {
					process.StandardOutput.BaseStream.CopyTo(ms);
					pcmBytes = ms.ToArray();
				}

				process.WaitForExit(TimeoutMs);

				if (extendedLogging && errOutput.Length > 0)
					Logger.Instance.Info($"[ChromaprintEngine] {Path.GetFileName(filePath)}: {errOutput}");

				if (pcmBytes.Length < Chroma.FrameSize * 2) // too short to fingerprint
					return null;

				return ComputeFingerprint(pcmBytes);
			}
			catch (Exception ex) {
				Logger.Instance.Info($"[ChromaprintEngine] Failed on '{filePath}': {ex.Message}");
				return null;
			}
		}

		private static uint[] ComputeFingerprint(byte[] pcmBytes) {
			// Reinterpret raw bytes as little-endian int16 samples
			int sampleCount = pcmBytes.Length / 2;
			var samples = MemoryMarshal.Cast<byte, short>(pcmBytes.AsSpan(0, sampleCount * 2));

			var ctx = new ChromaContext();
			ctx.Start();
			ctx.Feed(samples);
			ctx.Finish();
			return ctx.GetRawFingerprint();
		}
	}
}
