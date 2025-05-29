using Microsoft.VisualStudio.TestTools.UnitTesting;
using VDF.Core;
using System;

namespace VDF.Core.Tests
{
    [TestClass]
    public class SegmentDefinitionTests
    {
        [TestMethod]
        public void ValidateAndCalculateTimestamps_AbsoluteTime_Valid()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.AbsoluteTime,
                AbsoluteStartTime = TimeSpan.FromSeconds(10),
                AbsoluteEndTime = TimeSpan.FromSeconds(20)
            };
            var (isValid, calcStart, calcEnd) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsTrue(isValid);
            Assert.AreEqual(TimeSpan.FromSeconds(10), calcStart);
            Assert.AreEqual(TimeSpan.FromSeconds(20), calcEnd);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_AbsoluteTime_StartLessThanZero_ClampsToZero()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.AbsoluteTime,
                AbsoluteStartTime = TimeSpan.FromSeconds(-5),
                AbsoluteEndTime = TimeSpan.FromSeconds(10)
            };
            var (isValid, calcStart, calcEnd) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsTrue(isValid);
            Assert.AreEqual(TimeSpan.FromSeconds(0), calcStart);
            Assert.AreEqual(TimeSpan.FromSeconds(10), calcEnd);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_AbsoluteTime_EndGreaterThanDuration_ClampsToEndOfVideo()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.AbsoluteTime,
                AbsoluteStartTime = TimeSpan.FromSeconds(10),
                AbsoluteEndTime = TimeSpan.FromSeconds(35)
            };
            var (isValid, calcStart, calcEnd) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsTrue(isValid);
            Assert.AreEqual(TimeSpan.FromSeconds(10), calcStart);
            Assert.AreEqual(TimeSpan.FromSeconds(30), calcEnd);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_AbsoluteTime_StartGreaterThanEnd_Invalid()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.AbsoluteTime,
                AbsoluteStartTime = TimeSpan.FromSeconds(20),
                AbsoluteEndTime = TimeSpan.FromSeconds(10)
            };
            var (isValid, _, _) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_AbsoluteTime_ZeroDurationVideo_Invalid()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.AbsoluteTime,
                AbsoluteStartTime = TimeSpan.FromSeconds(10),
                AbsoluteEndTime = TimeSpan.FromSeconds(20)
            };
            var (isValid, _, _) = def.ValidateAndCalculateTimestamps(TimeSpan.Zero);
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_Offset_FromStart_FromStart_Valid()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.Offset,
                StartReference = SegmentDefinition.OffsetReference.FromStart,
                StartOffset = TimeSpan.FromSeconds(5),
                EndReference = SegmentDefinition.OffsetReference.FromStart,
                EndOffset = TimeSpan.FromSeconds(15)
            };
            var (isValid, calcStart, calcEnd) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsTrue(isValid);
            Assert.AreEqual(TimeSpan.FromSeconds(5), calcStart);
            Assert.AreEqual(TimeSpan.FromSeconds(15), calcEnd);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_Offset_FromStart_FromEnd_Valid()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.Offset,
                StartReference = SegmentDefinition.OffsetReference.FromStart,
                StartOffset = TimeSpan.FromSeconds(5),
                EndReference = SegmentDefinition.OffsetReference.FromEnd,
                EndOffset = TimeSpan.FromSeconds(5)
            };
            var (isValid, calcStart, calcEnd) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsTrue(isValid);
            Assert.AreEqual(TimeSpan.FromSeconds(5), calcStart);
            Assert.AreEqual(TimeSpan.FromSeconds(25), calcEnd);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_Offset_FromEnd_FromEnd_Valid()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.Offset,
                StartReference = SegmentDefinition.OffsetReference.FromEnd,
                StartOffset = TimeSpan.FromSeconds(15), // Results in 30-15 = 15s
                EndReference = SegmentDefinition.OffsetReference.FromEnd,
                EndOffset = TimeSpan.FromSeconds(5)   // Results in 30-5 = 25s
            };
            var (isValid, calcStart, calcEnd) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsTrue(isValid);
            Assert.AreEqual(TimeSpan.FromSeconds(15), calcStart);
            Assert.AreEqual(TimeSpan.FromSeconds(25), calcEnd);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_Offset_CalculatedStartLessThanZero_ClampsToZero()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.Offset,
                StartReference = SegmentDefinition.OffsetReference.FromEnd,
                StartOffset = TimeSpan.FromSeconds(35), // 30 - 35 = -5s
                EndReference = SegmentDefinition.OffsetReference.FromEnd,
                EndOffset = TimeSpan.FromSeconds(5)     // 30 - 5 = 25s
            };
            var (isValid, calcStart, calcEnd) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsTrue(isValid); // Still valid as times are clamped
            Assert.AreEqual(TimeSpan.FromSeconds(0), calcStart);
            Assert.AreEqual(TimeSpan.FromSeconds(25), calcEnd);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_Offset_CalculatedEndGreaterThanDuration_ClampsToEndOfVideo()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.Offset,
                StartReference = SegmentDefinition.OffsetReference.FromStart,
                StartOffset = TimeSpan.FromSeconds(5),
                EndReference = SegmentDefinition.OffsetReference.FromStart,
                EndOffset = TimeSpan.FromSeconds(35) // start + 35s
            };
            var (isValid, calcStart, calcEnd) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsTrue(isValid); // Still valid
            Assert.AreEqual(TimeSpan.FromSeconds(5), calcStart);
            Assert.AreEqual(TimeSpan.FromSeconds(30), calcEnd);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_Offset_CalculatedStartGreaterThanCalculatedEnd_Invalid()
        {
            var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.Offset,
                StartReference = SegmentDefinition.OffsetReference.FromStart,
                StartOffset = TimeSpan.FromSeconds(15),
                EndReference = SegmentDefinition.OffsetReference.FromStart,
                EndOffset = TimeSpan.FromSeconds(5)
            };
            var (isValid, _, _) = def.ValidateAndCalculateTimestamps(TimeSpan.FromSeconds(30));
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void ValidateAndCalculateTimestamps_Offset_ZeroDurationVideo_Invalid()
        {
             var def = new SegmentDefinition
            {
                Mode = SegmentDefinition.DefinitionMode.Offset,
                StartReference = SegmentDefinition.OffsetReference.FromStart,
                StartOffset = TimeSpan.FromSeconds(5),
                EndReference = SegmentDefinition.OffsetReference.FromStart,
                EndOffset = TimeSpan.FromSeconds(15)
            };
            var (isValid, _, _) = def.ValidateAndCalculateTimestamps(TimeSpan.Zero);
            Assert.IsFalse(isValid);
        }
    }
}
