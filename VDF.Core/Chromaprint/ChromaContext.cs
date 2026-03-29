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
// Derived from AcoustID.NET by wo80 (https://github.com/wo80/AcoustID.NET), LGPL 2.1.
// Modernised for net9.0: fixed Chroma allocation, uses Span<double> for frame view.

using VDF.Core.Chromaprint.Pipeline;

namespace VDF.Core.Chromaprint {

	/// <summary>
	/// Orchestrates the full audio fingerprinting pipeline.
	///
	/// Expected usage:
	/// <code>
	///   var ctx = new ChromaContext();
	///   ctx.Start();
	///   ctx.Feed(monoSamples11025Hz);
	///   ctx.Finish();
	///   uint[] fingerprint = ctx.GetRawFingerprint();
	/// </code>
	///
	/// Input PCM must be mono 16-bit samples at 11025 Hz.
	/// Each element in the returned array represents one second of audio encoded
	/// as a 32-bit integer (majority-vote aggregation of ~8 per-frame fingerprints).
	/// </summary>
	public sealed class ChromaContext {
		// ──────────────────────────────────────────────────────────────────────
		// Frame / hop parameters  (standard Chromaprint / AcoustID values)
		// ──────────────────────────────────────────────────────────────────────
		private const int FrameHop = 1365; // samples between consecutive frames
		// Frames per second = 11025 / 1365 ≈ 8.07

		// ──────────────────────────────────────────────────────────────────────
		// Pipeline objects  (allocated once, reused across Start/Finish cycles)
		// ──────────────────────────────────────────────────────────────────────
		private readonly Chroma _chroma = new();
		private readonly ChromaFilter _filter = new();
		private readonly double[] _chromaBuf = new double[12];
		private readonly double[] _filteredBuf = new double[12];

		// ──────────────────────────────────────────────────────────────────────
		// Per-scan state
		// ──────────────────────────────────────────────────────────────────────
		private short[] _samples = Array.Empty<short>();
		private int _sampleCount;

		private readonly List<uint> _secondFrames = new(16);
		private readonly List<uint> _aggregated = new();
		private int _frameIndex;

		/// <summary>Reset state and prepare for a new file.</summary>
		public void Start() {
			_samples = Array.Empty<short>();
			_sampleCount = 0;
			_frameIndex = 0;
			_secondFrames.Clear();
			_aggregated.Clear();
			_filter.Reset();
		}

		/// <summary>
		/// Accepts a block of mono 16-bit PCM samples at 11025 Hz and processes
		/// all complete frames available.  Any leftover samples are retained for
		/// the next call.
		/// </summary>
		public void Feed(ReadOnlySpan<short> samples) {
			// Append incoming samples to the carry buffer
			int needed = _sampleCount + samples.Length;
			if (_samples.Length < needed) {
				var newBuf = new short[needed + Chroma.FrameSize]; // extra headroom
				_samples.AsSpan(0, _sampleCount).CopyTo(newBuf);
				_samples = newBuf;
			}
			samples.CopyTo(_samples.AsSpan(_sampleCount));
			_sampleCount += samples.Length;

			ProcessFrames();
		}

		/// <summary>
		/// Flushes the last partial 1-second bucket.  Call once after all audio
		/// has been fed.
		/// </summary>
		public void Finish() {
			if (_secondFrames.Count > 0) {
				_aggregated.Add(FingerprintCalculator.AggregateMajorityVote(_secondFrames));
				_secondFrames.Clear();
			}
		}

		/// <summary>
		/// Returns the aggregated fingerprint: one <c>uint</c> per second of audio.
		/// Only valid after <see cref="Finish"/> has been called.
		/// </summary>
		public uint[] GetRawFingerprint() => _aggregated.ToArray();

		// ──────────────────────────────────────────────────────────────────────
		// Private helpers
		// ──────────────────────────────────────────────────────────────────────

		private void ProcessFrames() {
			Span<double> frameBuf = stackalloc double[Chroma.FrameSize];
			int pos = 0;

			while (pos + Chroma.FrameSize <= _sampleCount) {
				// Convert short → double normalised to [-1, 1]
				for (int i = 0; i < Chroma.FrameSize; i++)
					frameBuf[i] = _samples[pos + i] * (1.0 / 32768.0);

				// Compute chromagram for this frame
				Array.Clear(_chromaBuf, 0, 12);
				_chroma.Compute(frameBuf, _chromaBuf);

				// Temporal FIR smoothing — produces output only once buffer is primed
				if (_filter.Feed(_chromaBuf, _filteredBuf)) {
					ChromaNormalizer.Normalize(_filteredBuf);
					uint fp = FingerprintCalculator.Compute(_filteredBuf);

					// Determine which 1-second bucket this frame belongs to
					double frameSec = (double)_frameIndex * FrameHop / Chroma.SampleRate;
					double bucket = Math.Floor(frameSec);

					if (_secondFrames.Count > 0 && bucket > _aggregated.Count) {
						// Close the previous bucket
						_aggregated.Add(FingerprintCalculator.AggregateMajorityVote(_secondFrames));
						_secondFrames.Clear();
					}

					_secondFrames.Add(fp);
					_frameIndex++;
				} else {
					_frameIndex++;
				}

				pos += FrameHop;
			}

			// Shift leftover samples to the front of the buffer
			int leftover = _sampleCount - pos;
			if (leftover > 0 && pos > 0)
				Array.Copy(_samples, pos, _samples, 0, leftover);
			_sampleCount = leftover;
		}
	}
}
