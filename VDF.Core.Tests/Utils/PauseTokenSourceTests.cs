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

using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class PauseTokenSourceTests {

	[Fact]
	public void TryWait_NotPaused_ReturnsTrueImmediately() {
		var pts = new PauseTokenSource();
		Assert.True(pts.TryWaitWhilePaused(CancellationToken.None));
	}

	// Regression for Stop-while-paused: workers parked at the pause gate must unwind
	// WITHOUT an exception when the scan is canceled (the old throwing wait broke into
	// the debugger as "user-unhandled" on every Stop pressed during a pause).
	[Fact]
	public void TryWait_CanceledWhilePaused_ReturnsFalseInsteadOfThrowing() {
		var pts = new PauseTokenSource { IsPaused = true };
		using var cts = new CancellationTokenSource();

		var worker = Task.Run(() => pts.TryWaitWhilePaused(cts.Token));
		Assert.False(worker.Wait(150)); // parked at the gate

		cts.Cancel();
		Assert.True(worker.Wait(TimeSpan.FromSeconds(10)), "worker did not unwind after cancel");
		Assert.False(worker.Result);
	}

	[Fact]
	public void TryWait_ResumedWhilePaused_ReturnsTrue() {
		var pts = new PauseTokenSource { IsPaused = true };
		var worker = Task.Run(() => pts.TryWaitWhilePaused(CancellationToken.None));
		Assert.False(worker.Wait(150));

		pts.IsPaused = false;
		Assert.True(worker.Wait(TimeSpan.FromSeconds(10)));
		Assert.True(worker.Result);
	}

	[Fact]
	public void TryWait_AlreadyCanceled_ReturnsFalseEvenWhenNotPaused() {
		var pts = new PauseTokenSource();
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		// ManualResetEventSlim.Wait checks the token before the (set) event.
		Assert.False(pts.TryWaitWhilePaused(cts.Token));
	}
}
