using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 도구 설정 화면(XAML)에 노출된 파라미터가 <c>ToolSerializer</c> 에도 있는지 스캔.
    ///
    /// <para>현장 보고(2026-09-22): Caliper 의 <b>Edge Selection</b>(SelectionMode)을 바꿔도
    /// 레시피를 다시 열면 기본값으로 돌아갔다. 설정 화면·뷰모델·도구에는 다 있는데
    /// 직렬화에만 빠져 있었고, 컴파일도 테스트도 통과했다. 같은 식으로 Caliper 7건,
    /// CircleFit 1건이 저장되지 않고 있었다.</para>
    ///
    /// <para><see cref="KnownNotSerialized"/> 는 "저장하지 않는 게 맞는" 항목의 기준선이다.
    /// 새 파라미터를 추가하면서 직렬화를 빠뜨리면 이 테스트가 실패한다 —
    /// 기준선에 넣기 전에 정말 저장 대상이 아닌지 먼저 따져 볼 것.</para>
    /// </summary>
    public class ToolSerializerCoversSettingsXamlTests
    {
        /// <summary>저장하지 않는 것이 의도된 파라미터 (이유를 반드시 적을 것).</summary>
        private static readonly Dictionary<string, string> KnownNotSerialized = new()
        {
            // 학습 시점 입력값 — 뷰모델에만 있고 도구 속성이 아니다. 학습 결과만 저장된다.
            ["OCVTool.KnownString"] = "학습 입력값 (도구 속성 아님)",

            // 이름만 다르게 저장되는 것들 (별칭)
            ["CircleFitTool.CenterPoint.X"] = "CenterPointX 로 저장",
            ["CircleFitTool.CenterPoint.Y"] = "CenterPointY 로 저장",
            ["FeatureMatchTool.TrainMask"] = "모델별 TrainMaskBase64 로 저장",
            ["ColorMatchTool.MeanL"] = "모델(ColorMatchModel) 항목으로 저장",
            ["ColorMatchTool.MeanA"] = "모델(ColorMatchModel) 항목으로 저장",
            ["ColorMatchTool.MeanB"] = "모델(ColorMatchModel) 항목으로 저장",
        };

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VMS.sln")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        [Fact]
        public void 설정_화면의_파라미터는_모두_직렬화된다()
        {
            var root = RepoRoot();
            var serializer = File.ReadAllText(Path.Combine(root, "VMS.VisionSetup", "Services", "ToolSerializer.cs"));

            var serialized = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(serializer, @"Parameters\[""([^""]+)""\]"))
                serialized.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(serializer, @"TryGetValue\(""([^""]+)"""))
                serialized.Add(m.Groups[1].Value);

            var settingsDir = Path.Combine(root, "VMS.VisionSetup", "Views", "ToolSettings", "Tools");
            var gaps = new List<string>();

            foreach (var file in Directory.GetFiles(settingsDir, "*Settings.xaml").OrderBy(f => f))
            {
                var toolType = Path.GetFileNameWithoutExtension(file).Replace("Settings", "");
                var xaml = File.ReadAllText(file);

                foreach (Match m in Regex.Matches(xaml, @"ParameterName=""([^""]+)"""))
                {
                    var name = m.Groups[1].Value;
                    if (serialized.Contains(name)) continue;
                    if (KnownNotSerialized.ContainsKey($"{toolType}.{name}")) continue;
                    gaps.Add($"{toolType}.{name}");
                }
            }

            Assert.True(gaps.Count == 0,
                "설정 화면에 있는데 ToolSerializer 에 없는 파라미터 — 저장·복원이 안 됩니다:" +
                Environment.NewLine + string.Join(Environment.NewLine, gaps.Distinct()));
        }

        [Fact]
        public void 기준선_항목은_실제로_설정_화면에_존재한다()
        {
            // 기준선이 낡아 실효를 잃지 않도록 — 없어진 파라미터가 남아 있으면 알려 준다.
            var settingsDir = Path.Combine(RepoRoot(), "VMS.VisionSetup", "Views", "ToolSettings", "Tools");
            var present = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in Directory.GetFiles(settingsDir, "*Settings.xaml"))
            {
                var toolType = Path.GetFileNameWithoutExtension(file).Replace("Settings", "");
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"ParameterName=""([^""]+)"""))
                    present.Add($"{toolType}.{m.Groups[1].Value}");
            }

            var stale = KnownNotSerialized.Keys.Where(k => !present.Contains(k)).ToList();
            Assert.True(stale.Count == 0,
                "기준선에 남아 있으나 설정 화면에는 없는 항목 — 지우세요: " + string.Join(", ", stale));
        }
    }
}
