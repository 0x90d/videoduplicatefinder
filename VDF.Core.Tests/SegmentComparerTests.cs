using Microsoft.VisualStudio.TestTools.UnitTesting;
using VDF.Core;
using VDF.Core.FFTools; // For FfmpegEngine, FFProbeEngine if used directly (not in test method)
using VDF.Core.ViewModels; // For MediaInfo
using System;
using System.Collections.Generic;
using System.Linq;

namespace VDF.Core.Tests
{
    [TestClass]
    public class SegmentComparerTests
    {
        private SegmentComparer _comparer;
        private Settings _currentSettings;

        // Sample Thumbnail Data
        private readonly byte[] T1 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }; // 16 bytes
        private readonly byte[] T2 = new byte[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
        private readonly byte[] T3 = new byte[] { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        private readonly byte[] T4 = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160 }; // Different
        private readonly byte[] T1_Variant = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 200 }; // Slightly different (1 byte off)

        [TestInitialize]
        public void TestInitialize()
        {
            _comparer = new SegmentComparer();
            _currentSettings = new Settings
            {
                Percent = 95f, // Default for most tests
                ExtendedFFToolsLogging = false,
                IgnoreBlackPixels = false,
                IgnoreWhitePixels = false
            };
        }

        private MediaInfo CreateDummyMediaInfo(int durationSeconds)
        {
            return new MediaInfo { Duration = TimeSpan.FromSeconds(durationSeconds) };
        }

        private Dictionary<double, byte[]?> CreateThumbnailDict(params byte[][] thumbnails)
        {
            var dict = new Dictionary<double, byte[]?>();
            for (int i = 0; i < thumbnails.Length; i++)
            {
                dict.Add(i * 0.1, thumbnails[i]); // Key represents time/order
            }
            return dict;
        }

        // --- DirectSequenceMatch Tests ---

        [TestMethod]
        public void CompareSegmentsForTest_DirectMatch_PerfectMatch_ReturnsSuccessAndScore1()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(10) };
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(10) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 2, Method = ComparisonParameters.ComparisonMethod.DirectSequenceMatch };
            
            var thumbnailsA = CreateThumbnailDict(T1, T2);
            var thumbnailsB = CreateThumbnailDict(T1, T2);
            var mediaInfoA = CreateDummyMediaInfo(20);
            var mediaInfoB = CreateDummyMediaInfo(20);

            // Assuming SegmentComparer has an internal method for testing:
            // internal SegmentComparisonResult CompareSegmentsForTest(SegmentDefinition segADef, SegmentDefinition segBDef, ComparisonParameters compParams, Settings currentSettings, Dictionary<double, byte[]?> thumbnailsA, Dictionary<double, byte[]?> thumbnailsB, MediaInfo mediaInfoA, MediaInfo mediaInfoB)
            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1.0f, result.SimilarityScore, 0.001f);
            Assert.IsTrue(result.Message.Contains("Similarity: 100.00%"));
        }

        [TestMethod]
        public void CompareSegmentsForTest_DirectMatch_NoMatch_ReturnsSuccessAndScore0()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(10) };
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(10) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 2, Method = ComparisonParameters.ComparisonMethod.DirectSequenceMatch };
            
            var thumbnailsA = CreateThumbnailDict(T1, T2);
            var thumbnailsB = CreateThumbnailDict(T3, T4); // Completely different
            var mediaInfoA = CreateDummyMediaInfo(20);
            var mediaInfoB = CreateDummyMediaInfo(20);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);
            
            Assert.IsTrue(result.IsSuccess); // Comparison itself succeeds
            Assert.IsTrue(result.SimilarityScore < 0.5f); // Expect low similarity
        }

        [TestMethod]
        public void CompareSegmentsForTest_DirectMatch_PartialSimilarity_ReturnsCorrectScore()
        {
            _currentSettings.Percent = 50f; // Lower threshold for this test
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(10) };
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(10) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 2, Method = ComparisonParameters.ComparisonMethod.DirectSequenceMatch };
            
            var thumbnailsA = CreateThumbnailDict(T1, T2);
            var thumbnailsB = CreateThumbnailDict(T1, T1_Variant); // One match, one partial
            var mediaInfoA = CreateDummyMediaInfo(20);
            var mediaInfoB = CreateDummyMediaInfo(20);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);
            
            Assert.IsTrue(result.IsSuccess);
            // Exact score depends on GrayBytesUtils.PercentageDifference. T1 vs T1 is 1.0. T2 vs T1_Variant will be less.
            // For T1_Variant (1 byte diff in 16 bytes): 1 - ( (200-16) / (16*255) ) = 1 - (184 / 4080) = 1 - 0.045 = 0.955
            // Average: (1.0 + 0.955) / 2 = 0.9775
            Assert.AreEqual(0.97794f, result.SimilarityScore, 0.001f); 
        }

        [TestMethod]
        public void CompareSegmentsForTest_DirectMatch_NotEnoughThumbnailsA_ReturnsFail()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(10) };
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(10) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 2, Method = ComparisonParameters.ComparisonMethod.DirectSequenceMatch };
            
            var thumbnailsA = CreateThumbnailDict(T1); // Only 1 thumbnail
            var thumbnailsB = CreateThumbnailDict(T1, T2);
            var mediaInfoA = CreateDummyMediaInfo(20);
            var mediaInfoB = CreateDummyMediaInfo(20);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);
            
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Message.Contains("requires exactly 2 thumbnails"));
        }

        // --- SearchASinB Tests ---
        [TestMethod]
        public void CompareSegmentsForTest_SearchASinB_MatchAtStart()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(5) }; // Shorter segment A
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(20) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 2, Method = ComparisonParameters.ComparisonMethod.SearchASinB };
            
            var thumbnailsA = CreateThumbnailDict(T1, T2);
            var thumbnailsB = CreateThumbnailDict(T1, T2, T3, T4); // B is longer
            var mediaInfoA = CreateDummyMediaInfo(10);
            var mediaInfoB = CreateDummyMediaInfo(30);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, result.MatchStartTimesInB.Count);
            Assert.AreEqual(TimeSpan.FromSeconds(0 * 0.1), result.MatchStartTimesInB[0]); // Key of first thumbnail in B
        }
        
        [TestMethod]
        public void CompareSegmentsForTest_SearchASinB_MatchInMiddle()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(5) };
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(20) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 2, Method = ComparisonParameters.ComparisonMethod.SearchASinB };
            
            var thumbnailsA = CreateThumbnailDict(T2, T3);
            var thumbnailsB = CreateThumbnailDict(T1, T2, T3, T4);
            var mediaInfoA = CreateDummyMediaInfo(10);
            var mediaInfoB = CreateDummyMediaInfo(30);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1, result.MatchStartTimesInB.Count);
            Assert.AreEqual(TimeSpan.FromSeconds(1 * 0.1), result.MatchStartTimesInB[0]); // Key of T2 in B
        }

        [TestMethod]
        public void CompareSegmentsForTest_SearchASinB_NoMatch()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(5) };
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(20) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 2, Method = ComparisonParameters.ComparisonMethod.SearchASinB };
            
            var thumbnailsA = CreateThumbnailDict(T1, T4); // No consecutive T1, T4 in B
            var thumbnailsB = CreateThumbnailDict(T1, T2, T3, T4);
            var mediaInfoA = CreateDummyMediaInfo(10);
            var mediaInfoB = CreateDummyMediaInfo(30);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(0, result.MatchStartTimesInB.Count);
        }
        
        [TestMethod]
        public void CompareSegmentsForTest_SearchASinB_MultipleMatches()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(5) };
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(30) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 1, Method = ComparisonParameters.ComparisonMethod.SearchASinB };
            
            var thumbnailsA = CreateThumbnailDict(T1);
            var thumbnailsB = CreateThumbnailDict(T1, T2, T1, T3, T1); // T1 appears 3 times
            var mediaInfoA = CreateDummyMediaInfo(10);
            var mediaInfoB = CreateDummyMediaInfo(40);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(3, result.MatchStartTimesInB.Count);
            CollectionAssert.Contains(result.MatchStartTimesInB, TimeSpan.FromSeconds(0 * 0.1));
            CollectionAssert.Contains(result.MatchStartTimesInB, TimeSpan.FromSeconds(2 * 0.1));
            CollectionAssert.Contains(result.MatchStartTimesInB, TimeSpan.FromSeconds(4 * 0.1));
        }

        [TestMethod]
        public void CompareSegmentsForTest_SearchASinB_AHasMoreThumbnailsThanB_Fails()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(20) };
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.Zero, AbsoluteEndTime = TimeSpan.FromSeconds(5) };
            var compParams = new ComparisonParameters { NumberOfThumbnails = 3, Method = ComparisonParameters.ComparisonMethod.SearchASinB };
            
            var thumbnailsA = CreateThumbnailDict(T1, T2, T3);
            var thumbnailsB = CreateThumbnailDict(T1, T2); // B is shorter
            var mediaInfoA = CreateDummyMediaInfo(30);
            var mediaInfoB = CreateDummyMediaInfo(10);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, thumbnailsA, thumbnailsB, mediaInfoA, mediaInfoB);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Message.Contains("Segment B must have at least as many thumbnails as Segment A"));
        }

        // --- Overall Error Handling Tests (using the testable method) ---
        // These primarily test the logic before thumbnail comparison

        [TestMethod]
        public void CompareSegmentsForTest_MediaInfoANull_ReturnsFail()
        {
            var segADef = new SegmentDefinition { VideoPath = "nonexistent_a.mp4" }; // Path doesn't matter for this test variant
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4" };
            var compParams = new ComparisonParameters();
            
            // For the testable method, we directly pass null MediaInfo
            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, 
                                                          CreateThumbnailDict(T1), CreateThumbnailDict(T1), 
                                                          null, CreateDummyMediaInfo(10));
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Message.Contains("Could not retrieve valid media information for Video A"));
        }

        [TestMethod]
        public void CompareSegmentsForTest_SegmentAInvalid_ReturnsFail()
        {
            var segADef = new SegmentDefinition { VideoPath = "a.mp4", Mode = SegmentDefinition.DefinitionMode.AbsoluteTime, AbsoluteStartTime = TimeSpan.FromSeconds(20), AbsoluteEndTime = TimeSpan.FromSeconds(10) }; // Invalid: Start > End
            var segBDef = new SegmentDefinition { VideoPath = "b.mp4" };
            var compParams = new ComparisonParameters();
            var mediaInfoA = CreateDummyMediaInfo(30);

            var result = _comparer.CompareSegmentsForTest(segADef, segBDef, compParams, _currentSettings, 
                                                          CreateThumbnailDict(T1), CreateThumbnailDict(T1), 
                                                          mediaInfoA, CreateDummyMediaInfo(10));
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Message.Contains("Invalid segment definition for Video A"));
        }
    }
}
