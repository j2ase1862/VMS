using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using Xunit;
using Xunit.Abstractions;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 예제 템플릿 다이어그램 PNG 생성기 — 카탈로그(RecipeTemplateCatalog)로부터
    /// VMS.VisionSetup/Resources/Templates/{Id}.png 를 생성한다.
    /// 카탈로그를 수정하면 이 테스트를 다시 돌려 다이어그램을 재생성할 것:
    ///   dotnet test --filter TemplateDiagramGenerator
    /// 소스 트리에 쓰는 유틸리티라 CI 에서는 *Diagnostic* 필터로 제외된다.
    /// </summary>
    public class TemplateDiagramGeneratorDiagnosticTests
    {
        private readonly ITestOutputHelper _out;
        public TemplateDiagramGeneratorDiagnosticTests(ITestOutputHelper output) => _out = output;

        // 카드 배경과 동일 계열의 다크 테마
        private static readonly Scalar Bg = new(38, 38, 37);        // #252526 (BGR)
        private static readonly Scalar BoxFill = new(48, 45, 45);   // #2D2D30
        private static readonly Scalar BoxBorder = new(204, 122, 0); // #007ACC
        private static readonly Scalar TextColor = new(230, 230, 230);
        private static readonly Dictionary<ConnectionType, Scalar> ConnColor = new()
        {
            [ConnectionType.Image] = new Scalar(247, 195, 79),       // #4FC3F7
            [ConnectionType.Result] = new Scalar(132, 199, 129),     // #81C784
            [ConnectionType.Coordinates] = new Scalar(77, 183, 255), // #FFB74D
        };

        private const int BoxW = 200, BoxH = 56, ColGap = 90, RowGap = 24, Margin = 20;

        [Fact]
        public void Diagnostic_GenerateTemplateDiagrams()
        {
            // 테스트 실행 위치(bin\...)에서 소스 트리의 Resources\Templates 로 역산
            var outDir = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\..\VMS.VisionSetup\Resources\Templates"));
            Directory.CreateDirectory(outDir);

            foreach (var template in RecipeTemplateCatalog.Templates)
            {
                var path = Path.Combine(outDir, $"{template.Id}.png");
                using var img = Render(template);
                Cv2.ImWrite(path, img);
                _out.WriteLine($"생성: {path}");
                Assert.True(File.Exists(path));
            }
        }

        private static Mat Render(RecipeTemplate template)
        {
            // 다중 스텝 템플릿 — 스텝 체인을 좌→우로 이어 붙여 하나의 도식으로 렌더.
            // 스텝 경계는 인덱스/레벨 오프셋으로 분리하고 상단에 STEP 라벨을 단다.
            List<VMS.VisionSetup.Models.VisionToolBase> tools;
            List<TemplateConnectionSpec> connections;
            var stepLabelAtIndex = new Dictionary<int, string>();   // 스텝 첫 툴 인덱스 → 라벨

            if (template.Steps.Count > 0)
            {
                tools = new List<VMS.VisionSetup.Models.VisionToolBase>();
                connections = new List<TemplateConnectionSpec>();
                int offset = 0;
                for (int s = 0; s < template.Steps.Count; s++)
                {
                    var stepSpec = template.Steps[s];
                    stepLabelAtIndex[offset] = $"STEP {s + 1}";
                    tools.AddRange(RecipeTemplateCatalog.CreateTools(stepSpec, template.Id));
                    foreach (var c in stepSpec.Connections)
                        connections.Add(new TemplateConnectionSpec
                        {
                            SourceIndex = c.SourceIndex + offset,
                            TargetIndex = c.TargetIndex + offset,
                            Type = c.Type
                        });
                    // 다음 스텝은 이전 스텝 뒤 레벨에서 시작하도록 가짜 순서 연결이 아닌
                    // 레벨 오프셋으로 처리 — 아래 레벨 계산 후 보정한다.
                    offset = tools.Count;
                }
            }
            else
            {
                tools = RecipeTemplateCatalog.CreateTools(template);
                connections = template.Connections;
            }

            int n = tools.Count;

            // 레벨 = 소스로부터의 최장 경로 (연결 없는 툴은 0). 같은 레벨은 세로로 쌓음.
            var levels = new int[n];
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var c in connections)
                {
                    int want = levels[c.SourceIndex] + 1;
                    if (levels[c.TargetIndex] < want)
                    {
                        levels[c.TargetIndex] = want;
                        changed = true;
                    }
                }
            }

            // 다중 스텝 — 각 스텝 블록을 이전 스텝의 최대 레벨 뒤로 밀어 좌→우 배치
            if (stepLabelAtIndex.Count > 0)
            {
                var starts = stepLabelAtIndex.Keys.OrderBy(i => i).ToList();
                int levelBase = 0;
                for (int s = 0; s < starts.Count; s++)
                {
                    int from = starts[s];
                    int to = s + 1 < starts.Count ? starts[s + 1] : n;
                    int localMax = 0;
                    for (int i = from; i < to; i++)
                    {
                        levels[i] += levelBase;
                        localMax = Math.Max(localMax, levels[i]);
                    }
                    levelBase = localMax + 1;
                }
            }

            var rowInLevel = new int[n];
            var levelCounts = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                levelCounts.TryGetValue(levels[i], out var r);
                rowInLevel[i] = r;
                levelCounts[levels[i]] = r + 1;
            }

            int cols = levels.Max() + 1;
            int rows = levelCounts.Values.Max();
            int topExtra = stepLabelAtIndex.Count > 0 ? 26 : 0;   // STEP 라벨 공간
            int width = Margin * 2 + cols * BoxW + (cols - 1) * ColGap;
            int height = Margin * 2 + topExtra + rows * BoxH + (rows - 1) * RowGap;

            var img = new Mat(new Size(width, height), MatType.CV_8UC3, Bg);

            // 박스 좌표
            var rects = new Rect[n];
            for (int i = 0; i < n; i++)
            {
                int x = Margin + levels[i] * (BoxW + ColGap);
                int y = Margin + topExtra + rowInLevel[i] * (BoxH + RowGap);
                rects[i] = new Rect(x, y, BoxW, BoxH);
            }

            // STEP 라벨 (스텝 첫 툴 위)
            foreach (var (idx, label) in stepLabelAtIndex)
            {
                Cv2.PutText(img, label, new Point(rects[idx].X, Margin + 14),
                    HersheyFonts.HersheySimplex, 0.55, new Scalar(160, 200, 255), 1, LineTypes.AntiAlias);
            }

            // 연결선 (박스보다 먼저 그려 선이 박스 아래로)
            foreach (var c in connections)
            {
                var s = rects[c.SourceIndex];
                var t = rects[c.TargetIndex];
                var p1 = new Point(s.Right, s.Y + s.Height / 2);
                var p2 = new Point(t.X, t.Y + t.Height / 2);
                var color = ConnColor[c.Type];

                Cv2.ArrowedLine(img, p1, p2, color, 2, LineTypes.AntiAlias, tipLength: 0.12);
                var label = c.Type.ToString();
                var mid = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2 - 8);
                var size = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.42, 1, out _);
                Cv2.PutText(img, label, new Point(mid.X - size.Width / 2, mid.Y),
                    HersheyFonts.HersheySimplex, 0.42, color, 1, LineTypes.AntiAlias);
            }

            // 박스 + 툴 이름 (기본 이름은 영문 — OpenCV 한글 미지원)
            for (int i = 0; i < n; i++)
            {
                Cv2.Rectangle(img, rects[i], BoxFill, -1);
                Cv2.Rectangle(img, rects[i], BoxBorder, 2, LineTypes.AntiAlias);

                var name = tools[i].Name;
                double scale = 0.5;
                var size = Cv2.GetTextSize(name, HersheyFonts.HersheySimplex, scale, 1, out _);
                if (size.Width > BoxW - 14)
                    scale = scale * (BoxW - 14) / size.Width;
                size = Cv2.GetTextSize(name, HersheyFonts.HersheySimplex, scale, 1, out _);

                var org = new Point(
                    rects[i].X + (BoxW - size.Width) / 2,
                    rects[i].Y + (BoxH + size.Height) / 2);
                Cv2.PutText(img, name, org, HersheyFonts.HersheySimplex, scale,
                    TextColor, 1, LineTypes.AntiAlias);
            }

            return img;
        }
    }
}
