using System;
using DuplicateFinderEngine.FFProbeWrapper;
using System.Collections.Generic;

namespace DuplicateFinderEngine.Data
{
    [Serializable]
    public class VideoFileEntry
    {
        public VideoFileEntry(string file)
        {
            Path = file;
            var fi = new System.IO.FileInfo(file);
			Folder = fi.Directory?.FullName;
            var extension = fi.Extension;
            IsImage = FileHelper.ImageExtensions.Find(a => a.Equals(extension, StringComparison.OrdinalIgnoreCase)) != null;
        }
        public string Path;
        public string Folder;
        public List<byte[]> grayBytes;
        public MediaInfo mediaInfo;
        public readonly bool IsImage;
    }
}
