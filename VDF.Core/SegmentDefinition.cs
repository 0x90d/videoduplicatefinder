using System;

namespace VDF.Core {
    public class SegmentDefinition {
        public string VideoPath { get; set; }
        public enum DefinitionMode { AbsoluteTime, Offset }
        public DefinitionMode Mode { get; set; }

        // For AbsoluteTime Mode
        public TimeSpan AbsoluteStartTime { get; set; }
        public TimeSpan AbsoluteEndTime { get; set; }

        // For Offset Mode
        public enum OffsetReference { FromStart, FromEnd }
        public OffsetReference StartReference { get; set; }
        public TimeSpan StartOffset { get; set; }
        public OffsetReference EndReference { get; set; }
        public TimeSpan EndOffset { get; set; }

        public (bool isValid, TimeSpan calculatedStart, TimeSpan calculatedEnd) ValidateAndCalculateTimestamps(TimeSpan videoDuration) {
            TimeSpan calcStart;
            TimeSpan calcEnd;

            if (videoDuration <= TimeSpan.Zero) return (false, TimeSpan.Zero, TimeSpan.Zero); // Invalid duration

            if (Mode == DefinitionMode.AbsoluteTime) {
                calcStart = AbsoluteStartTime;
                calcEnd = AbsoluteEndTime;
            } else { // Offset Mode
                // Calculate Start Time
                if (StartReference == OffsetReference.FromStart) {
                    calcStart = StartOffset;
                } else { // FromEnd
                    calcStart = videoDuration - StartOffset;
                }

                // Calculate End Time
                if (EndReference == OffsetReference.FromStart) {
                    calcEnd = EndOffset;
                } else { // FromEnd
                    calcEnd = videoDuration - EndOffset;
                }
            }

            // Clamp times to video duration
            calcStart = TimeSpan.FromSeconds(Math.Max(0, Math.Min(calcStart.TotalSeconds, videoDuration.TotalSeconds)));
            calcEnd = TimeSpan.FromSeconds(Math.Max(0, Math.Min(calcEnd.TotalSeconds, videoDuration.TotalSeconds)));

            if (calcStart > calcEnd) {
                // Allow for calcStart == calcEnd for very short segments or single frame
                 return (false, calcStart, calcEnd); 
            }

            return (true, calcStart, calcEnd);
        }
    }
}
