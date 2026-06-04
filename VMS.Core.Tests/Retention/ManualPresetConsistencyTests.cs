using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VMS.Core.Retention;
using VMS.Core.Security;
using Xunit;

namespace VMS.Core.Tests.Retention
{
    /// <summary>
    /// docs/gs/gs_compliance_overview_v1.0.md §5.10 의 "프리셋 값" 표와
    /// VMS.Core.Retention.RetentionPresets 코드 상수의 일치성 자동 검증 (검토사항 P4).
    ///
    /// 한쪽만 수정하면 빌드 실패 → 매뉴얼/코드 drift 사전 차단.
    /// 매뉴얼 표 형식:
    /// | Conservative (규제) | 730 | 90 | 60 | 1825 | 1095 | 730 | 180 |
    /// | Standard (GS 권장)  | 365 | 30 | 30 | 1095 | 730  | 365 | 90  |
    /// | Minimal (소형)      | 90  | 7  | 14 | 365  | 180  | 90  | 30  |
    /// 컬럼 순서: Audit | AutoBackup | UploadQueue | Sec/User/Cfg | Auth/Authz/Recipe | Seq/Insp | System
    /// </summary>
    public class ManualPresetConsistencyTests
    {
        private static readonly string ManualPath = ResolveManualPath();

        private static string ResolveManualPath([CallerFilePath] string? thisFile = null)
        {
            // 본 .cs 파일 위치 → repo root → docs/gs/...
            // CallerFilePath 는 컴파일 시점 절대 경로.
            var dir = Path.GetDirectoryName(thisFile)!;
            // VMS.Core.Tests/Retention/ → ../../ = repo root
            var repoRoot = Path.GetFullPath(Path.Combine(dir, "..", ".."));
            return Path.Combine(repoRoot, "docs", "gs", "gs_compliance_overview_v1.0.md");
        }

        [Fact]
        public void Manual_file_exists_at_expected_path()
        {
            Assert.True(File.Exists(ManualPath),
                $"매뉴얼 파일 위치 확인 — 예상 경로: {ManualPath}. " +
                "PR #121 docs reorg 이후 docs/gs/ 하위 위치 변경되었다면 본 테스트의 ResolveManualPath 갱신 필요.");
        }

        [Theory]
        [InlineData(RetentionPresets.Conservative)]
        [InlineData(RetentionPresets.Standard)]
        [InlineData(RetentionPresets.Minimal)]
        public void Manual_preset_row_matches_code_constants(string presetName)
        {
            Assert.True(File.Exists(ManualPath), $"매뉴얼 누락: {ManualPath}");
            var preset = RetentionPresets.All[presetName];

            var manualRow = FindManualPresetRow(presetName);
            Assert.NotNull(manualRow);

            // | Conservative (규제) | 730 | 90 | 60 | 1825 | 1095 | 730 | 180 |
            // 파이프 split → [0]=빈, [1]=이름, [2]=Audit, ..., [8]=System, [9]=빈
            var cells = manualRow!.Split('|', StringSplitOptions.None)
                .Select(c => c.Trim())
                .ToArray();
            Assert.True(cells.Length >= 10,
                $"매뉴얼 표 셀 수 부족 ({cells.Length}). 표 형식 변경되었다면 본 테스트 갱신 필요.");

            int audit       = ParseInt(cells[2]);
            int autoBackup  = ParseInt(cells[3]);
            int uploadQueue = ParseInt(cells[4]);
            int secUserCfg  = ParseInt(cells[5]);
            int authRecipe  = ParseInt(cells[6]);
            int seqInsp     = ParseInt(cells[7]);
            int system      = ParseInt(cells[8]);

            // 전역 3 항목 일치
            Assert.Equal(preset.Audit,       audit);
            Assert.Equal(preset.AutoBackup,  autoBackup);
            Assert.Equal(preset.UploadQueue, uploadQueue);

            // 카테고리 그룹별 일치 (매뉴얼 표는 그룹화 — 동일 그룹의 모든 카테고리가 같은 값)
            AssertCategoryGroup(preset, secUserCfg,
                AuditCategory.Security, AuditCategory.UserManagement, AuditCategory.Configuration);
            AssertCategoryGroup(preset, authRecipe,
                AuditCategory.Authentication, AuditCategory.Authorization, AuditCategory.RecipeChange);
            AssertCategoryGroup(preset, seqInsp,
                AuditCategory.SequenceControl, AuditCategory.Inspection);
            AssertCategoryGroup(preset, system, AuditCategory.System);
        }

        private static void AssertCategoryGroup(RetentionPreset preset, int expected, params AuditCategory[] categories)
        {
            foreach (var cat in categories)
            {
                Assert.True(preset.Categories.TryGetValue(cat, out var actual),
                    $"카테고리 {cat} 가 RetentionPresets.{preset.Name} 에 정의되지 않음");
                Assert.Equal(expected, actual);
            }
        }

        private static string? FindManualPresetRow(string presetName)
        {
            // 라인 단위로 읽어 CRLF / LF 차이 무관하게 처리.
            // 표 행 형식: "| Conservative (규제) | 730 | 90 | ... |"
            var lines = File.ReadAllLines(ManualPath);
            var pattern = $@"^\|\s*{Regex.Escape(presetName)}\b";
            foreach (var line in lines)
            {
                if (Regex.IsMatch(line, pattern) && line.Count(c => c == '|') >= 9)
                    return line.TrimEnd();
            }
            return null;
        }

        private static int ParseInt(string s)
        {
            if (!int.TryParse(s, out var n))
                throw new FormatException($"매뉴얼 표 셀이 정수가 아님: '{s}'");
            return n;
        }
    }
}
