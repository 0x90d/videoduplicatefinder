// /*
//     Copyright (C) 2021 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GPLv3 as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//     You should have received a copy of the GNU General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using ProtoBuf;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using VDF.Core.Utils;

namespace VDF.Core {
	
	[ProtoContract]
	[DebuggerDisplay("{" + nameof(Path) + ",nq}")]
	public class FileEntry {
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
		protected FileEntry() { }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
		public FileEntry(string file) {
			Path = file;
			var fi = new FileInfo(file);
			Folder = fi.Directory?.FullName ?? string.Empty;
			var extension = fi.Extension;
			IsImage = FileUtils.ImageExtensions.Any(x => extension.EndsWith(x, StringComparison.OrdinalIgnoreCase));
			grayBytes = new Dictionary<double, byte[]?>();
			DateCreated = fi.CreationTimeUtc;
			DateModified = fi.LastWriteTimeUtc;
			FileSize = fi.Length;
		}
		[ProtoMember(1)]
		public string Path { get; set; }
		[ProtoMember(2)]
		public string Folder;
		[ProtoMember(3)]
		public Dictionary<double, byte[]?> grayBytes;
		[ProtoMember(4)]
		public MediaInfo? mediaInfo;
		[ProtoMember(5)]
		public EntryFlags Flags;
		[ProtoMember(6)]
		public DateTime DateCreated;
		[ProtoMember(7)]
		public DateTime DateModified;
		[ProtoMember(8)]
		public long FileSize;

		[ProtoIgnore]
		public bool IsImage {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Flags.Has(EntryFlags.IsImage);
			protected set => Flags.Set(EntryFlags.IsImage, value);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public double GetGrayBytesIndex(float position) => mediaInfo!.Duration.TotalSeconds * position;

		public override bool Equals(object? obj) =>
			obj is FileEntry entry &&
			Path == entry.Path;

		public override int GetHashCode() => HashCode.Combine(Path);
	}
}
