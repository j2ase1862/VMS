using System.Threading.Tasks;
using VMS.Core.Imaging;
using VMS.Services;
using VMS.Services.ImageUpload;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// NG 이미지 Web 미표시(2026-08-19 현장) 회귀 —
    /// ① 전달 모드 Auto + Web 같은 머신이면 업로드가 통째로 스킵되던 분기 제거
    ///    (대안이던 "Web 로컬 경로 참조"는 미구현이라 이미지가 영원히 비었다),
    /// ② 사이클 누적 모드에서 검사별 고유 키 vs Web 이력 1행(마지막 키) 불일치로
    ///    이미지가 409 폐기되던 문제 — 사이클 내 모든 검사가 키를 공유해야 한다.
    /// </summary>
    public class NgImageUploadPolicyTests
    {
        // ─── ① ShouldUpload — Auto = 항상 업로드 ───

        [Theory]
        [InlineData(ImageDeliveryMode.Auto)]
        [InlineData(ImageDeliveryMode.Upload)]
        public void AutoAndUpload_SendNg_WhenToggleOn(ImageDeliveryMode mode)
        {
            var opts = new ImageSaveOptions { DeliveryMode = mode, WebSendNg = true };
            Assert.True(ImageUploadService.ShouldUpload(ok: false, opts));
        }

        [Fact]
        public void SharedPath_NeverUploads()
        {
            var opts = new ImageSaveOptions
            {
                DeliveryMode = ImageDeliveryMode.SharedPath,
                WebSendOk = true,
                WebSendNg = true
            };
            Assert.False(ImageUploadService.ShouldUpload(ok: true, opts));
            Assert.False(ImageUploadService.ShouldUpload(ok: false, opts));
        }

        [Fact]
        public void Toggles_GateByVerdict()
        {
            var opts = new ImageSaveOptions
            {
                DeliveryMode = ImageDeliveryMode.Auto,
                WebSendOk = false,
                WebSendNg = true
            };
            Assert.False(ImageUploadService.ShouldUpload(ok: true, opts));
            Assert.True(ImageUploadService.ShouldUpload(ok: false, opts));
        }

        // ─── ② 사이클 상관 키 공유 ───

        [Fact]
        public async Task CycleMode_AllInspectionsShareKey_NewKeyPerCycle()
        {
            try
            {
                InspectionService.SetCycleAccumulation(true);

                var k1 = InspectionService.CreateCorrelationKey();
                var k2 = InspectionService.CreateCorrelationKey();
                Assert.Equal(k1, k2);   // 같은 사이클 = 같은 키 (이미지·이력 매칭)

                // 사이클 경계(플러시)에서 키 초기화 → 다음 사이클은 새 키
                await InspectionService.FlushCycleResultAsync(overallPass: false);
                var k3 = InspectionService.CreateCorrelationKey();
                Assert.NotEqual(k1, k3);
            }
            finally
            {
                InspectionService.SetCycleAccumulation(false);
            }
        }

        [Fact]
        public void ManualMode_UniqueKeyPerInspection()
        {
            InspectionService.SetCycleAccumulation(false);
            var k1 = InspectionService.CreateCorrelationKey();
            var k2 = InspectionService.CreateCorrelationKey();
            Assert.NotEqual(k1, k2);
        }
    }
}
