namespace VDF.Core {
    public class ComparisonParameters {
        public int NumberOfThumbnails { get; set; } = 5; // Default value
        public enum ComparisonMethod { DirectSequenceMatch, SearchASinB }
        public ComparisonMethod Method { get; set; }
    }
}
