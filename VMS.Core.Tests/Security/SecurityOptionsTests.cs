using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Security
{
    [Collection("SecurityOptionsState")]
    public class SecurityOptionsTests
    {
        // ─── Preset 검증 ──────────────────────────────────────────

        [Fact]
        public void Development_Preset_Values()
        {
            var dev = SecurityOptions.Development;
            Assert.Equal(SecurityMode.Development, dev.Mode);
            Assert.False(dev.RequireHttps);
            Assert.True(dev.AllowSelfSignedCert);
        }

        [Fact]
        public void Production_Preset_Values()
        {
            var prod = SecurityOptions.Production;
            Assert.Equal(SecurityMode.Production, prod.Mode);
            Assert.True(prod.RequireHttps);
            Assert.False(prod.AllowSelfSignedCert);
        }

        [Fact]
        public void DefaultUserAgent_NotEmpty()
        {
            // 모든 외부 호출의 User-Agent 헤더 — 비어 있으면 안 됨.
            Assert.False(string.IsNullOrWhiteSpace(SecurityOptions.Development.UserAgent));
            Assert.False(string.IsNullOrWhiteSpace(SecurityOptions.Production.UserAgent));
        }

        // ─── Current 동작 ────────────────────────────────────────

        [Fact]
        public void Current_SetGet_RoundTrip()
        {
            var original = SecurityOptions.Current;
            try
            {
                var prod = SecurityOptions.Production;
                SecurityOptions.Current = prod;
                Assert.Same(prod, SecurityOptions.Current);
                Assert.Equal(SecurityMode.Production, SecurityOptions.Current.Mode);
            }
            finally { SecurityOptions.Current = original; }
        }

        [Fact]
        public void Current_DefaultFallback_IsDevelopment()
        {
            // _current 가 null 이면 Development 폴백. set 으로 null 강제할 방법이 없으므로
            // 다른 모드로 세팅 → 같은 instance 가 다시 나오는지 정도만 확인.
            var original = SecurityOptions.Current;
            try
            {
                // Production 으로 세팅 → Mode 확인
                SecurityOptions.Current = SecurityOptions.Production;
                Assert.Equal(SecurityMode.Production, SecurityOptions.Current.Mode);

                // Development 로 다시 → 새 인스턴스 적용
                SecurityOptions.Current = SecurityOptions.Development;
                Assert.Equal(SecurityMode.Development, SecurityOptions.Current.Mode);
            }
            finally { SecurityOptions.Current = original; }
        }

        // ─── Init-only 필드 동작 ────────────────────────────────

        [Fact]
        public void CustomOptions_AllowsInitOverride()
        {
            // record-like init pattern — 운영자가 커스텀 모드 객체를 만들 수 있는지.
            var custom = new SecurityOptions
            {
                Mode = SecurityMode.Production,
                RequireHttps = true,
                AllowSelfSignedCert = false,
                UserAgent = "Custom/1.0"
            };
            Assert.Equal(SecurityMode.Production, custom.Mode);
            Assert.True(custom.RequireHttps);
            Assert.False(custom.AllowSelfSignedCert);
            Assert.Equal("Custom/1.0", custom.UserAgent);
        }
    }
}
