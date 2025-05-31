using System; // Required for HashCode

namespace VDF.Core {
    public class ThumbnailPositionSetting {
        public enum PositionType { Percentage, OffsetFromStart, OffsetFromEnd }
        public PositionType Type { get; set; } = PositionType.Percentage;
        public double Value { get; set; } // Percentage (e.g., 50 for 50%) or Offset in seconds.

        // Default constructor is fine, properties can be set via initializers.
        // Optional: Add a parameterized constructor for convenience.
        public ThumbnailPositionSetting() {}

        public ThumbnailPositionSetting(PositionType type, double value) {
            Type = type;
            Value = value;
        }

        // Override Equals and GetHashCode if these objects are stored in collections
        // where value-based equality is important (e.g., HashSet, or for Distinct()).
        // For a List serialized to JSON and then compared, this might not be strictly necessary
        // unless direct comparison of ThumbnailPositionSetting objects is done.
        public override bool Equals(object? obj) {
            if (obj is ThumbnailPositionSetting other) {
                return Type == other.Type && Value == other.Value;
            }
            return false;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Type, Value);
        }
    }
}
