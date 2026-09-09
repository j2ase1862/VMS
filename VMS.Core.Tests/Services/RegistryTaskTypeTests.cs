using System;
using VMS.Core.Models.Annotation;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 학습 도구의 작업 유형 이름과 레지스트리의 이름이 다르다. 이 대응이 어긋나면
    /// 업로드가 "작업 유형이 맞지 않습니다" 로 막히는데, 두 이름을 나란히 보기 전에는
    /// 왜 막혔는지 알기 어렵다 — 그래서 대응 자체를 시험으로 못 박는다.
    /// </summary>
    public class RegistryTaskTypeTests
    {
        [Theory]
        [InlineData(DatasetTaskType.Detection, "detection")]
        [InlineData(DatasetTaskType.Classification, "classification")]
        [InlineData(DatasetTaskType.OCR, "ocr")]
        [InlineData(DatasetTaskType.AnomalyDetection, "anomaly")]
        [InlineData(DatasetTaskType.Segmentation, "segmentation")]
        public void From_MapsToRegistryName(DatasetTaskType taskType, string expected)
            => Assert.Equal(expected, RegistryTaskType.From(taskType));

        [Theory]
        [InlineData(DatasetTaskType.Detection)]
        [InlineData(DatasetTaskType.Classification)]
        [InlineData(DatasetTaskType.OCR)]
        [InlineData(DatasetTaskType.AnomalyDetection)]
        [InlineData(DatasetTaskType.Segmentation)]
        public void To_RoundTrips(DatasetTaskType taskType)
            => Assert.Equal(taskType, RegistryTaskType.To(RegistryTaskType.From(taskType)));

        [Fact]
        public void To_IgnoresCaseAndPadding()
            => Assert.Equal(DatasetTaskType.AnomalyDetection, RegistryTaskType.To("  Anomaly "));

        [Fact]
        public void To_ReturnsNullForUnknown()
        {
            Assert.Null(RegistryTaskType.To("pose"));
            Assert.Null(RegistryTaskType.To(null));
            Assert.Null(RegistryTaskType.To(""));
        }

        /// <summary>
        /// 모르는 값을 조용히 detection 으로 밀면 엉뚱한 계열에 모델이 쌓인다.
        /// 새 작업 유형이 생기면 이 시험이 먼저 깨져야 한다.
        /// </summary>
        [Fact]
        public void From_ThrowsForUnknown()
            => Assert.Throws<ArgumentOutOfRangeException>(() => RegistryTaskType.From((DatasetTaskType)999));

        [Fact]
        public void Describe_IsKorean()
            => Assert.Equal("이상 탐지", RegistryTaskType.Describe(DatasetTaskType.AnomalyDetection));
    }
}
