using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using VMS.Models;
using VMS.Services;
using Xunit;

namespace VMS.Tests.Documentation
{
    /// <summary>
    /// docs/manuals/BODA-VMS-User-Manual.html §3.7 (id="vms-usergrade") 의
    /// UserGrade × UserPermission 매트릭스가 코드 진실 소스와 동기화되어 있는지 검증.
    /// - VMS.Models.UserPermission enum (행)
    /// - 헤더 = Operator / Engineer / Admin / local-admin
    /// - 각 셀 = UserService.HasPermission switch + LocalFallbackAllowed 결과
    /// 한쪽만 수정하면 빌드 실패 → 매뉴얼/코드 drift 사전 차단 (PR #135 후속, H 매뉴얼-상수 일치 확장).
    /// </summary>
    public class UserGradePermissionMatrixConsistencyTests
    {
        private const string SectionAnchor = "vms-usergrade";
        private static readonly string ManualPath = ResolveManualPath();
        private static readonly string[] ExpectedHeaders = { "Operator", "Engineer", "Admin", "local-admin" };

        private static string ResolveManualPath([CallerFilePath] string? thisFile = null)
        {
            // VMS.Tests/Documentation/ → ../../ = repo root
            var dir = Path.GetDirectoryName(thisFile)!;
            var repoRoot = Path.GetFullPath(Path.Combine(dir, "..", ".."));
            return Path.Combine(repoRoot, "docs", "manuals", "BODA-VMS-User-Manual.html");
        }

        [Fact]
        public void Manual_file_exists_at_expected_path()
        {
            Assert.True(File.Exists(ManualPath),
                $"매뉴얼 경로 확인 — 예상: {ManualPath}. 파일 위치 변경 시 ResolveManualPath 갱신 필요.");
        }

        [Fact]
        public void Manual_section_3_7_present()
        {
            var content = File.ReadAllText(ManualPath);
            Assert.Contains($"id=\"{SectionAnchor}\"", content);
        }

        [Fact]
        public void Manual_headers_match_expected()
        {
            var (headers, _) = ParseMatrix();
            Assert.Equal(ExpectedHeaders, headers);
        }

        [Fact]
        public void Manual_permission_rows_match_UserPermission_enum()
        {
            var (_, rows) = ParseMatrix();
            var manualPermissions = rows.Select(r => r.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var enumPermissions = Enum.GetNames<UserPermission>().OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.Equal(enumPermissions, manualPermissions);
        }

        [Theory]
        [MemberData(nameof(MatrixCells))]
        public void Manual_cell_matches_HasPermission_logic(string permName, string column, bool manualAllowed)
        {
            var perm = Enum.Parse<UserPermission>(permName);
            bool codeAllowed = column switch
            {
                "Operator"    => ExpectedAllow(UserGrade.Operator, perm, isLocalFallback: false),
                "Engineer"    => ExpectedAllow(UserGrade.Engineer, perm, isLocalFallback: false),
                "Admin"       => ExpectedAllow(UserGrade.Admin,    perm, isLocalFallback: false),
                "local-admin" => ExpectedAllow(UserGrade.Admin,    perm, isLocalFallback: true),
                _ => throw new InvalidOperationException($"unknown column: {column}")
            };
            Assert.Equal(manualAllowed, codeAllowed);
        }

        public static IEnumerable<object[]> MatrixCells()
        {
            var (headers, rows) = ParseMatrix();
            foreach (var row in rows)
                for (int i = 0; i < headers.Length && i < row.Cells.Length; i++)
                    yield return new object[] { row.Name, headers[i], row.Cells[i] };
        }

        // UserService.HasPermission 의 switch 미러. 분기 변경시 본 helper + 매뉴얼 §3.7 동시 갱신 필요 —
        // UserServiceIntegrationTests / UserServiceLocalFallbackTests 가 실제 동작 검증.
        private static bool ExpectedAllow(UserGrade grade, UserPermission permission, bool isLocalFallback)
        {
            if (isLocalFallback) return UserService.LocalFallbackAllowed.Contains(permission);
            return grade switch
            {
                UserGrade.Admin    => true,
                UserGrade.Engineer => permission != UserPermission.ManageUsers,
                UserGrade.Operator => permission == UserPermission.StartStop
                                   || permission == UserPermission.ViewStatistics,
                _ => false
            };
        }

        private static (string[] headers, (string Name, bool[] Cells)[] rows) ParseMatrix()
        {
            var content = File.ReadAllText(ManualPath);
            var sectionStart = content.IndexOf($"id=\"{SectionAnchor}\"", StringComparison.Ordinal);
            Assert.True(sectionStart >= 0, $"섹션 anchor 누락: {SectionAnchor}");
            var tableStart = content.IndexOf("<table>", sectionStart, StringComparison.Ordinal);
            Assert.True(tableStart >= 0, "§3.7 의 <table> 누락");
            var tableEnd = content.IndexOf("</table>", tableStart, StringComparison.Ordinal);
            var tableHtml = content.Substring(tableStart, tableEnd - tableStart);

            var trMatches = Regex.Matches(tableHtml, "<tr>(.*?)</tr>", RegexOptions.Singleline);
            Assert.True(trMatches.Count >= 2, $"표 행 수 부족 ({trMatches.Count})");

            // 헤더 (첫 행) — 첫 셀 "권한 ..." 건너뛰고 나머지가 등급 컬럼
            var headerCells = ExtractCells(trMatches[0].Groups[1].Value, "th");
            var headers = headerCells.Skip(1).Select(NormalizeHeader).ToArray();

            var rows = new List<(string Name, bool[] Cells)>();
            for (int i = 1; i < trMatches.Count; i++)
            {
                var cells = ExtractCells(trMatches[i].Groups[1].Value, "td");
                if (cells.Length < 2) continue;
                rows.Add((ExtractPermissionName(cells[0]),
                          cells.Skip(1).Select(c => c == "✓").ToArray()));
            }
            return (headers, rows.ToArray());
        }

        private static string[] ExtractCells(string trInner, string tag)
        {
            return Regex.Matches(trInner, $"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .ToArray();
        }

        private static string NormalizeHeader(string raw)
        {
            // "local-admin<br>(비상 폴백)" → "local-admin"
            var br = raw.IndexOf("<br>", StringComparison.OrdinalIgnoreCase);
            if (br > 0) raw = raw.Substring(0, br);
            return raw.Trim();
        }

        private static string ExtractPermissionName(string cell)
        {
            // "StartStop (AUTO RUN 시작/정지)" → "StartStop"
            var space = cell.IndexOf(' ');
            return space < 0 ? cell.Trim() : cell.Substring(0, space).Trim();
        }
    }
}
