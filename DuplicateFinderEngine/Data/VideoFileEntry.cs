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
            Folder = new System.IO.FileInfo(file).Directory?.FullName;
        }
        public string Path;
        public string Folder;
        public List<byte[]> grayBytes;
        public MediaInfo mediaInfo;

    }
}
