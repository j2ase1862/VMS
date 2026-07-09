using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using VMS.Camera.Configuration;
using Xunit;

namespace VMS.Core.Tests.Configuration
{
    /// <summary>
    /// AppDataPaths 의 인스턴스 해석 우선순위(--instance 인자 > BODA_VMS_INSTANCE 환경변수 > 기본)
    /// 와 경로/IPC 이름 파생 규칙 검증 — 다중 인스턴스(한 PC 여러 라인) 지원의 기반.
    ///
    /// 환경변수/정적 상태를 만지므로 Collection 으로 직렬화 + Dispose 에서 완전 복원.
    /// </summary>
    [Collection("AppDataPathsState")]
    public class AppDataPathsTests : IDisposable
    {
        private readonly string? _origEnvValue;

        public AppDataPathsTests()
        {
            _origEnvValue = Environment.GetEnvironmentVariable(AppDataPaths.EnvVarName);
            Environment.SetEnvironmentVariable(AppDataPaths.EnvVarName, null);
            AppDataPaths.ResetForTests();
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataPaths.EnvVarName, _origEnvValue);
            AppDataPaths.ResetForTests();
        }

        // ─── 해석 우선순위 ───────────────────────────────────────

        [Fact]
        public void Default_instance_when_no_arg_and_no_envvar()
        {
            AppDataPaths.Initialize(Array.Empty<string>());

            Assert.True(AppDataPaths.IsDefaultInstance);
            Assert.Equal(string.Empty, AppDataPaths.InstanceName);
            Assert.EndsWith(AppDataPaths.RootFolderName, AppDataPaths.Root);
        }

        [Fact]
        public void EnvVar_resolves_instance_without_Initialize()
        {
            Environment.SetEnvironmentVariable(AppDataPaths.EnvVarName, "line2");

            Assert.Equal("line2", AppDataPaths.InstanceName);
            Assert.False(AppDataPaths.IsDefaultInstance);
        }

        [Fact]
        public void Instance_arg_overrides_envvar()
        {
            Environment.SetEnvironmentVariable(AppDataPaths.EnvVarName, "from-env");
            AppDataPaths.Initialize(new[] { "--instance", "from-arg" });

            Assert.Equal("from-arg", AppDataPaths.InstanceName);
        }

        [Fact]
        public void Instance_arg_is_case_insensitive_flag()
        {
            AppDataPaths.Initialize(new[] { "--INSTANCE", "line3" });

            Assert.Equal("line3", AppDataPaths.InstanceName);
        }

        [Fact]
        public void Initialize_exports_envvar_for_child_processes()
        {
            AppDataPaths.Initialize(new[] { "--instance", "line2" });

            Assert.Equal("line2", Environment.GetEnvironmentVariable(AppDataPaths.EnvVarName));
        }

        [Fact]
        public void Initialize_default_leaves_envvar_unset()
        {
            // 인자도 env var 도 없으면 기본 인스턴스 — env var 는 계속 미설정 상태여야
            // 자식 프로세스도 기본 인스턴스로 뜬다.
            AppDataPaths.Initialize(Array.Empty<string>());

            Assert.Null(Environment.GetEnvironmentVariable(AppDataPaths.EnvVarName));
        }

        // ─── 이름 검증 ───────────────────────────────────────────

        [Theory]
        [InlineData("한글")]
        [InlineData("bad name")]
        [InlineData("slash/name")]
        [InlineData("dot.name")]
        [InlineData("123456789012345678901234567890123")] // 33자
        public void Invalid_instance_name_throws(string bad)
        {
            Assert.Throws<ArgumentException>(
                () => AppDataPaths.Initialize(new[] { "--instance", bad }));
        }

        [Theory]
        [InlineData("line2")]
        [InlineData("LINE_2")]
        [InlineData("a")]
        [InlineData("op102-north")]
        public void Valid_instance_name_accepted(string good)
        {
            AppDataPaths.Initialize(new[] { "--instance", good });

            Assert.Equal(good, AppDataPaths.InstanceName);
        }

        // ─── 경로 파생 ───────────────────────────────────────────

        [Fact]
        public void Named_instance_root_nests_under_instances_folder()
        {
            AppDataPaths.Initialize(new[] { "--instance", "line2" });

            var expectedSuffix = Path.Combine(AppDataPaths.RootFolderName, "instances", "line2");
            Assert.EndsWith(expectedSuffix, AppDataPaths.Root);
        }

        [Fact]
        public void GetPath_composes_relative_parts()
        {
            AppDataPaths.Initialize(Array.Empty<string>());

            Assert.Equal(Path.Combine(AppDataPaths.Root, "Recipes", "r1.json"),
                AppDataPaths.GetPath("Recipes", "r1.json"));
            Assert.Equal(Path.Combine(AppDataPaths.Root, "system_config.json"),
                AppDataPaths.SystemConfigFile);
        }

        // ─── IPC 이름 파생 ───────────────────────────────────────

        [Fact]
        public void QualifyIpcName_keeps_legacy_name_for_default_instance()
        {
            AppDataPaths.Initialize(Array.Empty<string>());

            Assert.Equal(@"Local\X_Mutex", AppDataPaths.QualifyIpcName(@"Local\X_Mutex"));
        }

        [Fact]
        public void QualifyIpcName_appends_instance_suffix()
        {
            AppDataPaths.Initialize(new[] { "--instance", "line2" });

            Assert.Equal(@"Local\X_Mutex.line2", AppDataPaths.QualifyIpcName(@"Local\X_Mutex"));
        }
    }

    /// <summary>
    /// 아키텍처 가드 — "BODA VISION AI" 리터럴로 경로를 조립하는 코드가
    /// AppDataPaths 밖에 다시 생기면 실패한다. 리터럴 경로는 다중 인스턴스 격리를
    /// 깨뜨리므로(두 인스턴스가 같은 파일을 공유) 반드시 AppDataPaths 를 경유할 것.
    /// (ManualPresetConsistencyTests 와 동일한 CallerFilePath 기반 소스 스캔 패턴)
    /// </summary>
    public class AppDataPathsSourceScanTests
    {
        private static string RepoRoot([CallerFilePath] string? thisFile = null)
        {
            var dir = Path.GetDirectoryName(thisFile)!;
            // VMS.Core.Tests/Configuration/ → ../../ = repo root
            return Path.GetFullPath(Path.Combine(dir, "..", ".."));
        }

        [Fact]
        public void No_hardcoded_root_folder_literal_outside_AppDataPaths()
        {
            var root = RepoRoot();
            var offenders = Directory.EnumerateDirectories(root, "VMS*")
                .Where(d => !Path.GetFileName(d).EndsWith(".Tests", StringComparison.Ordinal))
                .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                         && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                         && !f.EndsWith(Path.Combine("Configuration", "AppDataPaths.cs"), StringComparison.Ordinal))
                .Where(f => File.ReadAllText(f).Contains("\"BODA VISION AI\""))
                .Select(f => Path.GetRelativePath(root, f))
                .ToList();

            Assert.True(offenders.Count == 0,
                "\"BODA VISION AI\" 리터럴 경로 조립 발견 — AppDataPaths.Root/GetPath 를 사용하세요:\n  "
                + string.Join("\n  ", offenders));
        }
    }
}
