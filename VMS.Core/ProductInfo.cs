using System;
using System.Reflection;

namespace VMS.Core
{
    /// <summary>
    /// 제품명 · 버전의 단일 원천 — 설치 프로그램 · 각 프로그램 화면 · 매뉴얼 · 제출 문서가 같은 표기를 써야 한다
    /// (GS 인증: 신청 제품명 · 버전이 모든 곳에서 동일). 버전은 Directory.Build.props 의 Version 이
    /// 실행 파일의 InformationalVersion 으로 들어온 값 — 솔루션 전 프로젝트가 같은 값을 상속한다.
    /// 순수 정적 클래스(VMS.Core 의존 없음) — VMS.Core 를 참조하지 않는 AppSetup 은 Linked Compile 하되
    /// internal 로 둔다(PRODUCTINFO_INTERNAL). VMS 가 AppSetup 과 VMS.Core 를 함께 참조하므로
    /// 둘 다 public 이면 VMS 쪽에서 같은 이름의 형식이 두 개가 된다.
    /// </summary>
#if PRODUCTINFO_INTERNAL
    internal
#else
    public
#endif
    static class ProductInfo
    {
        public const string Name = "BODA VMS";

        /// <summary>예: 1.48.0 (빌드 메타데이터 "+커밋" 은 뗀다).</summary>
        public static string Version { get; } = ReadVersion();

        /// <summary>예: BODA VMS v1.48.0</summary>
        public static string DisplayName => $"{Name} v{Version}";

        /// <summary>제품명 변경 전 기본값 — 기존 system_config.json 의 applicationName 에 남아 있다.</summary>
        public const string LegacyName = "BODA Vision System";

        /// <summary>
        /// system_config.json 의 applicationName(화면 제목 · Web 하트비트 swName) — 비었거나 옛 기본값이면
        /// 현재 제품명으로 본다. 사용자가 바꿔 둔 이름(라인명 등)은 그대로.
        /// </summary>
        public static string NormalizeApplicationName(string? configured) =>
            string.IsNullOrWhiteSpace(configured) || configured.Trim() == LegacyName ? Name : configured;

        private static string ReadVersion()
        {
            var asm = Assembly.GetEntryAssembly() ?? typeof(ProductInfo).Assembly;
            var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0];
            var v = asm.GetName().Version;
            return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";
        }
    }
}
