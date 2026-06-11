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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MemoryPack;

namespace VDF.Core {
	[MemoryPackable(GenerateType.VersionTolerant)]
	public partial class DatabaseWrapper {
		[MemoryPackOrder(0)]
		public int Version { get; set; } = 3;

		[MemoryPackOrder(1)]
		public HashSet<FileEntry> Entries { get; set; } = new();

		/// <summary>
		/// Pipeline used to hash still images. 0 = legacy (ImageSharp: BT.709 luma,
		/// ImageSharp bicubic), 1 = FFmpeg (BT.601 luma, swscale bicubic — identical to
		/// the video pipeline). Databases loaded with a value of 0 get their image
		/// hashes cleared once so they are recomputed with the current pipeline;
		/// mixing the two would produce false non-matches between old and new entries.
		/// </summary>
		[MemoryPackOrder(2)]
		public int ImageHashPipeline { get; set; }
	}
}
