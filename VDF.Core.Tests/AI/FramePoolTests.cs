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

using VDF.Core.AI;

namespace VDF.Core.Tests.AI;

// The frame pool exists to stop 150 KB AI frame buffers from churning the Large
// Object Heap (#878). Tests run against private instances - FramePool.Shared is
// process-global and other tests may be using it concurrently.
public class FramePoolTests {

	[Fact]
	public void RentedBuffers_AreExactFrameSize() {
		var pool = new FramePool();
		Assert.Equal(FramePool.FrameBytes, pool.Rent().Length);
	}

	[Fact]
	public void Return_ThenRent_RecyclesTheSameInstance() {
		var pool = new FramePool();
		byte[] buffer = pool.Rent();
		pool.Return(buffer);
		Assert.Equal(1, pool.PooledCount);
		Assert.Same(buffer, pool.Rent());
		Assert.Equal(0, pool.PooledCount);
	}

	[Fact]
	public void ForeignArrays_AreIgnoredInsteadOfPoisoningThePool() {
		// Some producers hand the pipeline arrays the pool never issued (CLI stdout
		// slices); returning those must be safe, and wrong sizes must never enter
		// the pool - a short array handed to a later renter would crash the fill.
		var pool = new FramePool();
		pool.Return(null);
		pool.Return(new byte[10]);
		pool.Return(new byte[FramePool.FrameBytes + 1]);
		Assert.Equal(0, pool.PooledCount);

		// An exact-size foreign array simply joins the pool.
		pool.Return(new byte[FramePool.FrameBytes]);
		Assert.Equal(1, pool.PooledCount);
	}

	[Fact]
	public void Cap_StopsPoolGrowth() {
		var pool = new FramePool(maxPooled: 2);
		pool.Return(new byte[FramePool.FrameBytes]);
		pool.Return(new byte[FramePool.FrameBytes]);
		pool.Return(new byte[FramePool.FrameBytes]);
		Assert.Equal(2, pool.PooledCount);
	}
}
