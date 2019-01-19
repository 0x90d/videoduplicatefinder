using System;
using System.Linq;
using DuplicateFinderEngine.FFProbeWrapper;
using System.Collections.Generic;
using ProtoBuf;

namespace DuplicateFinderEngine.Data {
	[Flags]
	public enum EntryFlags {
		IsImage = 1,
		ManuallyExcluded = 2,
		ThumbnailError = 4,
		MetadataError = 8,
		TooDark = 16,

		AllErrors = ThumbnailError | MetadataError | TooDark
	}

	public static class EntryFlagExtensions {
		public static bool Any(this EntryFlags f, EntryFlags checkFlags) => (f & checkFlags) > 0;
		public static bool Has(this EntryFlags f, EntryFlags checkFlags) => (f & checkFlags) == checkFlags;
		public static void Set(this ref EntryFlags f, EntryFlags setFlag) => f = f | setFlag;
		public static void Set(this ref EntryFlags f, EntryFlags setFlag, bool falseToReset) => f = (f & ~setFlag) | (falseToReset ? setFlag : 0);
	}


	[ProtoContract]
	public class VideoFileEntry {
		protected VideoFileEntry() { }
		public VideoFileEntry(string file) {
			Path = file;
			var fi = new System.IO.FileInfo(file);
			Folder = fi.Directory?.FullName;
			var extension = fi.Extension;
			IsImage = FileHelper.ImageExtensions.Any(x => extension.EndsWith(x, StringComparison.OrdinalIgnoreCase));
		}
		[ProtoMember(1)]
		public string Path;
		[ProtoMember(2)]
		public string Folder;
		[ProtoMember(3)]
		public List<byte[]> grayBytes;
		[ProtoMember(4)]
		public MediaInfo mediaInfo;
		[ProtoMember(5)]
		public EntryFlags Flags;

		public bool IsImage {
			get => Flags.Has(EntryFlags.IsImage);
			protected set => Flags.Set(EntryFlags.IsImage, value);
		}
	}
}
