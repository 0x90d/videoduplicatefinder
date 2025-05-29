using System;
using System.Collections.Generic;

namespace VDF.Core {
    public class SegmentComparisonResult {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } // For errors or summary
        public float SimilarityScore { get; set; } // For DirectSequenceMatch
        public List<TimeSpan> MatchStartTimesInB { get; set; } // For SearchASinB

        public SegmentComparisonResult() {
            MatchStartTimesInB = new List<TimeSpan>();
            Message = string.Empty; // Initialize message to avoid null
        }
    }
}
