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
// Regression tests for the Stop-poisoning bug: cancelling a scan while a file's
// audio fingerprint was being extracted returned null and permanently flagged
// the entry with AudioFingerprintError + an empty (non-null) fingerprint —
// both of which block every retry gate, so the file was never fingerprinted
// again on later scans.

namespace VDF.Core.Tests;

public class FingerprintCancellationTests {
	private static string CreateTempVideo() {
		string path = Path.Combine(Path.GetTempPath(), $"vdf-cancel-test-{Guid.NewGuid():N}.mp4");
		File.WriteAllBytes(path, Array.Empty<byte>());
		return path;
	}

	[Fact]
	public void StopMidExtraction_DoesNotPoisonEntry() {
		string tmp = CreateTempVideo();
		try {
			var entry = new FileEntry(tmp);
			using var cts = new CancellationTokenSource();
			cts.Cancel();

			ScanEngine.ExtractAudioFingerprint(entry, cts.Token);

			Assert.False(entry.Flags.Has(EntryFlags.AudioFingerprintError));
			Assert.Null(entry.AudioFingerprint); // still eligible for the next scan
		}
		finally {
			File.Delete(tmp);
		}
	}

	[Fact]
	public void GenuineFailure_StillFlagsEntry() {
		// An empty/broken file without cancellation must keep today's behaviour:
		// flagged as error so it is not retried forever.
		string tmp = CreateTempVideo();
		try {
			var entry = new FileEntry(tmp);

			ScanEngine.ExtractAudioFingerprint(entry, CancellationToken.None);

			Assert.True(entry.Flags.Has(EntryFlags.AudioFingerprintError));
			Assert.Equal(Array.Empty<uint>(), entry.AudioFingerprint);
		}
		finally {
			File.Delete(tmp);
		}
	}
}
