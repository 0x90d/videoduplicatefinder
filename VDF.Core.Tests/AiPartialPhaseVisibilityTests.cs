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
// Regression cover for issue #865. The reporter's scan "hung in verifying partial clips":
// that phase had in fact finished and the AI partial pass had taken over, but its first
// progress event only came after the eligibility scan and the keyframe sidecar load - both
// silent minutes on a large library - so the gate's label and its completed counters stayed
// on screen the whole time. Same lesson as #831: a phase that works must say so.

using System.Collections.Concurrent;
using VDF.Core.Utils;

namespace VDF.Core.Tests;

[Collection("DatabaseUtils")] // ScanForPartialDuplicatesVisual reads the shared static database
public class AiPartialPhaseVisibilityTests : IDisposable {

	readonly List<FileEntry> added = new();

	public void Dispose() {
		foreach (var e in added)
			DatabaseUtils.Database.Remove(e);
	}

	FileEntry Add(string name, double durationSeconds) {
		var entry = new FileEntry {
			_Path = @"C:\vdf-ai-partial-" + Guid.NewGuid().ToString("N") + "\\" + name,
			FileSize = 1,
			invalid = false,
			IsImage = false,
			mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(durationSeconds) },
		};
		DatabaseUtils.Database.Add(entry);
		added.Add(entry);
		return entry;
	}

	/// <summary>
	/// The pass must claim the status bar before its prep work, not after. Only one eligible
	/// video, so it returns straight after the eligibility scan - no ONNX model, no ffmpeg -
	/// which is exactly the stretch that used to run under the previous phase's label.
	/// </summary>
	[Fact]
	public void AiPartialPass_AnnouncesItsOwnStageBeforeAnyPrepWork() {
		var engine = new ScanEngine();
		engine.ElapsedTimer.Start();
		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();

		// Stand in for the visual gate that ran just before: it ends full over its own maximum.
		engine.InitProgress(12);
		for (int i = 0; i < 12; i++)
			engine.IncrementProgress("clip.mp4");
		engine.Progress += (_, e) => events.Enqueue(e);

		Add("only-one.mp4", 120);
		engine.ScanForPartialDuplicatesVisual();

		var first = events.First();
		Assert.Equal("AI partial: preparing", first.CurrentStage);
		// A fresh, empty bar - not the finished 12/12 of the phase before it.
		Assert.Equal(0, first.CurrentPosition);
		Assert.Equal(1, first.MaxPosition);
	}

	/// <summary>
	/// The label must not leak backwards either: nothing the AI pass emits may still carry
	/// the visual gate's stage, which is what the reporter saw frozen on screen.
	/// </summary>
	[Fact]
	public void AiPartialPass_NeverReportsUnderThePreviousPhasesLabel() {
		var engine = new ScanEngine();
		engine.ElapsedTimer.Start();
		var events = new ConcurrentQueue<ScanProgressChangedEventArgs>();

		// The gate takes its videos as a list, so these stay out of the database - only the
		// single entry added below is eligible for the AI pass, which keeps it out of ONNX.
		engine.RunPartialClipVisualGate(
			new List<FileEntry> {
				new() { _Path = @"C:\vdf-gate\source.mp4" },
				new() { _Path = @"C:\vdf-gate\clip.mp4" },
			},
			new List<(int, int, float, int, Guid)> { (0, 1, 0.9f, 0, Guid.NewGuid()) },
			(_, _, _) => (true, 1f));
		Add("only-one.mp4", 120);

		engine.Progress += (_, e) => events.Enqueue(e);
		engine.ScanForPartialDuplicatesVisual();

		Assert.NotEmpty(events);
		Assert.DoesNotContain(events, e => e.CurrentStage == "verifying partial clips");
	}
}
