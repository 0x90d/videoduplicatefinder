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
// Regression tests for issue #831: a partial-clip scan looked hung. The audio pass
// legitimately finished at 6722/6722, then the visual gate ran for minutes without
// raising a single Progress event, so the counter, the ETA and the elapsed clock all
// stood still while the scan was in fact still working.

using System.Collections.Concurrent;

namespace VDF.Core.Tests;

public class PartialClipProgressTests {

	// The gate only reads paths (progress text, log lines); the frame decoding it would do
	// lives behind the injected verifier, so these entries never need to exist on disk.
	static FileEntry Video(string name) => new() { _Path = Path.Combine(Path.GetTempPath(), name) };

	/// <summary>index 0 is the source, 1..count are its candidate clips.</summary>
	static List<(int sourceIdx, int clipIdx, float sim, int offsetSec, Guid groupId)> Assignments(int count) {
		var list = new List<(int, int, float, int, Guid)>();
		for (int i = 0; i < count; i++)
			list.Add((0, i + 1, 0.9f, 0, Guid.NewGuid()));
		return list;
	}

	static List<FileEntry> VideosFor(int assignmentCount) {
		var videos = new List<FileEntry> { Video("source.mp4") };
		for (int i = 0; i < assignmentCount; i++)
			videos.Add(Video($"clip{i}.mp4"));
		return videos;
	}

	/// <summary>
	/// The core of #831: the gate must run as its own progress phase. Before the fix it
	/// raised nothing at all, leaving the audio pass's "6722 / 6722, ~0s left" on screen
	/// for the whole (possibly much longer) duration of the frame-decoding gate.
	/// </summary>
	[Fact]
	public void VisualGate_RaisesProgressUnderItsOwnStageAndReachesItsOwnMaximum() {
		const int assignmentCount = 5;
		var engine = new ScanEngine();
		engine.ElapsedTimer.Start();

		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();
		engine.Progress += (_, e) => events.Enqueue(e);

		// Stand in for the earlier audio pass: it ends with a full bar over its own maximum.
		engine.InitProgress(6722);
		for (int i = 0; i < 6722; i++)
			engine.IncrementProgress("audio.mp4");
		Assert.Equal(6722, events.Last().CurrentPosition);
		Assert.Equal(6722, events.Last().MaxPosition);

		engine.RunPartialClipVisualGate(VideosFor(assignmentCount), Assignments(assignmentCount),
			(_, _, _) => (true, 1f));

		var gateEvents = events.Where(e => e.CurrentStage == "verifying partial clips").ToList();
		Assert.NotEmpty(gateEvents);
		Assert.All(gateEvents, e => Assert.Equal(assignmentCount, e.MaxPosition));

		// The phase opens on an empty bar and closes on a full one — a live phase, not a frozen one.
		Assert.Equal(0, gateEvents.First().CurrentPosition);
		Assert.Equal(assignmentCount, gateEvents.Last().CurrentPosition);
	}

	/// <summary>Every assignment is verified exactly once, and only the passing ones come back.</summary>
	[Fact]
	public void VisualGate_KeepsPassingAssignmentsAndVerifiesEachExactlyOnce() {
		const int assignmentCount = 6;
		var engine = new ScanEngine();
		var verified = new ConcurrentBag<string>();

		var kept = engine.RunPartialClipVisualGate(VideosFor(assignmentCount), Assignments(assignmentCount),
			(_, clip, _) => {
				verified.Add(clip.Path);
				// Drop the odd-numbered clips.
				return (int.Parse(Path.GetFileNameWithoutExtension(clip.Path)["clip".Length..]) % 2 == 0, 0.5f);
			});

		Assert.Equal(assignmentCount, verified.Count);
		Assert.Equal(assignmentCount, verified.Distinct().Count());
		Assert.Equal(3, kept.Count);
		Assert.Equal(kept.OrderBy(k => k.clipIdx).ToList(), kept); // deterministic order
	}

	/// <summary>
	/// The gate never consulted the pause token, so Pause was a no-op for the whole phase —
	/// the visible Pause button did nothing while the scan looked hung.
	/// </summary>
	[Fact]
	public async Task VisualGate_HonorsPause() {
		var engine = new ScanEngine();
		engine.pauseTokenSource.IsPaused = true;

		int verifyCalls = 0;
		var gate = Task.Run(() => engine.RunPartialClipVisualGate(VideosFor(3), Assignments(3),
			(_, _, _) => { Interlocked.Increment(ref verifyCalls); return (true, 1f); }));

		var early = await Task.WhenAny(gate, Task.Delay(TimeSpan.FromMilliseconds(300)));
		Assert.True(early != gate, "The visual gate ran straight through a paused scan.");
		Assert.Equal(0, Volatile.Read(ref verifyCalls));

		engine.pauseTokenSource.IsPaused = false;
		var kept = await gate.WaitAsync(TimeSpan.FromSeconds(30));
		Assert.Equal(3, kept.Count);
		Assert.Equal(3, Volatile.Read(ref verifyCalls));
	}

	/// <summary>
	/// The last item's push has processed == maxPosition, which drove the old expression
	/// one tick negative. It rendered as "~0s left" — on a phase that was still running.
	/// </summary>
	[Theory]
	[InlineData(10, 10)] // final item: nothing left
	[InlineData(9, 10)]  // second to last: the extrapolation floors at zero too
	[InlineData(11, 10)] // counter overshoots its maximum (a phase re-init race)
	public void EstimateRemaining_AtOrPastTheLastItem_IsNeverNegative(int processed, int maxPosition) =>
		Assert.Equal(TimeSpan.Zero, ScanEngine.EstimateRemaining(TimeSpan.FromMinutes(5), processed, maxPosition));

	[Fact]
	public void EstimateRemaining_MidPhase_ExtrapolatesLinearly() {
		// One minute spent on 1 of 3 items => two more items => two more minutes.
		var remaining = ScanEngine.EstimateRemaining(TimeSpan.FromMinutes(1), processed: 0, maxPosition: 3);
		Assert.Equal(TimeSpan.FromMinutes(2), remaining);
	}

	/// <summary>
	/// Elapsed and Remaining only ever reached a frontend on a Progress event, so a phase that
	/// stalled between file completions froze the whole status block — the visual signature of
	/// #831. The heartbeat re-sends the last snapshot with a live clock.
	/// </summary>
	[Fact]
	public async Task Heartbeat_RepublishesTheLastSnapshotWithAFreshClock() {
		var engine = new ScanEngine();
		engine.ElapsedTimer.Start();

		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();
		engine.InitProgress(10);
		engine.IncrementProgress("stalled.mp4");
		engine.Progress += (_, e) => events.Enqueue(e);

		// Progress is still fresh: the heartbeat must not talk over the workers.
		engine.EmitProgressHeartbeat();
		Assert.Empty(events);

		await Task.Delay(TimeSpan.FromMilliseconds(400)); // outlast progressUpdateIntervall
		engine.EmitProgressHeartbeat();

		Assert.Single(events);
		Assert.True(events.Single().Elapsed > TimeSpan.Zero);
		Assert.Equal(1, events.Single().CurrentPosition); // counter unchanged: only the clock moved
		Assert.Equal(10, events.Single().MaxPosition);
	}

	/// <summary>
	/// The heartbeat fires on a threadpool timer thread, outside the scan task's catch. A
	/// subscriber that throws there would take the whole process down over a cosmetic clock tick.
	/// </summary>
	[Fact]
	public async Task Heartbeat_SurvivesAThrowingProgressSubscriber() {
		var engine = new ScanEngine();
		engine.ElapsedTimer.Start();
		engine.InitProgress(10);
		engine.IncrementProgress("stalled.mp4");
		engine.Progress += (_, _) => throw new InvalidOperationException("a frontend blew up");

		await Task.Delay(TimeSpan.FromMilliseconds(400));

		engine.HeartbeatTick(); // must swallow; the raw EmitProgressHeartbeat would propagate
		Assert.Throws<InvalidOperationException>(engine.EmitProgressHeartbeat);
	}

	/// <summary>Pause stops ElapsedTimer on purpose — the clock is meant to stand still there.</summary>
	[Fact]
	public async Task Heartbeat_IsSilentWhileTheClockIsStopped() {
		var engine = new ScanEngine();
		engine.ElapsedTimer.Start();
		engine.InitProgress(10);
		engine.IncrementProgress("paused.mp4");

		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();
		engine.Progress += (_, e) => events.Enqueue(e);

		engine.ElapsedTimer.Stop();
		await Task.Delay(TimeSpan.FromMilliseconds(400));
		engine.EmitProgressHeartbeat();

		Assert.Empty(events);
	}

	/// <summary>
	/// A phase opens by publishing its own zeroed counters, so a slow first item can't leave the
	/// previous phase's finished-looking numbers on screen.
	/// </summary>
	[Fact]
	public void InitProgress_ImmediatelyPublishesTheNewPhasesEmptyBar() {
		var engine = new ScanEngine();
		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();
		engine.Progress += (_, e) => events.Enqueue(e);

		engine.InitProgress(42);

		var opening = Assert.Single(events);
		Assert.Equal(0, opening.CurrentPosition);
		Assert.Equal(42, opening.MaxPosition);
		Assert.Equal(TimeSpan.Zero, opening.Remaining);
	}
}
