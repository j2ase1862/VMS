using System.Collections.Generic;
using OpenCvSharp;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// ResultValueFormatter — Run Results 표(VisionSetup · VMS 메인)의 값 글자.
    /// 목록 값이 "System.Collections.Generic.List`1[…]" 형식 이름으로 보이던 결함(2026-10-24) 회귀 방지.
    /// </summary>
    public class ResultValueFormatterTests
    {
        [Fact]
        public void Numbers_UseThreeDecimals()
        {
            Assert.Equal("1.235", ResultValueFormatter.Format(1.23456));
            Assert.Equal("0.500", ResultValueFormatter.Format(0.5f));
            Assert.Equal("7", ResultValueFormatter.Format(7));
            Assert.Equal("True", ResultValueFormatter.Format(true));
            Assert.Equal("ABC 123", ResultValueFormatter.Format("ABC 123"));
            Assert.Equal("", ResultValueFormatter.Format(null));
        }

        [Fact]
        public void ListOfObjectsWithoutText_ShowsCountOnly()
        {
            var detections = new List<DetectionResult> { new() };
            var text = ResultValueFormatter.Format(detections);

            Assert.Equal("1개", text);
            Assert.DoesNotContain("System.", text);
        }

        [Fact]
        public void ListOfNumbers_ShowsItems()
        {
            Assert.Equal("[1.000, 2.500]", ResultValueFormatter.Format(new List<double> { 1, 2.5 }));
            Assert.Equal("0개", ResultValueFormatter.Format(new List<double>()));
        }

        [Fact]
        public void LongList_ShowsFirstItemsAndCount()
        {
            var text = ResultValueFormatter.Format(new[] { 1, 2, 3, 4, 5, 6 });
            Assert.Equal("[1, 2, 3, 4, …] (6개)", text);
        }

        [Fact]
        public void TupleList_FormatsEachMember()
        {
            var topN = new List<(int ClassId, float Score)> { (0, 0.98765f), (1, 0.01234f) };
            Assert.Equal("[(0, 0.988), (1, 0.012)]", ResultValueFormatter.Format(topN));
        }

        [Fact]
        public void StructWithOwnText_IsKept()
        {
            var p = new Point2d(1.5, 2);
            Assert.Equal(p.ToString(), ResultValueFormatter.Format(p));
        }

        [Fact]
        public void ObjectWithoutText_ShowsShortTypeName()
        {
            var text = ResultValueFormatter.Format(new DetectionResult());
            Assert.Equal(nameof(DetectionResult), text);
        }

        [Fact]
        public void Dictionary_ShowsCount()
        {
            Assert.Equal("2개", ResultValueFormatter.Format(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }));
        }
    }
}
