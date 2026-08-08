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

using System.Runtime.CompilerServices;
using VDF.Core.FFTools;

namespace VDF.Core.Tests.FFTools;

// Regression cover for the #865 follow-up: a timed-out stdout copy that outlived
// KillAndDrain's bounded drain window faulted later (broken pipe, or the caller's
// MemoryStream disposed under it) and, unawaited, resurfaced on the finalizer thread
// as an UnobservedTaskException - 105k identical log entries in one reported scan.
// ObserveAbandonedRead attaches an exception observer to keep the abandonment silent.
public class AbandonedReadObservationTests {

	[MethodImpl(MethodImplOptions.NoInlining)] // the task must be collectible after return
	static void CreateAbandonedFaultingTask(string marker, bool observe) {
		using var gate = new ManualResetEventSlim();
		var task = Task.Run(() => {
			gate.Wait(5000);
			throw new ObjectDisposedException(marker);
		});
		if (observe)
			FFToolsUtils.ObserveAbandonedRead(task);
		gate.Set();
		// Poll instead of Wait(): waiting would observe the fault and defeat the test.
		while (!task.IsCompleted)
			Thread.Sleep(1);
	}

	static bool AbandonedFaultSurfaces(bool observe) {
		string marker = "vdf-abandoned-read-" + Guid.NewGuid().ToString("N");
		bool surfaced = false;
		EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, e) => {
			if (e.Exception?.InnerExceptions.Any(x => x is ObjectDisposedException ode && ode.ObjectName == marker) == true) {
				surfaced = true;
				e.SetObserved(); // keep the probe from polluting anything else
			}
		};
		TaskScheduler.UnobservedTaskException += handler;
		try {
			CreateAbandonedFaultingTask(marker, observe);
			for (int i = 0; i < 5 && !surfaced; i++) {
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}
		finally {
			TaskScheduler.UnobservedTaskException -= handler;
		}
		return surfaced;
	}

	/// <summary>
	/// Control: without the observer the fault must surface - otherwise the assertion
	/// in the test below would pass vacuously on a harness that cannot detect faults.
	/// </summary>
	[Fact]
	public void Control_AnUnobservedAbandonedFaultSurfacesOnFinalization() {
		Assert.True(AbandonedFaultSurfaces(observe: false),
			"the probe task's unobserved fault never surfaced - this harness cannot verify the fix");
	}

	[Fact]
	public void ObserveAbandonedRead_KeepsTheAbandonedFaultSilent() {
		Assert.False(AbandonedFaultSurfaces(observe: true),
			"the abandoned read's fault still surfaced as an UnobservedTaskException");
	}
}
