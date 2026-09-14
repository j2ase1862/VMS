using System;
using System.Net;
using VMS.Core.Models.ParameterSync;
using VMS.Services;
using VMS.Services.ImageUpload;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// Web 연동 계약의 남은 어긋남 3건 (전수 점검 W-134 · W-139 · W-137).
    ///
    /// <para>셋 다 "조용히 틀리는" 부류다 — 화면에 오류가 뜨지 않고 숫자나 상태만 어긋난다.</para>
    /// </summary>
    public class WebContractRemnantTests
    {
        // ── W-139: 이미지 업로드 영구 거절 분류 ────────────────────────────

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]          // meta 파손 — 다시 보내도 같은 답
        [InlineData(HttpStatusCode.RequestEntityTooLarge)] // 30MB 초과 — 같은 바이트면 영원히 413
        [InlineData(HttpStatusCode.UnsupportedMediaType)]
        [InlineData(HttpStatusCode.NotFound)]
        public void PermanentRejection_StopsRetrying(HttpStatusCode code)
        {
            Assert.True(ImageUploadService.IsPermanentRejection(code));
        }

        [Theory]
        [InlineData(HttpStatusCode.Conflict)]        // 결과 레코드 미도착 — 곧 도착한다
        [InlineData(HttpStatusCode.Unauthorized)]    // 키가 고쳐지면 통한다
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        public void TransientFailure_KeepsRetrying(HttpStatusCode code)
        {
            Assert.False(ImageUploadService.IsPermanentRejection(code));
        }

        [Fact]
        public void RetryGrace_EventuallyGivesUp_SoTheQueueDoesNotFill()
        {
            var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

            // 재시도 대상이라도 영원히 붙잡지 않는다 — 큐 상한에 닿으면 EnforceCap 이
            // 정상 NG 이미지부터 버린다(2026-08-19 현장).
            Assert.False(ImageUploadService.IsPastRetryGrace(
                now.AddHours(-1).ToString("o"), fallbackUtc: now, nowUtc: now));
            Assert.True(ImageUploadService.IsPastRetryGrace(
                now.AddHours(-7).ToString("o"), fallbackUtc: now, nowUtc: now));
        }

        [Fact]
        public void RetryGrace_FallsBackToFileTime_WhenCapturedAtUnparsable()
        {
            var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

            Assert.True(ImageUploadService.IsPastRetryGrace(
                "(깨진 값)", fallbackUtc: now.AddHours(-8), nowUtc: now));
            Assert.False(ImageUploadService.IsPastRetryGrace(
                null, fallbackUtc: now.AddMinutes(-5), nowUtc: now));
        }

        // ── W-137: 사이클 업로드의 CycleTimeMs 는 합계 ─────────────────────

        [Fact]
        public void CycleUpload_UsesTotalCycleTime_NotLastStep()
        {
            var lastStep = new InspectionFeatureMetrics
            {
                CycleTimeMs = 120,          // 마지막 스텝만의 시간
                Brightness = 88.5,
                FocusScore = 0.42,
                BlobCount = 3,
                DlModelVersion = "fitting-detector@3",
            };

            var uploaded = InspectionService.WithCycleTotal(lastStep, totalMs: 640);

            Assert.Equal(640, uploaded!.CycleTimeMs);
            Assert.NotSame(lastStep, uploaded);          // 원본은 다른 소비자가 쓴다
            Assert.Equal(120, lastStep.CycleTimeMs);

            // 이미지 품질 피처는 대표 1장 기준이라 그대로 따라간다
            Assert.Equal(88.5, uploaded.Brightness);
            Assert.Equal(0.42, uploaded.FocusScore);
            Assert.Equal(3, uploaded.BlobCount);
            Assert.Equal("fitting-detector@3", uploaded.DlModelVersion);
        }

        [Fact]
        public void CycleUpload_LeavesMetricsAlone_WhenNoTimeAccumulated()
        {
            var metrics = new InspectionFeatureMetrics { CycleTimeMs = 120 };

            // 스텝이 시간을 보고하지 않은 사이클(합계 0)에서 0 으로 덮어쓰면 되레 거짓이 된다.
            Assert.Same(metrics, InspectionService.WithCycleTotal(metrics, totalMs: 0));
            Assert.Null(InspectionService.WithCycleTotal(null, totalMs: 500));
        }
    }
}
