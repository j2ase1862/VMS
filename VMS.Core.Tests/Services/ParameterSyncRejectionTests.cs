using System.Net;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 업로드 실패를 "다시 보내면 될 것" 과 "다시 보내도 안 될 것" 으로 가르는 규칙.
    ///
    /// <para><b>왜 이 구분이 있어야 하는가.</b>
    /// 실패한 업로드는 디스크 큐에 쌓이고, 드레인은 <b>첫 실패에서 멈춥니다</b>(순서 보존).
    /// 그래서 다시 보내도 같은 답이 오는 요청이 큐 맨 앞에 박히면 <b>그 뒤의 모든 검사 결과가
    /// 영원히 올라가지 못합니다</b>. 실제로 그런 일이 있었습니다 — DlModelVersion 이 50자를
    /// 넘겨 400 을 받았고, 그 라인의 생산 이력이 그 시점부터 통째로 끊겼습니다.
    /// </para>
    /// <para>
    /// 반대로 너무 많이 버리면 안 됩니다. 인증이 잠깐 흔들린 사이의 검사 결과까지 버려집니다.
    /// </para>
    /// </summary>
    public class ParameterSyncRejectionTests
    {
        /// <summary>요청 자체가 서버 규칙에 맞지 않는다 — 다시 보내도 같은 답이 온다.</summary>
        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]            // 400 — 검증 실패 (길이 초과가 여기였다)
        [InlineData(HttpStatusCode.NotFound)]              // 404 — 없는 레시피·클라이언트
        [InlineData(HttpStatusCode.Conflict)]              // 409
        [InlineData(HttpStatusCode.RequestEntityTooLarge)] // 413
        [InlineData(HttpStatusCode.UnsupportedMediaType)]  // 415
        [InlineData(HttpStatusCode.UnprocessableEntity)]   // 422
        public void Client_errors_are_permanent(HttpStatusCode code)
            => Assert.True(ParameterSyncService.IsPermanentRejection(code));

        /// <summary>시간이 지나거나 상황이 바뀌면 통할 수 있다 — 버리면 데이터가 사라진다.</summary>
        [Theory]
        [InlineData(HttpStatusCode.RequestTimeout)]        // 408 — 그때만 느렸다
        [InlineData(HttpStatusCode.TooManyRequests)]       // 429 — 잠시 뒤에 다시
        [InlineData(HttpStatusCode.Unauthorized)]          // 401 — 토큰 재발급하면 통한다
        [InlineData(HttpStatusCode.Forbidden)]             // 403 — 권한 설정이 바뀔 수 있다
        [InlineData(HttpStatusCode.InternalServerError)]   // 500 — 서버 문제
        [InlineData(HttpStatusCode.BadGateway)]            // 502
        [InlineData(HttpStatusCode.ServiceUnavailable)]    // 503 — 배포 중
        [InlineData(HttpStatusCode.GatewayTimeout)]        // 504
        public void Recoverable_failures_are_retried(HttpStatusCode code)
            => Assert.False(ParameterSyncService.IsPermanentRejection(code));

        /// <summary>성공은 애초에 실패가 아니다.</summary>
        [Theory]
        [InlineData(HttpStatusCode.OK)]
        [InlineData(HttpStatusCode.Created)]
        [InlineData(HttpStatusCode.NoContent)]
        public void Success_is_not_a_rejection(HttpStatusCode code)
            => Assert.False(ParameterSyncService.IsPermanentRejection(code));
    }
}
