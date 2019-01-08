namespace DuplicateFinderEngine.FFmpegWrapper
{
    sealed class Media
    {
        public string Filename { get; set; }
        public string Format { get; set; }
        public byte[] Bytes { get; set; }
    }
}
