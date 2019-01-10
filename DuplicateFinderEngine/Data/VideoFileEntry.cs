using System;
using DuplicateFinderEngine.FFProbeWrapper;
using System.Collections.Generic;
using ProtoBuf;

namespace DuplicateFinderEngine.Data
{
    [ProtoContract]
    public class VideoFileEntry {
	    protected VideoFileEntry() {}
        public VideoFileEntry(string file)
        {
            Path = file;
            var fi = new System.IO.FileInfo(file);
			Folder = fi.Directory?.FullName;
            var extension = fi.Extension;
            IsImage = FileHelper.ImageExtensions.Find(a => a.Equals(extension, StringComparison.OrdinalIgnoreCase)) != null;
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
		public readonly bool IsImage;
    }
}
