﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BCad.Test
{
    [TestClass]
    public class UnitFormatTests
    {
        [TestMethod]
        public void MetricFormatTest()
        {
            // nearest whole number
            TestMetric(3.0, "3", 0);
            TestMetric(3.4, "3", 0);
            TestMetric(3.5, "4", 0);

            // 3 decimal places
            TestMetric(3.0, "3", 3);
            TestMetric(3.14159, "3.142", 3);

            // 4 decimal places
            TestMetric(3.14159, "3.1416", 4);
        }

        [TestMethod]
        public void ArchitecturalFormatTest()
        {
            // nearest inch
            TestArch(0.0, "0'0\"", 0); // 0'0"
            TestArch(15.2, "1'3\"", 0); // 1'3"

            // nearest eighth inch
            TestArch(0.0, "0'0\"", 8); // 0'0"
            TestArch(15.2, "1'3-1/8\"", 8); // 1'3-1/8"

            // even feet
            TestArch(24.0, "2'0\"", 16); // 2'

            // only fractional inches
            TestArch(0.125, "0'0-1/8\"", 16); // 1/8"

            // feet and fractional inches, no whole inches
            TestArch(36.625, "3'0-5/8\"", 16); // 3'5/8"

            // near the upper limit
            TestArch(15.99999999, "1'4\"", 16);
        }

        [TestMethod]
        public void MetricParseTest()
        {
            TestParse("0", 0.0);
            TestParse("3.25", 3.25);
            TestParse("-4.8", -4.8);
        }

        [TestMethod]
        public void ArchitecturalParseTest()
        {
            TestParse("18", 18.0); // just inches, no specifier
            TestParse("18\"", 18.0); // just inches, with specifier

            TestParse("2'", 24.0); // just feet

            TestParse("2'3\"", 27.0); // feet and inches, with specifier
            TestParse("2'3", 27.0); // feet and inches, no specifier

            TestParse("1'3-5/8\"", 15.625); // feet and whole and fractional inches
            TestParse("1'5/8\"", 12.625); // feet and fractional inches

            TestParse("1'3.5\"", 15.5); // feet with decimal inches with specifier
            TestParse("1'3.5", 15.5); // feet with decimal inches without specifier
        }

        [TestMethod]
        public void ParseFailureTest()
        {
            ParseFail(""); // empty string
            ParseFail("foo"); // not a number
            ParseFail("1'3foo"); // garbage tail
        }

        private void TestParse(string text, double expected)
        {
            double actual;
            Assert.IsTrue(DrawingSettings.TryParseUnits(text, out actual));
            Assert.AreEqual(expected, actual);
        }

        private void ParseFail(string text)
        {
            double temp;
            Assert.IsFalse(DrawingSettings.TryParseUnits(text, out temp));
        }

        private void TestMetric(double value, string expected, int precision)
        {
            Assert.AreEqual(expected, DrawingSettings.FormatUnits(value, UnitFormat.Metric, precision));
        }

        private void TestArch(double value, string expected, int precision)
        {
            Assert.AreEqual(expected, DrawingSettings.FormatUnits(value, UnitFormat.Architectural, precision));
        }
    }
}
