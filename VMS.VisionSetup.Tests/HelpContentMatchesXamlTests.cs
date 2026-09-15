using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 물음표(HelpIcon) 도움말이 <b>실제 설정</b>과 일치하는지 소스에서 검증한다.
    ///
    /// <para><b>왜 필요한가 (2026-09-15).</b> Image Rectify 의 도움말은 존재하지 않는 항목 3개
    /// (UseCalibrationFile · CalibrationFilePath · InterpolationMode)를 설명하고, 정작 화면에 있는
    /// ApplyHomography 는 비어 있었다 — 물음표를 눌러도 아무것도 안 떴고, 설명에 적힌 항목은
    /// 도구에 존재하지도 않았다. 도구가 바뀔 때 도움말만 뒤처져도 컴파일은 통과하므로
    /// 아무도 알아채지 못한다.</para>
    /// </summary>
    public class HelpContentMatchesXamlTests
    {
        private static readonly Regex ToolTypeAttr = new(@"ToolType\s*=\s*""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex ParamNameAttr = new(@"ParameterName\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        private static DirectoryInfo VisionSetupDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "VMS.VisionSetup");
                if (File.Exists(Path.Combine(candidate, "VMS.VisionSetup.csproj")))
                    return new DirectoryInfo(candidate);
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("VMS.VisionSetup 소스 폴더를 찾지 못했습니다.");
        }

        /// <summary>XAML 에서 (ToolType, ParameterName) 쌍 — 화면에서 물음표가 보이는 자리.</summary>
        private static List<(string Tool, string Param, string File, int Line)> HelpBindings()
        {
            var found = new List<(string, string, string, int)>();
            foreach (var xaml in VisionSetupDir().GetFiles("*.xaml", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (xaml.FullName.Contains($"{sep}obj{sep}") || xaml.FullName.Contains($"{sep}bin{sep}"))
                    continue;

                var text = File.ReadAllText(xaml.FullName);
                foreach (Match element in Regex.Matches(text, @"<[^<>]+>", RegexOptions.Singleline))
                {
                    var tool = ToolTypeAttr.Match(element.Value);
                    var param = ParamNameAttr.Match(element.Value);
                    if (!tool.Success || !param.Success) continue;
                    if (tool.Groups[1].Value.StartsWith("{") || param.Groups[1].Value.StartsWith("{")) continue;

                    var line = text.Take(element.Index).Count(c => c == '\n') + 1;
                    found.Add((tool.Groups[1].Value, param.Groups[1].Value, xaml.Name, line));
                }
            }
            return found;
        }

        /// <summary>도구 클래스 이름 → 공개 속성 이름 집합.</summary>
        private static Dictionary<string, HashSet<string>> ToolProperties()
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var t in typeof(VisionToolBase).Assembly.GetTypes())
            {
                if (!typeof(VisionToolBase).IsAssignableFrom(t) || t.IsAbstract) continue;
                map[t.Name] = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                               .Select(p => p.Name)
                               .ToHashSet(StringComparer.Ordinal);
            }
            return map;
        }

        /// <summary>
        /// 화면에 물음표는 있는데 설명이 비어 있는 자리 — 이미 존재하던 것들.
        ///
        /// <para>이 목록은 <b>늘어나면 안 된다</b>. 새 도구를 만들면서 도움말을 빼먹으면 여기 없는
        /// 항목이 생겨 테스트가 깨진다. 기존 항목을 채우면 이 목록에서 지우면 된다
        /// (남겨 두면 "채웠는데 목록에 있다" 로 깨지므로 자연히 정리된다).</para>
        /// </summary>
        private static readonly HashSet<string> KnownEmptyHelp = new(StringComparer.Ordinal)
        {
            // 2026-09-15: 21곳 전부 채웠다 (AnomalyTool · DetectionTool CLAHE/SAHI/Dot · PhotometricStereo).
            // 새 도구를 만들면서 도움말을 빼먹으면 여기 없는 항목이 생겨 테스트가 깨진다.
        };

        [Fact]
        public void No_new_help_icon_is_left_empty()
        {
            var newlyEmpty = new List<string>();
            foreach (var (tool, param, file, line) in HelpBindings())
            {
                if (!string.IsNullOrWhiteSpace(HelpContent.GetParameterHelp(tool, param))) continue;
                var key = $"{tool}.{param}";
                if (KnownEmptyHelp.Contains(key)) continue;
                newlyEmpty.Add($"{file}:{line}  {key}");
            }

            Assert.True(newlyEmpty.Count == 0,
                "화면에 물음표가 있는데 설명이 비어 있습니다 (눌러도 아무것도 안 뜹니다).\n"
                + "설명을 HelpContent 에 추가하거나, 기존 공백이면 KnownEmptyHelp 에 등록하세요:\n  "
                + string.Join("\n  ", newlyEmpty));
        }

        [Fact]
        public void Fixed_gaps_are_removed_from_the_known_list()
        {
            var stillEmpty = HelpBindings()
                .Where(b => string.IsNullOrWhiteSpace(HelpContent.GetParameterHelp(b.Tool, b.Param)))
                .Select(b => $"{b.Tool}.{b.Param}")
                .ToHashSet(StringComparer.Ordinal);

            var stale = KnownEmptyHelp.Where(k => !stillEmpty.Contains(k)).ToList();

            Assert.True(stale.Count == 0,
                "이미 채워졌거나 화면에서 사라진 항목이 KnownEmptyHelp 에 남아 있습니다 — 목록에서 지우세요:\n  "
                + string.Join("\n  ", stale));
        }

        /// <summary>
        /// 속성이 아니지만 <b>일부러</b> 넣은 개념 설명 — 도구 ROI 처럼 UI 동작을 설명하는 항목.
        /// </summary>
        private static readonly HashSet<string> IntentionalConceptHelp = new(StringComparer.Ordinal)
        {
            "PointCloudMaskCropTool.(도구 ROI)",
            "FeatureMatchTool.안정특징정제",
        };

        /// <summary>
        /// 이름이 바뀐 뒤 설명만 옛 이름으로 남은 것들 — Image Rectify 와 <b>같은 결함</b>이다.
        /// 해당 도구는 문서화된 항목이 전부 옛 이름이고 실제 항목은 설명이 비어 있다.
        ///
        /// <para>예: PlaneFitTool 의 실제 항목은 RansacIterations · RansacThreshold · SampleStride 인데
        /// 설명은 InlierThresholdMm · MaxIterations · MinInlierRatio 를 말하고 있다.</para>
        ///
        /// <para>고칠 때 이 목록에서 지우면 된다. <b>늘어나면 안 된다</b>.</para>
        /// </summary>
        private static readonly HashSet<string> KnownStaleHelp = new(StringComparer.Ordinal)
        {
            // 2026-09-15: PlaneFitTool · SegmentationTool · Geometry3DTool 의 낡은 설명은 전부 정정됐다.
            // 새로 발견되면 여기 등록하고 백로그에 올린다.
        };

        /// <summary>
        /// 도구에 <b>존재하지 않는 설정</b>을 설명하고 있지 않은지. 화면에 물음표로 걸려 있으면
        /// (속성이 아니어도) 의도적인 UI 개념 설명이므로 통과시킨다.
        /// </summary>
        [Fact]
        public void Help_does_not_describe_settings_the_tool_does_not_have()
        {
            var props = ToolProperties();
            var wired = HelpBindings()
                .Select(b => $"{b.Tool}.{b.Param}")
                .ToHashSet(StringComparer.Ordinal);

            var ghosts = new List<string>();
            foreach (var toolName in props.Keys)
            {
                var help = HelpContent.GetToolHelp(toolName);
                if (help?.Parameters == null) continue;

                foreach (var documented in help.Parameters.Keys)
                {
                    var key = $"{toolName}.{documented}";
                    if (props[toolName].Contains(documented)) continue;   // 실제 속성
                    if (wired.Contains(key)) continue;                   // 화면에 걸린 개념 설명
                    if (IntentionalConceptHelp.Contains(key)) continue;  // 일부러 넣은 개념 설명
                    if (KnownStaleHelp.Contains(key)) continue;          // 이미 알려진 낡은 설명
                    ghosts.Add(key);
                }
            }

            Assert.True(ghosts.Count == 0,
                "도구에 없는 설정을 설명하고 있습니다 (도구가 바뀌었는데 도움말만 남은 경우).\n"
                + "설명을 실제 항목 이름으로 고치거나, 기존 건이면 KnownStaleHelp 에 등록하세요:\n  "
                + string.Join("\n  ", ghosts));
        }

        /// <summary>2026-09-15 에 실제로 틀려 있던 자리 — 회귀 방지 고정 검사.</summary>
        [Fact]
        public void ImageRectify_documents_exactly_its_two_checkboxes()
        {
            var help = HelpContent.GetToolHelp("ImageRectifyTool");
            Assert.NotNull(help);
            Assert.NotNull(help!.Parameters);
            Assert.Equal(
                new[] { "ApplyHomography", "Undistort" },
                help.Parameters!.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

            // 어떤 캘리브레이션 방식에서만 동작하는지가 설명에 있어야 한다 —
            // 이걸 몰라서 "켰는데 아무 일도 안 일어난다" 가 된다.
            Assert.Contains("Checkerboard", help.Parameters!["Undistort"]);
            Assert.Contains("N-Point", help.Parameters!["ApplyHomography"]);
        }

        [Fact]
        public void CalibrationManager_window_has_help()
        {
            var help = HelpContent.GetToolHelp("CalibrationManager");
            Assert.NotNull(help);
            Assert.NotNull(help!.Parameters);
            // 이 창에서 가장 자주 틀리는 값 — 칸 수가 아니라 안쪽 교차점 수
            Assert.Contains("교차점", help.Parameters!["PatternCols"]);
            Assert.Contains("교차점", help.Parameters!["PatternRows"]);
        }
    }
}
