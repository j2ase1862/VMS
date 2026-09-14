using System;
using System.Collections.Generic;
using System.Text.Json;
using VMS.Core.Models.ParameterSync;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 업로드가 <b>검사한 시각</b>을 실어 보내는가 (P-06).
    ///
    /// <para><b>왜.</b> 전송이 실패하면 요청은 디스크 큐에 담겼다가 복구된 뒤에야 올라간다.
    /// Web 은 예전에 첫 측정값의 Timestamp 로 시각을 잡고, 그것이 없으면(판정 전용 사이클
    /// 업로드 — Results 가 비어 있다) <b>도착 시각</b>을 썼다. 그래서 네트워크가 몇 시간
    /// 끊겼다 복구되면 그 시간치가 전부 '지금' 으로 기록돼 교대 배정·일별 집계·예측 모델의
    /// 시간 창이 통째로 어긋났다.</para>
    /// </summary>
    public class UploadInspectedAtTests
    {
        private static ParameterResultUploadRequest Build(
            DateTimeOffset inspectedAt, List<ParameterResultDto>? results = null) =>
            ParameterSyncService.BuildUploadRequest(
                clientIndex: 3,
                recipeId: 10,
                results: results ?? new List<ParameterResultDto>(),
                featureMetrics: null,
                correlationKey: "cycle-1",
                overallPass: true,
                workOrderId: 100,
                lotId: 1000,
                operatorId: null,
                serialNumber: null,
                inspectedAt: inspectedAt);

        [Fact]
        public void Request_carries_the_time_the_cycle_was_inspected()
        {
            var inspected = DateTimeOffset.Now.AddHours(-3);

            var request = Build(inspected);

            Assert.Equal(inspected, request.InspectedAt);
        }

        /// <summary>판정 전용(Results 비어 있음) 업로드가 바로 이 필드가 필요한 경우다.</summary>
        [Fact]
        public void Judgment_only_upload_still_has_a_time()
        {
            var request = Build(DateTimeOffset.Now);

            Assert.Empty(request.Results);
            Assert.NotNull(request.InspectedAt);
        }

        /// <summary>
        /// 큐에 저장했다 다시 읽어도 같은 순간이어야 한다 — 큐 왕복이 이 값을 잃으면
        /// 고치려던 문제가 그대로 남는다.
        /// </summary>
        [Fact]
        public void Queue_roundtrip_preserves_the_instant()
        {
            var inspected = new DateTimeOffset(2026, 9, 14, 21, 30, 0, TimeSpan.FromHours(9));
            var request = Build(inspected);

            var json = JsonSerializer.Serialize(request, ParameterSyncService.JsonOptions);
            var restored = JsonSerializer.Deserialize<ParameterResultUploadRequest>(
                json, ParameterSyncService.JsonOptions);

            Assert.NotNull(restored!.InspectedAt);
            Assert.Equal(inspected.UtcDateTime, restored.InspectedAt!.Value.UtcDateTime);
        }

        /// <summary>Web 이 읽는 이름(camelCase)으로 나가는가 — 이름이 어긋나면 조용히 무시된다.</summary>
        [Fact]
        public void Field_is_sent_with_the_name_the_server_reads()
        {
            var json = JsonSerializer.Serialize(Build(DateTimeOffset.Now), ParameterSyncService.JsonOptions);

            Assert.Contains("\"inspectedAt\"", json);
        }

        /// <summary>오프셋이 함께 나가야 서버가 UTC 로 정확히 환산한다(로컬 시각만 보내면 9시간 어긋난다).</summary>
        [Fact]
        public void Serialized_value_keeps_its_offset()
        {
            var inspected = new DateTimeOffset(2026, 9, 14, 21, 30, 0, TimeSpan.FromHours(9));

            var json = JsonSerializer.Serialize(Build(inspected), ParameterSyncService.JsonOptions);

            Assert.Contains("2026-09-14T21:30:00+09:00", json);
        }
    }
}
