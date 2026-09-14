using System;
using VMS.Core.Security;
using VMS.Services.ImageUpload;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// 검사 이미지 업로드 채널의 보안 정책 검증.
    ///
    /// <para>이 채널은 생산 이미지 전체와 <c>X-API-Key</c> 를 실어 나르는데, 예전에는 Web 연동
    /// 채널 중 유일하게 맨 <c>HttpClient</c> 를 써서 <see cref="InsecureUrlGuard"/> 도
    /// <see cref="HttpClientPolicy"/> 도 타지 않았다. 그래서 Production 모드 + 원격 http 구성에서
    /// 다른 채널은 전부 부팅 시 차단되는데 이미지 업로드만 조용히 평문으로 나갔다 — 감사 로그에도
    /// 남지 않아 사람이 알 방법이 없었다.</para>
    ///
    /// <para><see cref="SecurityOptions.Current"/> 정적 상태를 바꾸므로 같은 이름의 컬렉션으로
    /// 직렬화한다.</para>
    /// </summary>
    [Collection("SecurityOptionsState")]
    public class ImageUploadServiceGuardTests
    {
        [Theory]
        [InlineData("http://192.168.0.10:5292")]
        [InlineData("http://boda-vms.com")]
        public void Ctor_RemoteHttp_Production_Throws(string url)
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                var ex = Assert.Throws<InvalidOperationException>(
                    () => new ImageUploadService(url, clientIndex: 1, clientApiKey: "k"));
                Assert.Contains(nameof(ImageUploadService), ex.Message);
            }
            finally { SecurityOptions.Current = original; }
        }

        [Theory]
        [InlineData("http://localhost:5292")]
        [InlineData("http://127.0.0.1:5292")]
        public void Ctor_LoopbackHttp_Production_NoThrow(string url)
        {
            var original = SecurityOptions.Current;
            try
            {
                // 오프라인 단일 PC 구성 (Web 이 같은 PC 의 Kestrel HTTP) — loopback 예외로 허용.
                SecurityOptions.Current = SecurityOptions.Production;
                using var svc = new ImageUploadService(url, clientIndex: 1);
            }
            finally { SecurityOptions.Current = original; }
        }

        [Fact]
        public void Ctor_Https_Production_NoThrow()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                using var svc = new ImageUploadService("https://boda-vms.com", clientIndex: 1);
            }
            finally { SecurityOptions.Current = original; }
        }

        /// <summary>Web 미연동(주소 비어 있음)이면 그냥 꺼진 상태로 만들어져야 한다 — 예외 금지.</summary>
        [Fact]
        public void Ctor_EmptyUrl_Production_NoThrow()
        {
            var original = SecurityOptions.Current;
            try
            {
                SecurityOptions.Current = SecurityOptions.Production;
                using var svc = new ImageUploadService("", clientIndex: 1);
            }
            finally { SecurityOptions.Current = original; }
        }
    }
}
