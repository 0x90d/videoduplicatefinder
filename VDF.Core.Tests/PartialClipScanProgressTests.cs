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
// Drives the real ScanForPartialDuplicates over a hand-built fingerprint database, so the
// audio pass's progress accounting and its match results are exercised together. The visual
// gate is left off here (it decodes frames off disk); PartialClipProgressTests covers it
// through the injected verifier. Regression cover for #831.

using System.Collections.Concurrent;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // ScanForPartialDuplicates reads the shared static database
public class PartialClipScanProgressTests : IDisposable {

	readonly List<FileEntry> added = new();

	public void Dispose() {
		foreach (var e in added)
			DatabaseUtils.Database.Remove(e);
	}

	FileEntry Add(string name, double durationSeconds, uint[] fingerprint) {
		var entry = new FileEntry {
			_Path = @"C:\vdf-partial-" + Guid.NewGuid().ToString("N") + "\\" + name,
			FileSize = 1,
			invalid = false,
			IsImage = false,
			AudioFingerprint = fingerprint,
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(durationSeconds) },
		};
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	// Distinct, non-zero blocks: an all-zero fingerprint is treated as a silent track and skipped.
	static readonly uint[] SourceFingerprint =
		{ 0x0F0F0F0F, 0x12345678, 0xA5A5A5A5, 0xDEADBEEF, 0xCAFEBABE, 0x11223344, 0x55667788, 0x99AABBCC, 0x0BADF00D, 0xFEEDFACE };

	/// <summary>Blocks 2..4 of the source: an exact sub-window, so the sliding compare matches at 100%.</summary>
	static uint[] ClipFingerprint => SourceFingerprint[2..5];

	static ScanEngine NewEngine() {
		var engine = new ScanEngine();
		engine.Settings.EnablePartialClipDetection = true;
		engine.Settings.PartialClipRequireVisualMatch = false; // no frame decoding in this test
		engine.ElapsedTimer.Start();
		return engine;
	}

	/// <summary>
	/// The audio pass must count a source as done only once its whole row of pairs is checked,
	/// and must open its phase on an empty bar rather than inheriting the previous phase's.
	/// </summary>
	[Fact]
	public void AudioPass_ReportsItsOwnStageFromEmptyToFull_AndFindsThePartialClip() {
		var engine = NewEngine();
		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();
		engine.Progress += (_, e) => events.Enqueue(e);

		Add("source.mp4", 100, SourceFingerprint);
		Add("unrelated.mp4", 50, Enumerable.Repeat(0xFFFFFFFFu, 6).ToArray());
		var clip = Add("clip.mp4", 30, ClipFingerprint);

		engine.ScanForPartialDuplicates();

		var partial = events.Where(e => e.CurrentStage == "comparing partial clips").ToList();
		Assert.NotEmpty(partial);

		// 3 eligible videos => the shortest can never be a source => a maximum of 2.
		Assert.All(partial, e => Assert.Equal(2, e.MaxPosition));
		Assert.Equal(0, partial.First().CurrentPosition);
		Assert.Equal(2, partial.Last().CurrentPosition);

		// The counter never runs ahead of the work: no event may claim a position it hasn't reached.
		Assert.Equal(partial.Select(e => e.CurrentPosition).OrderBy(p => p), partial.Select(e => e.CurrentPosition));

		// Behavior preserved: the clip is grouped with its source and flagged.
		var clipResult = Assert.Single(engine.Duplicates, d => d.Path == clip.Path);
		Assert.True(clipResult.Flags.HasFlag(DuplicateFlags.PartialClip));
		Assert.Equal(2, engine.Duplicates.Count); // source + clip, no singleton for "unrelated"
	}

	/// <summary>
	/// The last thing the phase reports must be a full bar. The old code hit the maximum when the
	/// final iteration *started*, so this held for the wrong reason; it must still hold now that
	/// the counter advances on completion.
	/// </summary>
	[Fact]
	public void AudioPass_FinalEventIsAFullBar() {
		var engine = NewEngine();
		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();
		engine.Progress += (_, e) => events.Enqueue(e);

		Add("source.mp4", 100, SourceFingerprint);
		Add("clip.mp4", 30, ClipFingerprint);

		engine.ScanForPartialDuplicates();

		var last = events.Last();
		Assert.Equal(last.MaxPosition, last.CurrentPosition);
		Assert.Equal("comparing partial clips", last.CurrentStage);
		Assert.Equal(TimeSpan.Zero, last.Remaining); // and never a negative "~0s left"
	}

	/// <summary>Fewer than two eligible videos: the phase bails out before touching progress.</summary>
	[Fact]
	public void AudioPass_WithNothingToCompare_ReportsNoProgressAndFindsNothing() {
		var engine = NewEngine();
		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();
		engine.Progress += (_, e) => events.Enqueue(e);

		Add("lonely.mp4", 100, SourceFingerprint);

		engine.ScanForPartialDuplicates();

		Assert.Empty(events);
		Assert.Empty(engine.Duplicates);
	}
}
