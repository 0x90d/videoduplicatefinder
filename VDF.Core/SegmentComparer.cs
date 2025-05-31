using System;
using System.Collections.Generic;
using System.Linq;
using VDF.Core.FFTools; // For FfmpegEngine, FFProbeEngine, MediaInfo
using VDF.Core.Utils;  // For GrayBytesUtils

namespace VDF.Core {
    public class SegmentComparer {
        public SegmentComparisonResult CompareSegments(
            SegmentDefinition segADef,
            SegmentDefinition segBDef,
            ComparisonParameters compParams,
            Settings currentSettings // To access Percent, IgnoreBlackPixels, etc.
        ) {
            var result = new SegmentComparisonResult(); // Initialize result for early exits

            // 1. Get MediaInfo and Validate Segments for Video A
            var mediaInfoA = FFProbeEngine.GetMediaInfo(segADef.VideoPath, currentSettings.ExtendedFFToolsLogging);
            if (mediaInfoA == null || mediaInfoA.Duration == TimeSpan.Zero) {
                result.IsSuccess = false;
                result.Message = $"Could not retrieve valid media information for Video A: {segADef.VideoPath}";
                return result;
            }
            var (isValidA, startA, endA) = segADef.ValidateAndCalculateTimestamps(mediaInfoA.Duration);
            if (!isValidA) {
                result.IsSuccess = false;
                result.Message = $"Invalid segment definition for Video A: {segADef.VideoPath}. Calculated start {startA}, end {endA}.";
                return result;
            }

            // 2. Get MediaInfo and Validate Segments for Video B
            var mediaInfoB = FFProbeEngine.GetMediaInfo(segBDef.VideoPath, currentSettings.ExtendedFFToolsLogging);
            if (mediaInfoB == null || mediaInfoB.Duration == TimeSpan.Zero) {
                result.IsSuccess = false;
                result.Message = $"Could not retrieve valid media information for Video B: {segBDef.VideoPath}";
                return result;
            }
            var (isValidB, startB, endB) = segBDef.ValidateAndCalculateTimestamps(mediaInfoB.Duration);
            if (!isValidB) {
                result.IsSuccess = false;
                result.Message = $"Invalid segment definition for Video B: {segBDef.VideoPath}. Calculated start {startB}, end {endB}.";
                return result;
            }

            // 3. Extract Thumbnails
            var thumbnailsDictA = FfmpegEngine.GetThumbnailsForSegment(segADef.VideoPath, startA, endA, compParams.NumberOfThumbnails, currentSettings.ExtendedFFToolsLogging);
            var thumbnailsDictB = FfmpegEngine.GetThumbnailsForSegment(segBDef.VideoPath, startB, endB, compParams.NumberOfThumbnails, currentSettings.ExtendedFFToolsLogging);

            // After fetching all data, call the internal method
            return CompareSegmentsForTest( // Corrected method name
                segADef, segBDef, compParams, currentSettings,
                mediaInfoA, mediaInfoB,
                (startA, endA), (startB, endB),
                thumbnailsDictA, thumbnailsDictB
            );
        }

        // New internal method containing the core comparison logic
        // Renamed to CompareSegmentsForTest to match the test file's expectation from previous turn.
        // If it should be CompareSegmentsLogic, that's a minor name change.
        internal SegmentComparisonResult CompareSegmentsForTest( // Name updated to match test expectations
            SegmentDefinition segADef,
            SegmentDefinition segBDef,
            ComparisonParameters compParams,
            Settings currentSettings,
            MediaInfo mediaInfoA,
            MediaInfo mediaInfoB,
            (TimeSpan calculatedStart, TimeSpan calculatedEnd) segmentTimestampsA, // Not directly used by current logic but good for context
            (TimeSpan calculatedStart, TimeSpan calculatedEnd) segmentTimestampsB, // Not directly used by current logic
            Dictionary<double, byte[]?> thumbnailsDictA,
            Dictionary<double, byte[]?> thumbnailsDictB
        ) {
            var result = new SegmentComparisonResult();

            var thumbnailsA = thumbnailsDictA.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).Where(tb => tb != null).ToList();
            var thumbnailsB = thumbnailsDictB.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).Where(tb => tb != null).ToList();
            var thumbnailBTimes = thumbnailsDictB.OrderBy(kvp => kvp.Key).Select(kvp => TimeSpan.FromSeconds(kvp.Key)).ToList();

            // Initial checks based on extracted thumbnail counts
            if (compParams.Method == ComparisonParameters.ComparisonMethod.DirectSequenceMatch) {
                if (thumbnailsA.Count != compParams.NumberOfThumbnails || thumbnailsB.Count != compParams.NumberOfThumbnails) {
                    result.IsSuccess = false;
                    result.Message = $"Direct sequence match requires {compParams.NumberOfThumbnails} thumbnails per segment. Got {thumbnailsA.Count} for A, {thumbnailsB.Count} for B. Some thumbnails might have failed extraction.";
                    return result;
                }
                 if (thumbnailsA.Count == 0) { //Handles NumberOfThumbnails = 0 case for DirectSequenceMatch
                    result.IsSuccess = false;
                    result.Message = "Direct sequence match requires at least one thumbnail; none were extracted or provided for Segment A.";
                    return result;
                }
            } else if (compParams.Method == ComparisonParameters.ComparisonMethod.SearchASinB) {
                 if (thumbnailsA.Count == 0 ) {
                    result.IsSuccess = false;
                    result.Message = $"For search, Segment A must have thumbnails. Got {thumbnailsA.Count} for A.";
                    return result;
                }
                if (thumbnailsB.Count < thumbnailsA.Count) {
                     result.IsSuccess = false;
                     result.Message = $"For search, Segment B must have at least as many thumbnails as Segment A. Got {thumbnailsA.Count} for A, {thumbnailsB.Count} for B.";
                     return result;
                }
            }

            float differenceLimit = 1.0f - currentSettings.Percent / 100f;

            if (compParams.Method == ComparisonParameters.ComparisonMethod.DirectSequenceMatch) {
                float totalDifference = 0;
                int comparablePairs = 0;
                // NumberOfThumbnails is used as the loop bound because we've already validated counts match
                for (int i = 0; i < compParams.NumberOfThumbnails; i++) {
                    byte[]? currentThumbnailA = thumbnailsA[i];
                    byte[]? currentThumbnailB = thumbnailsB[i];

                    if (currentThumbnailA != null && currentThumbnailB != null) {
                        float diff = GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(
                                        currentThumbnailA, currentThumbnailB,
                                        currentSettings.IgnoreBlackPixels, currentSettings.IgnoreWhitePixels);
                        totalDifference += diff;
                        comparablePairs++;
                    }
                    // If one is null, this pair is skipped. The comparablePairs count will be lower.
                }

                if (comparablePairs == 0) {
                    result.IsSuccess = false;
                    result.Message = "No comparable thumbnail pairs found for direct sequence match (all pairs had at least one null thumbnail).";
                    return result;
                }

                float averageDifference = totalDifference / comparablePairs;
                result.SimilarityScore = 1.0f - averageDifference;
                result.IsSuccess = true;
                result.Message = $"Comparison complete. Average Similarity: {result.SimilarityScore:P2}";

            } else if (compParams.Method == ComparisonParameters.ComparisonMethod.SearchASinB) {
                for (int i = 0; i <= thumbnailsB.Count - thumbnailsA.Count; i++) {
                    bool currentWindowMatch = true;
                    for (int j = 0; j < thumbnailsA.Count; j++) {
                        byte[]? currentSearchThumbnailA = thumbnailsA[j];
                        byte[]? currentSegmentThumbnailB = thumbnailsB[i + j];

                        if (currentSearchThumbnailA == null || currentSegmentThumbnailB == null) {
                            currentWindowMatch = false; // If any thumbnail in the sequence is null, this window can't match
                            break;
                        }

                        float diff = GrayBytesUtils.PercentageDifferenceWithoutSpecificPixels(
                                        currentSearchThumbnailA, currentSegmentThumbnailB,
                                        currentSettings.IgnoreBlackPixels, currentSettings.IgnoreWhitePixels);
                        if (diff > differenceLimit) {
                            currentWindowMatch = false;
                            break;
                        }
                    }
                    if (currentWindowMatch) {
                        result.MatchStartTimesInB.Add(thumbnailBTimes[i]);
                    }
                }
                result.IsSuccess = true;
                result.Message = $"Search complete. Found {result.MatchStartTimesInB.Count} potential match(es).";
            }

            return result;
        }
    }
}
