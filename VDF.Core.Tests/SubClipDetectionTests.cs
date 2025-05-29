using Microsoft.VisualStudio.TestTools.UnitTesting;
using VDF.Core;
using VDF.Core.Utils; // For DatabaseUtils if used, though not directly for these tests
using VDF.Core.ViewModels; // For MediaInfo
using System;
using System.Collections.Generic;
using System.Linq;

namespace VDF.Core.Tests
{
    [TestClass]
    public class SubClipDetectionTests
    {
        private ScanEngine _scanEngine;
        private Settings _settings;

        [TestInitialize]
        public void TestInitialize()
        {
            _scanEngine = new ScanEngine();
            _settings = new Settings
            {
                ThumbnailCount = 2, // Default for many tests, can be overridden
                Percent = 95f,      // Default similarity
                IgnoreBlackPixels = false,
                IgnoreWhitePixels = false
            };
            // DatabaseUtils.Database isn't directly used by FindSubClipMatches, 
            // as it takes IEnumerable<FileEntry> directly.
            // So, no specific DatabaseUtils cleanup is needed here beyond what other tests might do.
        }

        private FileEntry CreateTestFileEntry(string path, int durationSeconds, Dictionary<double, byte[]?> grayBytes)
        {
            return new FileEntry(path)
            {
                mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(durationSeconds) },
                grayBytes = grayBytes,
                IsImage = false // Ensure it's treated as a video
            };
        }

        private Dictionary<double, byte[]?> CreateThumbnails(params byte[][] thumbnails)
        {
            var dict = new Dictionary<double, byte[]?>();
            for (int i = 0; i < thumbnails.Length; i++)
            {
                // Using simple incremental keys for ordered thumbnails, representing time positions
                dict.Add(i * 0.1, thumbnails[i]); 
            }
            return dict;
        }

        // Thumbnail Definitions
        private readonly byte[] T1 = new byte[] { 1, 1, 1, 1 };
        private readonly byte[] T2 = new byte[] { 2, 2, 2, 2 };
        private readonly byte[] T3 = new byte[] { 3, 3, 3, 3 };
        private readonly byte[] T4 = new byte[] { 4, 4, 4, 4 };
        private readonly byte[] T5 = new byte[] { 5, 5, 5, 5 }; // Different from T1-T4
        private readonly byte[] T6 = new byte[] { 6, 6, 6, 6 }; // Different from T1-T4

        // Slightly different thumbnail for similarity testing
        private readonly byte[] T2_Similar_90 = new byte[] { 2, 2, 2, 20 }; // Approx 90% similar to T2 if using simple diff

        [TestMethod]
        public void FindSubClipMatches_DirectMatch_FindsOne()
        {
            _settings.ThumbnailCount = 2; // Sub has 2 thumbnails
            var mainVideo = CreateTestFileEntry("main.mp4", 40, CreateThumbnails(T1, T2, T3, T4));
            var subClip = CreateTestFileEntry("sub.mp4", 20, CreateThumbnails(T2, T3));
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual(mainVideo, matches[0].MainVideo);
            Assert.AreEqual(subClip, matches[0].SubClipVideo);
            CollectionAssert.AreEqual(new List<double> { 0.1, 0.2 }, matches[0].MainVideoMatchStartTimes);
        }

        [TestMethod]
        public void FindSubClipMatches_NoMatch_FindsNone()
        {
            _settings.ThumbnailCount = 2;
            var mainVideo = CreateTestFileEntry("main.mp4", 40, CreateThumbnails(T1, T2, T3, T4));
            var subClip = CreateTestFileEntry("sub.mp4", 20, CreateThumbnails(T5, T6));
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);

            Assert.AreEqual(0, matches.Count);
        }

        [TestMethod]
        public void FindSubClipMatches_PartialMatch_No_FindsNone()
        {
            _settings.ThumbnailCount = 2;
            var mainVideo = CreateTestFileEntry("main.mp4", 40, CreateThumbnails(T1, T2, T3, T4));
            var subClip = CreateTestFileEntry("sub.mp4", 20, CreateThumbnails(T2, T5)); // T5 won't match T3
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);

            Assert.AreEqual(0, matches.Count);
        }

        [TestMethod]
        public void FindSubClipMatches_MatchAtStart_FindsOne()
        {
            _settings.ThumbnailCount = 2;
            var mainVideo = CreateTestFileEntry("main.mp4", 40, CreateThumbnails(T1, T2, T3, T4));
            var subClip = CreateTestFileEntry("sub_start.mp4", 20, CreateThumbnails(T1, T2));
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual(mainVideo, matches[0].MainVideo);
            Assert.AreEqual(subClip, matches[0].SubClipVideo);
            CollectionAssert.AreEqual(new List<double> { 0.0, 0.1 }, matches[0].MainVideoMatchStartTimes);
        }

        [TestMethod]
        public void FindSubClipMatches_MatchAtEnd_FindsOne()
        {
            _settings.ThumbnailCount = 2;
            var mainVideo = CreateTestFileEntry("main.mp4", 40, CreateThumbnails(T1, T2, T3, T4));
            var subClip = CreateTestFileEntry("sub_end.mp4", 20, CreateThumbnails(T3, T4));
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);

            Assert.AreEqual(1, matches.Count);
            Assert.AreEqual(mainVideo, matches[0].MainVideo);
            Assert.AreEqual(subClip, matches[0].SubClipVideo);
            CollectionAssert.AreEqual(new List<double> { 0.2, 0.3 }, matches[0].MainVideoMatchStartTimes);
        }
        
        [TestMethod]
        public void FindSubClipMatches_VaryingSimilarity_MatchFound()
        {
            _settings.ThumbnailCount = 2;
            _settings.Percent = 85f; // Lower similarity threshold to 85%
            // T2_Similar_90 is approx 90% similar to T2 (3/4 bytes same, 1/4 different by a large margin for test)
            // A more precise byte-level similarity calculation would be needed for exact % target.
            // GrayBytesUtils.PercentageDifference calculates 1 - (diff / (length * 255))
            // For T2 vs T2_Similar_90: diff = (20-2) = 18. length = 4. 1 - (18 / (4*255)) = 1 - (18/1020) = 1 - 0.0176 = 0.982 (98.2%)
            // This means T2_Similar_90 is 98.2% similar to T2, so it should match at 85% and 95% threshold.
            // Let's make T2_Similar_70 for testing 85% threshold specifically
            byte[] T2_Similar_70 = new byte[] { 2, 2, 50, 80 }; // diff = (50-2) + (80-2) = 48+78 = 126.  1 - (126 / 1020) = 1 - 0.123 = 0.877 (87.7%)
                                                        
            var mainVideo = CreateTestFileEntry("main.mp4", 30, CreateThumbnails(T1, T2_Similar_70, T3));
            var subClip = CreateTestFileEntry("sub.mp4", 20, CreateThumbnails(T1, T2)); // Sub has original T2
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);
            Assert.AreEqual(1, matches.Count, "Should match because T1 matches T1 (100%), and T2_Similar_70 (87.7%) should match T2 with 85% threshold.");
        }

        [TestMethod]
        public void FindSubClipMatches_VaryingSimilarity_NoMatchDueToThreshold()
        {
            _settings.ThumbnailCount = 2;
            _settings.Percent = 90f; // Set similarity threshold to 90%
            byte[] T2_Similar_70 = new byte[] { 2, 2, 50, 80 }; // 87.7% similar to T2

            var mainVideo = CreateTestFileEntry("main.mp4", 30, CreateThumbnails(T1, T2_Similar_70, T3));
            var subClip = CreateTestFileEntry("sub.mp4", 20, CreateThumbnails(T1, T2));
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);
            Assert.AreEqual(0, matches.Count, "Should not match because T2_Similar_70 (87.7%) does not meet 90% threshold with T2.");
        }


        [TestMethod]
        public void FindSubClipMatches_EmptySubClipThumbnails_FindsNone()
        {
            _settings.ThumbnailCount = 1; // even if 1, sub has 0
            var mainVideo = CreateTestFileEntry("main.mp4", 40, CreateThumbnails(T1, T2, T3, T4));
            var subClip = CreateTestFileEntry("sub_empty.mp4", 20, CreateThumbnails()); // No thumbnails
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);

            Assert.AreEqual(0, matches.Count);
        }

        [TestMethod]
        public void FindSubClipMatches_SubClipLongerThanMain_FindsNone()
        {
            _settings.ThumbnailCount = 2;
            var mainVideo = CreateTestFileEntry("main.mp4", 20, CreateThumbnails(T1, T2));
            var subClip = CreateTestFileEntry("sub_longer.mp4", 40, CreateThumbnails(T1, T2, T3, T4));
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);

            Assert.AreEqual(0, matches.Count);
        }

        [TestMethod]
        public void FindSubClipMatches_MainVideoNotEnoughThumbnailsForSub_FindsNone()
        {
            _settings.ThumbnailCount = 3; // Sub-clip needs 3 thumbnails
            var mainVideo = CreateTestFileEntry("main.mp4", 20, CreateThumbnails(T1, T2)); // Main only has 2
            var subClip = CreateTestFileEntry("sub.mp4", 30, CreateThumbnails(T1, T2, T3));
            var files = new List<FileEntry> { mainVideo, subClip };
            
            // Override scan engine settings for this specific test regarding required thumbnail counts
            // The method FindSubClipMatches uses settings.ThumbnailCount to check if *each file* has enough.
            // And then compares based on the actual count in subThumbnails.
            // Here, mainVideo.grayBytes.Count (2) < subClip.grayBytes.Count (3)
            // The internal logic `if (mainThumbnails.Count < subThumbnails.Count) continue;` will catch this.

            var matches = _scanEngine.FindSubClipMatches(files, _settings);
            Assert.AreEqual(0, matches.Count, "Main video does not have enough thumbnails to contain the sub-clip sequence.");
        }

        [TestMethod]
        public void FindSubClipMatches_SubClipRequiresMoreThumbnailsThanSetting_FindsNone()
        {
            _settings.ThumbnailCount = 2; // Global setting expects 2 thumbnails
            var mainVideo = CreateTestFileEntry("main.mp4", 30, CreateThumbnails(T1, T2, T3));
            // Sub-clip has 3 thumbnails, but the settings.ThumbnailCount check in FindSubClipMatches
            // is `potentialMain.grayBytes.Count < settings.ThumbnailCount || potentialSub.grayBytes.Count < settings.ThumbnailCount`
            // This means if subClip has MORE than settings.ThumbnailCount, it passes this initial check.
            // The logic then proceeds based on the actual number of thumbnails in the sub-clip.
            var subClip = CreateTestFileEntry("sub.mp4", 30, CreateThumbnails(T1, T2, T3)); 
            var files = new List<FileEntry> { mainVideo, subClip };
            
            // In this case, mainVideo has 3 thumbnails, subClip has 3. settings.ThumbnailCount is 2.
            // The initial check `potentialSub.grayBytes.Count < settings.ThumbnailCount` (3 < 2) is false.
            // The comparison loop will run.

            var matches = _scanEngine.FindSubClipMatches(files, _settings);
            Assert.AreEqual(1, matches.Count, "Should still find a match if sub-clip has more thumbnails than setting, as long as main has enough.");
            Assert.AreEqual(mainVideo, matches[0].MainVideo);
            Assert.AreEqual(subClip, matches[0].SubClipVideo);
            // Sub-clip has 3 thumbnails (T1, T2, T3). Main has (T1, T2, T3). Match at start.
            CollectionAssert.AreEqual(new List<double> { 0.0, 0.1, 0.2 }, matches[0].MainVideoMatchStartTimes);
        }
         [TestMethod]
        public void FindSubClipMatches_MultipleMatches_SamePair_DifferentSegments()
        {
            _settings.ThumbnailCount = 1; // Sub has 1 thumbnail
            var mainVideo = CreateTestFileEntry("main.mp4", 50, CreateThumbnails(T1, T2, T1, T3, T1)); // T1 appears multiple times
            var subClip = CreateTestFileEntry("sub.mp4", 10, CreateThumbnails(T1));
            var files = new List<FileEntry> { mainVideo, subClip };

            var matches = _scanEngine.FindSubClipMatches(files, _settings);

            Assert.AreEqual(3, matches.Count, "Should find T1 matching at three different positions in main.");
            
            // Verify each match points to the same main and sub video
            Assert.IsTrue(matches.All(m => m.MainVideo == mainVideo && m.SubClipVideo == subClip));

            // Verify the different start times
            var expectedStartTimes = new List<List<double>>
            {
                new List<double> { 0.0 }, // T1 at index 0
                new List<double> { 0.2 }, // T1 at index 2
                new List<double> { 0.4 }  // T1 at index 4
            };

            var actualStartTimes = matches.Select(m => m.MainVideoMatchStartTimes).ToList();
            
            foreach(var expectedTimeList in expectedStartTimes)
            {
                Assert.IsTrue(actualStartTimes.Any(actualTimeList => actualTimeList.SequenceEqual(expectedTimeList)),
                    $"Expected start time sequence {string.Join(",", expectedTimeList)} not found.");
            }
        }
    }
}
