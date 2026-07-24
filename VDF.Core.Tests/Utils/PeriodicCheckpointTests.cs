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
// Cover for the checkpoint gate the AI keyframe cache uses (#865): before it, hours of
// dense sampling were persisted only after the last file, so any kill lost all of it.

using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class PeriodicCheckpointTests {

	sealed class FakeClock {
		internal DateTime Now = new(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);
		internal DateTime Read() => Now;
		internal void Advance(TimeSpan by) => Now += by;
	}

	[Fact]
	public void DoesNotRunBeforeTheIntervalElapses() {
		var clock = new FakeClock();
		var checkpoint = new PeriodicCheckpoint(TimeSpan.FromMinutes(5), clock.Read);
		int runs = 0;

		clock.Advance(TimeSpan.FromMinutes(4));
		Assert.False(checkpoint.TryRun(() => runs++));
		Assert.Equal(0, runs);
	}

	[Fact]
	public void RunsOncePerInterval() {
		var clock = new FakeClock();
		var checkpoint = new PeriodicCheckpoint(TimeSpan.FromMinutes(5), clock.Read);
		int runs = 0;

		clock.Advance(TimeSpan.FromMinutes(5));
		Assert.True(checkpoint.TryRun(() => runs++));
		// A second worker arriving right after must not save again.
		Assert.False(checkpoint.TryRun(() => runs++));
		clock.Advance(TimeSpan.FromMinutes(5));
		Assert.True(checkpoint.TryRun(() => runs++));

		Assert.Equal(2, runs);
	}

	/// <summary>
	/// The clock restarts when the save finishes: a write that itself outlasts the interval
	/// must not re-trigger the moment it returns (the sidecar can be gigabytes).
	/// </summary>
	[Fact]
	public void SlowSaveDoesNotImmediatelyRetrigger() {
		var clock = new FakeClock();
		var checkpoint = new PeriodicCheckpoint(TimeSpan.FromMinutes(5), clock.Read);
		int runs = 0;

		clock.Advance(TimeSpan.FromMinutes(5));
		Assert.True(checkpoint.TryRun(() => {
			runs++;
			clock.Advance(TimeSpan.FromMinutes(30)); // the save itself took half an hour
		}));
		Assert.False(checkpoint.TryRun(() => runs++));
		Assert.Equal(1, runs);
	}

	[Fact]
	public void ZeroOrNegativeIntervalDisablesCheckpointing() {
		var clock = new FakeClock();
		foreach (var interval in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(-1) }) {
			var checkpoint = new PeriodicCheckpoint(interval, clock.Read);
			clock.Advance(TimeSpan.FromHours(10));
			Assert.False(checkpoint.TryRun(() => Assert.Fail("checkpointing is off")));
		}
	}

	/// <summary>
	/// Workers must never queue behind a save in flight - that would stall the whole phase
	/// on a multi-gigabyte write instead of costing one worker.
	/// </summary>
	[Fact]
	public void ConcurrentWorkersSkipInsteadOfQueueing() {
		var checkpoint = new PeriodicCheckpoint(TimeSpan.FromMilliseconds(1));
		using var inSave = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		int runs = 0, skipped = 0;
		Thread.Sleep(5); // let the interval elapse

		var saver = Task.Run(() => checkpoint.TryRun(() => {
			Interlocked.Increment(ref runs);
			inSave.Set();
			release.Wait();
		}));
		Assert.True(inSave.Wait(5_000), "the first checkpoint never started");

		var others = Enumerable.Range(0, 4).Select(_ => Task.Run(() => {
			if (!checkpoint.TryRun(() => Interlocked.Increment(ref runs)))
				Interlocked.Increment(ref skipped);
		})).ToArray();
		Assert.True(Task.WaitAll(others, 5_000), "a worker blocked behind the save in flight");

		release.Set();
		saver.Wait(5_000);
		Assert.Equal(1, runs);
		Assert.Equal(4, skipped);
	}
}
