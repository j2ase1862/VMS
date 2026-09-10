using System;
using VMS.Core.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// MLOps 클라이언트가 시작 시 만들어지지 못한 사유가 화면에 닿는지 (2026-09-10: Production 보안 모드의 http:// 거부가
    /// 조용히 삼켜져 NG 수집이 빠졌던 사고). 정적 상태라 테스트마다 Reset.
    /// </summary>
    [Collection("MlopsClientStatus")]
    public class MlopsClientStatusTests : IDisposable
    {
        public MlopsClientStatusTests() => MlopsClientStatus.Reset();
        public void Dispose() => MlopsClientStatus.Reset();

        [Fact]
        public void Nothing_reported_means_no_issue()
        {
            Assert.False(MlopsClientStatus.HasDisabled);
            Assert.Null(MlopsClientStatus.Summary);
        }

        [Fact]
        public void Https_policy_rejection_is_rewritten_into_field_guidance()
        {
            MlopsClientStatus.ReportDisabled(MlopsClientStatus.NgCollector, new InvalidOperationException(
                "보안 정책 위반 — Production 모드에서 HTTPS 가 필수입니다. 소스 'LineNgImageUploader' URL='http://192.168.219.83:5310'."));

            Assert.True(MlopsClientStatus.HasDisabled);
            var summary = MlopsClientStatus.Summary!;
            Assert.StartsWith(MlopsClientStatus.NgCollector + ": ", summary);
            Assert.Contains("보안 모드를 Development", summary);
            Assert.Contains("https://", summary);
        }

        [Fact]
        public void Other_exceptions_keep_their_message_and_components_are_listed_separately()
        {
            MlopsClientStatus.ReportDisabled(MlopsClientStatus.NgCollector, new ArgumentException("라인 토큰이 필요합니다."));
            MlopsClientStatus.ReportDisabled(MlopsClientStatus.ModelRegistry, new UriFormatException("주소 형식 오류"));

            var summary = MlopsClientStatus.Summary!;
            Assert.Contains("NG 이미지 수집: 라인 토큰이 필요합니다.", summary);
            Assert.Contains("모델 레지스트리: 주소 형식 오류", summary);
        }

        [Fact]
        public void Enabled_report_clears_only_that_component()
        {
            MlopsClientStatus.ReportDisabled(MlopsClientStatus.NgCollector, new Exception("a"));
            MlopsClientStatus.ReportDisabled(MlopsClientStatus.ModelRegistry, new Exception("b"));
            MlopsClientStatus.ReportEnabled(MlopsClientStatus.ModelRegistry);

            Assert.True(MlopsClientStatus.HasDisabled);
            Assert.DoesNotContain("모델 레지스트리", MlopsClientStatus.Summary);
            Assert.Contains("NG 이미지 수집: a", MlopsClientStatus.Summary);
        }
    }
}
