using Microsoft.VisualStudio.TestTools.UnitTesting;
using VDF.Core;
using VDF.Core.Utils;
using VDF.Core.ViewModels; // Required for MediaInfo if not fully qualified
using System;
using System.Collections.Generic;
using System.Linq;

namespace VDF.Core.Tests
{
    [TestClass]
    public class TimeLimitedScanTests
    {
        private ScanEngine _scanEngine;
        private FileEntry _fileOld;
        private FileEntry _fileRecent1;
        private FileEntry _fileRecent2;

        [TestInitialize]
        public void TestInitialize()
        {
            // Ensure a clean database for each test
            DatabaseUtils.Database = new HashSet<FileEntry>();
            _scanEngine = new ScanEngine();
            _scanEngine.Duplicates.Clear(); // Clear duplicates from previous runs if any

            // Common settings for these tests
            _scanEngine.Settings.ThumbnailCount = 1;
            _scanEngine.Settings.Percent = 100f; // Expect exact matches for simplicity

            _fileOld = new FileEntry("c:\\dummy\\old.mp4")
            {
                DateModified = DateTime.UtcNow.AddHours(-2),
                grayBytes = new Dictionary<double, byte[]?> { { 0.5, new byte[] { 1, 2, 3 } } },
                mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(10) }
            };
            _fileRecent1 = new FileEntry("c:\\dummy\\recent1.mp4")
            {
                DateModified = DateTime.UtcNow.AddMinutes(-10),
                grayBytes = new Dictionary<double, byte[]?> { { 0.5, new byte[] { 1, 2, 3 } } },
                mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(10) }
            };
            _fileRecent2 = new FileEntry("c:\\dummy\\recent2.mp4")
            {
                DateModified = DateTime.UtcNow.AddMinutes(-5),
                grayBytes = new Dictionary<double, byte[]?> { { 0.5, new byte[] { 1, 2, 3 } } },
                mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(10) }
            };
        }

        [TestCleanup]
        public void TestCleanup()
        {
            DatabaseUtils.Database.Clear();
            _scanEngine.Duplicates.Clear();
        }

        [TestMethod]
        public void ScanForDuplicates_TimeLimit1Hour_IncludesRecent()
        {
            DatabaseUtils.Database.Add(_fileOld);
            DatabaseUtils.Database.Add(_fileRecent1);
            DatabaseUtils.Database.Add(_fileRecent2);

            _scanEngine.Settings.EnableTimeLimitedScan = true;
            _scanEngine.Settings.TimeLimitSeconds = 3600; // 1 hour

            _scanEngine.StartCompare(); // This will run ScanForDuplicates

            Assert.AreEqual(2, _scanEngine.Duplicates.Count, "Should find one pair of duplicates.");
            var duplicateGroup = _scanEngine.Duplicates.Select(d => d.Path).ToList();
            CollectionAssert.Contains(duplicateGroup, _fileRecent1.Path);
            CollectionAssert.Contains(duplicateGroup, _fileRecent2.Path);
            CollectionAssert.DoesNotContain(duplicateGroup, _fileOld.Path);
        }

        [TestMethod]
        public void ScanForDuplicates_TimeLimit1Minute_NoDuplicates()
        {
            DatabaseUtils.Database.Add(_fileOld);
            DatabaseUtils.Database.Add(_fileRecent1); // Modified 10 mins ago
            DatabaseUtils.Database.Add(_fileRecent2); // Modified 5 mins ago

            _scanEngine.Settings.EnableTimeLimitedScan = true;
            _scanEngine.Settings.TimeLimitSeconds = 60; // 1 minute

            _scanEngine.StartCompare();

            Assert.AreEqual(0, _scanEngine.Duplicates.Count, "Should find no duplicates as all files are older than 1 minute relative to their scan window.");
        }

        [TestMethod]
        public void ScanForDuplicates_TimeLimit5Minutes_OnlyRecent2ConsideredButNoPair()
        {
            // This test checks if only one recent file within limit is handled correctly (no duplicate pair)
            var fileVeryRecent = new FileEntry("c:\\dummy\\very_recent.mp4")
            {
                DateModified = DateTime.UtcNow.AddMinutes(-1), // 1 minute ago
                grayBytes = new Dictionary<double, byte[]?> { { 0.5, new byte[] { 1, 2, 3 } } },
                mediaInfo = new MediaInfo { Duration = TimeSpan.FromSeconds(10) }
            };

            DatabaseUtils.Database.Add(_fileOld); // 2 hours old
            DatabaseUtils.Database.Add(_fileRecent1); // 10 minutes old
            DatabaseUtils.Database.Add(fileVeryRecent); // 1 minute old


            _scanEngine.Settings.EnableTimeLimitedScan = true;
            _scanEngine.Settings.TimeLimitSeconds = 300; // 5 minutes

            _scanEngine.StartCompare();

            Assert.AreEqual(0, _scanEngine.Duplicates.Count, "Only one file (very_recent) is within 5-min scan window, so no pairs.");
        }


        [TestMethod]
        public void ScanForDuplicates_TimeLimitDisabled_IncludesAll()
        {
            DatabaseUtils.Database.Add(_fileOld);
            DatabaseUtils.Database.Add(_fileRecent1);
            DatabaseUtils.Database.Add(_fileRecent2);

            _scanEngine.Settings.EnableTimeLimitedScan = false;
            // TimeLimitSeconds is ignored when EnableTimeLimitedScan is false

            _scanEngine.StartCompare();

            Assert.AreEqual(3, _scanEngine.Duplicates.Count, "Should find all three as duplicates.");
            var duplicateGroup = _scanEngine.Duplicates.Select(d => d.Path).ToList();
            CollectionAssert.Contains(duplicateGroup, _fileOld.Path);
            CollectionAssert.Contains(duplicateGroup, _fileRecent1.Path);
            CollectionAssert.Contains(duplicateGroup, _fileRecent2.Path);
        }
    }
}
