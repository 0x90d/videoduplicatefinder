using Microsoft.VisualStudio.TestTools.UnitTesting;
using VDF.Core;
using VDF.Core.Utils;
using System.Collections.Generic;
using System.Linq;

namespace VDF.Core.Tests
{
    [TestClass]
    public class BrokenFileDetectionTests
    {
        private ScanEngine _scanEngine;

        [TestInitialize]
        public void TestInitialize()
        {
            DatabaseUtils.Database = new HashSet<FileEntry>();
            _scanEngine = new ScanEngine();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            DatabaseUtils.Database.Clear();
        }

        [TestMethod]
        public void GetBrokenFileEntries_NoBrokenFiles_ReturnsEmptyList()
        {
            var entryOk1 = new FileEntry("ok1.mp4");
            var entryOk2 = new FileEntry("ok2.mp4");
            DatabaseUtils.Database.Add(entryOk1);
            DatabaseUtils.Database.Add(entryOk2);

            var brokenFiles = _scanEngine.GetBrokenFileEntries();

            Assert.AreEqual(0, brokenFiles.Count);
        }

        [TestMethod]
        public void GetBrokenFileEntries_OneMetadataError_ReturnsOne()
        {
            var entryOk = new FileEntry("ok.mp4");
            var entryMetaError = new FileEntry("meta.mp4");
            entryMetaError.Flags.Set(EntryFlags.MetadataError);
            DatabaseUtils.Database.Add(entryOk);
            DatabaseUtils.Database.Add(entryMetaError);

            var brokenFiles = _scanEngine.GetBrokenFileEntries();

            Assert.AreEqual(1, brokenFiles.Count);
            Assert.IsTrue(brokenFiles.Contains(entryMetaError));
        }

        [TestMethod]
        public void GetBrokenFileEntries_OneThumbnailError_ReturnsOne()
        {
            var entryOk = new FileEntry("ok.mp4");
            var entryThumbError = new FileEntry("thumb.mp4");
            entryThumbError.Flags.Set(EntryFlags.ThumbnailError);
            DatabaseUtils.Database.Add(entryOk);
            DatabaseUtils.Database.Add(entryThumbError);

            var brokenFiles = _scanEngine.GetBrokenFileEntries();

            Assert.AreEqual(1, brokenFiles.Count);
            Assert.IsTrue(brokenFiles.Contains(entryThumbError));
        }

        [TestMethod]
        public void GetBrokenFileEntries_BothErrorsInOneFile_ReturnsOne()
        {
            var entryOk = new FileEntry("ok.mp4");
            var entryBothError = new FileEntry("both.mp4");
            entryBothError.Flags.Set(EntryFlags.MetadataError);
            entryBothError.Flags.Set(EntryFlags.ThumbnailError);
            DatabaseUtils.Database.Add(entryOk);
            DatabaseUtils.Database.Add(entryBothError);

            var brokenFiles = _scanEngine.GetBrokenFileEntries();

            Assert.AreEqual(1, brokenFiles.Count);
            Assert.IsTrue(brokenFiles.Contains(entryBothError));
        }

        [TestMethod]
        public void GetBrokenFileEntries_MultipleBrokenFiles_ReturnsAllBroken()
        {
            var entryOk = new FileEntry("ok.mp4");
            var entryMetaError = new FileEntry("meta.mp4");
            entryMetaError.Flags.Set(EntryFlags.MetadataError);
            var entryThumbError = new FileEntry("thumb.mp4");
            entryThumbError.Flags.Set(EntryFlags.ThumbnailError);
            var entryBothError = new FileEntry("both.mp4");
            entryBothError.Flags.Set(EntryFlags.MetadataError | EntryFlags.ThumbnailError);

            DatabaseUtils.Database.Add(entryOk);
            DatabaseUtils.Database.Add(entryMetaError);
            DatabaseUtils.Database.Add(entryThumbError);
            DatabaseUtils.Database.Add(entryBothError);

            var brokenFiles = _scanEngine.GetBrokenFileEntries();

            Assert.AreEqual(3, brokenFiles.Count);
            CollectionAssert.Contains(brokenFiles, entryMetaError);
            CollectionAssert.Contains(brokenFiles, entryThumbError);
            CollectionAssert.Contains(brokenFiles, entryBothError);
            CollectionAssert.DoesNotContain(brokenFiles, entryOk);
        }

        [TestMethod]
        public void GetBrokenFileEntries_EmptyDatabase_ReturnsEmptyList()
        {
            // Database is already empty due to TestInitialize and TestCleanup
            var brokenFiles = _scanEngine.GetBrokenFileEntries();
            Assert.AreEqual(0, brokenFiles.Count);
        }
    }
}
