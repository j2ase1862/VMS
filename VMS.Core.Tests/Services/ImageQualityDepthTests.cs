using System;
using OpenCvSharp;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 밝기·대비를 카메라 비트 심도와 무관하게 같은 축(0~255)에서 읽는지.
    ///
    /// <para><b>왜 필요한가.</b> 예전에는 원시 평균을 그대로 실었다. 16bit 카메라에서는 밝기가
    /// 수천으로 나왔고, Web 의 업로드 검증(0~255)에 걸려 400 이 됐다. VMS 는 400 을 <b>영구 거절</b>
    /// 로 분류하므로 그 사이클의 측정값·판정·작업지시 수량까지 통째로 잃었다 — 부가 지표 하나
    /// 때문에 생산 기록이 사라진 것이다. 예측 피처로서도 카메라가 바뀌면 축이 달라져 쓸 수 없었다.</para>
    /// </summary>
    public class ImageQualityDepthTests
    {
        [Fact]
        public void An_8bit_gray_image_reports_its_own_mean()
        {
            using var mat = new Mat(10, 10, MatType.CV_8UC1, new Scalar(128));

            var result = ImageQualityMetrics.Compute(mat);

            Assert.InRange(result.Brightness, 127.0, 129.0);
        }

        /// <summary>같은 밝기의 16bit 이미지는 8bit 와 같은 값으로 읽혀야 한다.</summary>
        [Fact]
        public void A_16bit_image_is_scaled_to_the_same_axis()
        {
            // 16bit 의 중간 밝기 = 32768 → 8bit 기준 약 128
            using var mat = new Mat(10, 10, MatType.CV_16UC1, new Scalar(32768));

            var result = ImageQualityMetrics.Compute(mat);

            Assert.InRange(result.Brightness, 120.0, 136.0);
            Assert.True(result.Brightness <= 255.0, "16bit 원시 평균(수천)이 그대로 실리면 업로드가 거절된다");
        }

        [Fact]
        public void A_float_image_uses_the_0_to_1_convention()
        {
            using var mat = new Mat(10, 10, MatType.CV_32FC1, new Scalar(0.5));

            var result = ImageQualityMetrics.Compute(mat);

            Assert.InRange(result.Brightness, 120.0, 136.0);
        }

        /// <summary>대비(표준편차)도 같은 배율을 타야 밝기와 단위가 맞는다.</summary>
        [Fact]
        public void Contrast_uses_the_same_scale_as_brightness()
        {
            using var mat8 = new Mat(2, 2, MatType.CV_8UC1);
            mat8.Set(0, 0, (byte)0);
            mat8.Set(0, 1, (byte)255);
            mat8.Set(1, 0, (byte)0);
            mat8.Set(1, 1, (byte)255);

            using var mat16 = new Mat(2, 2, MatType.CV_16UC1);
            mat16.Set(0, 0, (ushort)0);
            mat16.Set(0, 1, (ushort)65535);
            mat16.Set(1, 0, (ushort)0);
            mat16.Set(1, 1, (ushort)65535);

            var r8 = ImageQualityMetrics.Compute(mat8);
            var r16 = ImageQualityMetrics.Compute(mat16);

            Assert.True(Math.Abs(r8.ContrastStd - r16.ContrastStd) < 2.0,
                $"8bit {r8.ContrastStd:F1} vs 16bit {r16.ContrastStd:F1} — 같은 장면이면 같은 값이어야 한다");
        }

        [Fact]
        public void A_null_image_is_not_an_error()
        {
            var result = ImageQualityMetrics.Compute(null);

            Assert.Equal(0, result.Brightness);
        }
    }

    /// <summary>
    /// 라인 번호 허용 범위 — Web 의 검증기와 같은 값을 써야 한다.
    ///
    /// <para>예전에는 VMS 입력에 상한이 없어서, 100 이상으로 설정하면 하트비트는 통과하는데
    /// 자가 등록만 영구히 400 이 됐다. 라인은 등록되지 않은 채 남고 그 사이 검사 결과가 전부
    /// 404 를 받아 폐기됐는데, 화면에는 "Web 연결 끊김" 만 보였다.</para>
    /// </summary>
    public class WebClientIndexTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(99)]
        public void Registerable_values_are_valid(int index)
            => Assert.True(WebClientIndex.IsValid(index));

        [Theory]
        [InlineData(-1)]
        [InlineData(100)]
        [InlineData(999)]
        public void Values_the_server_will_never_register_are_invalid(int index)
            => Assert.False(WebClientIndex.IsValid(index));
    }
}
