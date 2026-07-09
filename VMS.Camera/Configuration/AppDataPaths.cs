using System;
using System.IO;
using System.Text.RegularExpressions;

namespace VMS.Camera.Configuration
{
    /// <summary>
    /// VMS 솔루션 전체의 AppData 경로 단일 제공자 — 다중 인스턴스(한 PC 여러 라인) 지원의 중심.
    ///
    /// 인스턴스 해석 우선순위:
    ///   1. 명령줄 인자 "--instance &lt;이름&gt;" (Initialize 호출 시)
    ///   2. 환경변수 BODA_VMS_INSTANCE — VMS 가 자식 프로세스(VisionSetup/AppSetup 등)를
    ///      실행할 때 자동 상속되므로, 실행 연계 지점마다 인자를 넘길 필요가 없다.
    ///   3. 기본 인스턴스 (이름 없음)
    ///
    /// 경로 규칙:
    ///   기본:   %LocalAppData%\BODA VISION AI\
    ///   명명:   %LocalAppData%\BODA VISION AI\instances\&lt;이름&gt;\
    /// 기본 인스턴스 경로는 기존과 바이트 단위로 동일 — 마이그레이션 불필요.
    ///
    /// 규칙: "BODA VISION AI" 문자열로 경로를 조립하는 코드는 이 클래스 밖에 있으면 안 된다
    /// (소스 스캔 테스트 AppDataPathsSourceScanTests 가 강제). 새 파일/폴더가 필요하면
    /// GetPath(...) 를 사용할 것.
    /// </summary>
    public static class AppDataPaths
    {
        /// <summary>자식 프로세스로 인스턴스를 상속시키는 환경변수 이름.</summary>
        public const string EnvVarName = "BODA_VMS_INSTANCE";

        /// <summary>루트 폴더 이름 — appDataOverride 매개변수를 받는 로더가 경로를 조립할 때 사용.</summary>
        public const string RootFolderName = "BODA VISION AI";
        private const string InstancesFolderName = "instances";
        private const string InstanceArgName = "--instance";

        /// <summary>인스턴스 이름 규칙 — 파일시스템/IPC 이름에 안전한 문자만.</summary>
        private static readonly Regex ValidInstanceName = new("^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

        private static readonly object _lock = new();
        private static string? _instanceName;   // null = 미해석, "" = 기본 인스턴스

        /// <summary>
        /// 앱 시작 시 1회 호출 — 명령줄 인자에서 --instance 를 해석하고,
        /// 자식 프로세스가 상속하도록 환경변수로 내보낸다.
        /// 호출하지 않으면(라이브러리 소비자/테스트) 환경변수 → 기본 순으로 지연 해석된다.
        /// </summary>
        /// <exception cref="ArgumentException">인스턴스 이름이 규칙에 어긋날 때.</exception>
        public static void Initialize(string[] commandLineArgs)
        {
            string? fromArgs = null;
            for (int i = 0; i < commandLineArgs.Length - 1; i++)
            {
                if (string.Equals(commandLineArgs[i], InstanceArgName, StringComparison.OrdinalIgnoreCase))
                {
                    fromArgs = commandLineArgs[i + 1];
                    break;
                }
            }

            lock (_lock)
            {
                var name = fromArgs ?? Environment.GetEnvironmentVariable(EnvVarName) ?? string.Empty;
                _instanceName = Validate(name);

                // 프로세스 환경변수로 export — Process.Start 로 뜨는 자식이 자동 상속.
                Environment.SetEnvironmentVariable(EnvVarName,
                    _instanceName.Length == 0 ? null : _instanceName);
            }
        }

        /// <summary>현재 인스턴스 이름. 기본 인스턴스는 빈 문자열.</summary>
        public static string InstanceName
        {
            get
            {
                lock (_lock)
                {
                    _instanceName ??= Validate(Environment.GetEnvironmentVariable(EnvVarName) ?? string.Empty);
                    return _instanceName;
                }
            }
        }

        /// <summary>기본 인스턴스(인자/환경변수 없음) 여부.</summary>
        public static bool IsDefaultInstance => InstanceName.Length == 0;

        /// <summary>
        /// 인스턴스의 AppData 루트. 모든 설정/데이터 파일은 여기서 파생된다.
        /// </summary>
        public static string Root
        {
            get
            {
                var baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    RootFolderName);
                return IsDefaultInstance
                    ? baseDir
                    : Path.Combine(baseDir, InstancesFolderName, InstanceName);
            }
        }

        /// <summary>Root 하위 경로 조립. 예: GetPath("audit"), GetPath("Recipes", "r1.json")</summary>
        public static string GetPath(params string[] relativeParts)
        {
            var path = Root;
            foreach (var part in relativeParts)
                path = Path.Combine(path, part);
            return path;
        }

        /// <summary>시스템 설정 파일 — 가장 빈번한 소비처라 별도 제공.</summary>
        public static string SystemConfigFile => GetPath("system_config.json");

        /// <summary>
        /// IPC 객체 이름(뮤텍스/이벤트/MMF)에 인스턴스 접미사를 붙인다.
        /// 기본 인스턴스는 기존 이름 그대로 유지 — 구버전 프로세스와의 호환.
        /// </summary>
        public static string QualifyIpcName(string baseName) =>
            IsDefaultInstance ? baseName : $"{baseName}.{InstanceName}";

        private static string Validate(string name)
        {
            if (name.Length == 0) return name;
            if (!ValidInstanceName.IsMatch(name))
                throw new ArgumentException(
                    $"인스턴스 이름 '{name}' 이 올바르지 않습니다. " +
                    "영문/숫자/하이픈/밑줄 1~32자만 사용할 수 있습니다. (예: --instance line2)");
            return name;
        }

        /// <summary>테스트 전용 — 해석 상태 초기화.</summary>
        internal static void ResetForTests()
        {
            lock (_lock) { _instanceName = null; }
        }
    }
}
