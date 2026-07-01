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

using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using MemoryPack;
using VDF.Core.Utils;

namespace VDF.Core {

	[MemoryPackable(GenerateType.VersionTolerant)]
	[DebuggerDisplay("{" + nameof(_Path) + ",nq}")]
	public partial class FileEntry {
#pragma warning disable CS8618 // Non-nullable field is uninitialized.
		[MemoryPackConstructor]
		public FileEntry() { }
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
		public FileEntry(string file) : this(new FileInfo(file)) { }
		public FileEntry(FileInfo fileInfo) {
			_Path = fileInfo.FullName;
			Folder = fileInfo.Directory?.FullName ?? string.Empty;
			var extension = fileInfo.Extension;
			IsImage = FileUtils.ImageExtensions.Any(x => extension.EndsWith(x, StringComparison.OrdinalIgnoreCase));			
			DateCreated = fileInfo.CreationTimeUtc;
			DateModified = fileInfo.LastWriteTimeUtc;
			FileSize = fileInfo.Length;
			// Attributes are already populated on enumerated FileInfos, so stamping the
			// reparse flag here is free and spares the scan a per-file syscall later.
			FileAttributes attributes = fileInfo.Attributes;
			if (attributes != (FileAttributes)(-1)) {
				Flags.Set(EntryFlags.ReparsePoint, (attributes & FileAttributes.ReparsePoint) != 0);
				Flags.Set(EntryFlags.ReparsePointChecked);
			}
		}

		[MemoryPackInclude, MemoryPackOrder(0)]
		internal string _Path;
		[MemoryPackIgnore]
		public string Path {
			get => _Path;
			set {
				FileInfo fileInfo = new(value);
				_Path = fileInfo.FullName;
				Folder = fileInfo.Directory?.FullName ?? string.Empty;
			}
		 }
		[MemoryPackOrder(1)]
		public string Folder;
		[MemoryPackOrder(2)]
		public Dictionary<double, byte[]?> grayBytes = new();
		[MemoryPackOrder(3)]
		public MediaInfo? mediaInfo;
		[MemoryPackOrder(4)]
		public EntryFlags Flags;
		[MemoryPackOrder(5)]
		public DateTime DateCreated;
		[MemoryPackOrder(6)]
		public DateTime DateModified;
		[MemoryPackOrder(7)]
		public long FileSize;
		[MemoryPackOrder(8)]
		public Dictionary<double, ulong?> PHashes = new();
		/// <summary>
		/// Aggregated audio fingerprint: one <c>uint</c> per second of audio.
		/// <c>null</c> = not yet extracted; empty array = file has no audio track.
		/// </summary>
		[MemoryPackOrder(9)]
		public uint[]? AudioFingerprint;
		/// <summary>
		/// OpenSubtitles-style content fingerprint (size + head/tail 64 KiB checksum), cached so a
		/// later scan can recognise a MOVED file (same OsHash, old path now gone) and relink it —
		/// reusing its thumbnails/mediaInfo instead of re-decoding. <c>null</c> = not yet computed
		/// (or the file was too small / unreadable when last seen).
		/// </summary>
		[MemoryPackOrder(10)]
		public string? OsHash;

		[MemoryPackIgnore]
		internal bool invalid = true;

		// Transient compare-phase snapshot, built by ScanEngine.ScanForDuplicates and
		// cleared when the phase ends. compareGray holds the gray-byte arrays aligned
		// with the scan's thumbnail position order, comparePHash the first-position
		// pHash, and compareIndex the entry's position in the scan list. The per-pair
		// hot path reads these instead of probing Dictionary<double,...> with computed
		// keys and hashing the full path string for every candidate pair.
		[MemoryPackIgnore]
		internal byte[]?[]? compareGray;
		[MemoryPackIgnore]
		internal ulong? comparePHash;
		[MemoryPackIgnore]
		internal int compareIndex;

		[MemoryPackIgnore]
		internal bool IsImage {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Flags.Has(EntryFlags.IsImage);
			set => Flags.Set(EntryFlags.IsImage, value);
		}
		[MemoryPackIgnore]
		public bool IsManuallyExcluded {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Flags.Has(EntryFlags.ManuallyExcluded);
			protected set => Flags.Set(EntryFlags.ManuallyExcluded, value);
		}
		[MemoryPackIgnore]
		public bool HasMetadataError {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Flags.Has(EntryFlags.MetadataError);
			protected set => Flags.Set(EntryFlags.MetadataError, value);
		}
		[MemoryPackIgnore]
		public bool HasThubmanilError {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Flags.Has(EntryFlags.ThumbnailError);
			protected set => Flags.Set(EntryFlags.ThumbnailError, value);
		}
		[MemoryPackIgnore]
		public bool IsTooDark {
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => Flags.Has(EntryFlags.TooDark);
			protected set => Flags.Set(EntryFlags.TooDark, value);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public double GetGrayBytesIndex(float position) => mediaInfo!.Duration.TotalSeconds * position;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public double GetGrayBytesIndex(float position, double? maxSamplingDurationSeconds) {
			double durationSeconds = mediaInfo!.Duration.TotalSeconds;
			if (maxSamplingDurationSeconds.HasValue && maxSamplingDurationSeconds.Value > 0d && durationSeconds > maxSamplingDurationSeconds.Value)
				durationSeconds = maxSamplingDurationSeconds.Value;
			return durationSeconds * position;
		}
		public override bool Equals(object? obj) =>
			obj is FileEntry entry &&
			Path.Equals(entry.Path, CoreUtils.IsWindows ?
				StringComparison.OrdinalIgnoreCase :
				StringComparison.Ordinal);

		public override int GetHashCode() => CoreUtils.IsWindows ?
			StringComparer.OrdinalIgnoreCase.GetHashCode(Path) :
			HashCode.Combine(Path);
	}
}
