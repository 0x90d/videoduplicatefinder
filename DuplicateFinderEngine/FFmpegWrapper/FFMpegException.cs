using System;

namespace DuplicateFinderEngine.FFmpegWrapper
{
    sealed class FFMpegException : Exception
    {
        public int ErrorCode { get; }

        public FFMpegException(int errCode, string message) : base(string.Format(Properties.Resources.FFProbeException,message, errCode)) => ErrorCode = errCode;
    }
}
