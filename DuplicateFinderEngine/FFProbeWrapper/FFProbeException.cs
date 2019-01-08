using System;

namespace DuplicateFinderEngine.FFProbeWrapper
{
    sealed class FFProbeException : Exception
    {
        public int ErrorCode { get; }

        public FFProbeException(int errCode, string message) : base(string.Format(Properties.Resources.FFProbeException, message, errCode))
        {
            ErrorCode = errCode;
        }
   }

}
