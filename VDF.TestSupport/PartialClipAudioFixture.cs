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

using System.Text;

namespace VDF.TestSupport;

// Deterministic original audio with changing chords. Excerpts must be cut from PCM,
// rather than from an already-computed fingerprint: #935 lost their timing phase.
public static class PartialClipAudioFixture {
	public const int SampleRate = 11025;
	public static short[] Generate(int seconds, uint initialSeed = 935) {
		const int rate = 11025;
		double[] pitches = [110, 123.47, 130.81, 146.83, 164.81, 174.61, 196, 220, 246.94, 261.63, 293.66, 329.63];
		double[] ratios = [1, 1.25, 1.5, 2, 2.5, 3];
		uint seed = initialSeed;
		uint Next() { seed = unchecked(seed * 1664525 + 1013904223); return seed; }
		var notes = Enumerable.Range(0, seconds * 4).Select(_ => pitches[Next() % (uint)pitches.Length]).ToArray();
		var phases = Enumerable.Range(0, 6).Select(_ => Next() / (double)uint.MaxValue * Math.Tau).ToArray();
		var samples = new short[seconds * rate];
		for (int i = 0; i < samples.Length; i++) {
			double t = i / (double)rate, f = notes[Math.Min((int)(t * 4), notes.Length - 1)];
			double envelope = .55 + .45 * Math.Pow(Math.Sin(Math.PI * ((t * 4) % 1)), 2);
			double value = 0;
			for (int j = 0; j < ratios.Length; j++) value += Math.Sin(Math.Tau * f * ratios[j] * t + phases[j]) / (j + 1);
			samples[i] = (short)Math.Round(6500 * envelope * value + (Next() / (double)uint.MaxValue * 300 - 150));
		}
		return samples;
	}
	public static void WriteWave(string path, ReadOnlySpan<short> samples) {
		using var writer = new BinaryWriter(File.Create(path));
		int bytes = samples.Length * sizeof(short);
		writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
		writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
		writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate);
		writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
		writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
		foreach (short sample in samples) writer.Write(sample);
	}
}
