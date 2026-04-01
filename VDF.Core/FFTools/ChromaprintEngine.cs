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
using VDF.Core.FFTools.FFmpegNative;
using VDF.Core.Utils;

namespace VDF.Core.FFTools {

	/// <summary>
	/// Extracts audio from a video file via FFmpeg and computes a Chromaprint-style
	/// audio fingerprint stored as an array of aggregated 1-second <c>uint</c> blocks.
	/// </summary>
	internal static class ChromaprintEngine {
		private const int TimeoutMs = 30_000; // 30 seconds max for process exit after stream ends
		private const int TargetSampleRate = 11025;
		private const int TargetChannels = 1;
		// Read PCM in 32 KB chunks — keeps memory low while giving ChromaContext
		// enough samples to process multiple frames per iteration.
		private const int ReadBufferSize = 32_768;

		/// <summary>
		/// Extracts the audio fingerprint for <paramref name="filePath"/>.
		/// Returns <c>null</c> when the file has no audio stream or extraction fails.
		/// Returns an empty array when the file has no usable audio.
		/// </summary>
		internal static uint[]? ExtractFingerprint(string filePath, bool extendedLogging, CancellationToken ct = default) {
			if (FfmpegEngine.UseNativeBinding) {
				try {
					return ExtractFingerprintNative(filePath, extendedLogging, ct);
				}
				catch (Exception e) {
					Logger.Instance.Info(
						$"[ChromaprintEngine] Native binding failed on '{Path.GetFileName(filePath)}', " +
						$"falling back to process mode. Exception: {e.Message}");
				}
			}
			return ExtractFingerprintProcess(filePath, extendedLogging, ct);
		}

		/// <summary>Native path: uses FFmpeg.AutoGen bindings — no process spawning.</summary>
		private static uint[]? ExtractFingerprintNative(string filePath, bool extendedLogging, CancellationToken ct) {
			var sw = extendedLogging ? Stopwatch.StartNew() : null;

			using var decoder = new AudioStreamDecoder(filePath, TargetSampleRate, ct);
			if (!decoder.HasAudioStream)
				return Array.Empty<uint>();

			var ctx = new ChromaContext();
			ctx.Start();
			int totalSamples = decoder.DecodeAll(samples => ctx.Feed(samples), ct);

			if (ct.IsCancellationRequested)
				return null;

			if (totalSamples < Chroma.FrameSize) // too short to fingerprint
				return null;

			ctx.Finish();
			var result = ctx.GetRawFingerprint();

			if (extendedLogging)
				Logger.Instance.Info($"[ChromaprintEngine] {Path.GetFileName(filePath)}: " +
					$"native, total={sw!.ElapsedMilliseconds}ms, " +
					$"samples={totalSamples}, blocks={result.Length}, " +
					$"thread={Environment.CurrentManagedThreadId}");

			return result;
		}

		/// <summary>CLI fallback: spawns an FFmpeg process and streams PCM from stdout.</summary>
		private static uint[]? ExtractFingerprintProcess(string filePath, bool extendedLogging, CancellationToken ct) {
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
				var sw = extendedLogging ? Stopwatch.StartNew() : null;
				process.Start();

				if (extendedLogging) {
					process.ErrorDataReceived += (_, e) => {
						if (e.Data?.Length > 0)
							errOutput += Environment.NewLine + e.Data;
					};
					process.BeginErrorReadLine();
				}

				// Stream PCM directly into ChromaContext in small chunks instead of
				// buffering the entire audio into memory.  This allows Chromaprint to
				// process frames in parallel with FFmpeg's decode and keeps memory flat.
				var ctx = new ChromaContext();
				ctx.Start();

				var stream = process.StandardOutput.BaseStream;
				var buf = new byte[ReadBufferSize];
				int totalBytes = 0;
				// Carry buffer for an odd trailing byte from the previous read
				// (PCM samples are 2 bytes each, reads may return odd byte counts)
				byte leftoverByte = 0;
				bool hasLeftover = false;

				while (true) {
					if (ct.IsCancellationRequested) {
						KillProcess(process);
						return null;
					}

					int bytesRead = stream.Read(buf, 0, buf.Length);
					if (bytesRead <= 0) break;

					totalBytes += bytesRead;

					// Prepend leftover byte from previous iteration if any
					byte[]? merged = null;
					if (hasLeftover) {
						merged = new byte[1 + bytesRead];
						merged[0] = leftoverByte;
						Buffer.BlockCopy(buf, 0, merged, 1, bytesRead);
						bytesRead += 1;
						hasLeftover = false;
					}

					// If odd number of bytes, save the last one for next iteration
					byte[] source = merged ?? buf;
					if (bytesRead % 2 != 0) {
						leftoverByte = source[bytesRead - 1];
						hasLeftover = true;
						bytesRead--;
					}

					if (bytesRead >= 2) {
						var samples = MemoryMarshal.Cast<byte, short>(
							source.AsSpan(0, bytesRead));
						ctx.Feed(samples);
					}
				}

				if (ct.IsCancellationRequested) {
					KillProcess(process);
					return null;
				}

				process.WaitForExit(TimeoutMs);

				if (extendedLogging && errOutput.Length > 0)
					Logger.Instance.Info($"[ChromaprintEngine] {Path.GetFileName(filePath)}: {errOutput}");

				if (totalBytes < Chroma.FrameSize * 2) // too short to fingerprint
					return null;

				ctx.Finish();
				var result = ctx.GetRawFingerprint();

				if (extendedLogging)
					Logger.Instance.Info($"[ChromaprintEngine] {Path.GetFileName(filePath)}: " +
						$"process, total={sw!.ElapsedMilliseconds}ms, " +
						$"pcm={totalBytes / 1024}KB, blocks={result.Length}");

				return result;
			}
			catch (OperationCanceledException) {
				KillProcess(process);
				return null;
			}
			catch (Exception ex) {
				Logger.Instance.Info($"[ChromaprintEngine] Failed on '{filePath}': {ex.Message}");
				KillProcess(process);
				return null;
			}
		}

		private static void KillProcess(Process process) {
			try {
				if (!process.HasExited)
					process.Kill();
			}
			catch { }
		}
	}
}
