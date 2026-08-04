using System;
using System.Collections.Generic;
using System.Linq;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.DeepLearning;
using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 레시피 예제 템플릿 카탈로그 — 자주 쓰는 툴 체인의 단일 정의처.
    /// C# 팩토리로 유지하는 이유: 툴 파라미터가 진화해도 컴파일러가 동기화를 강제하고,
    /// 직렬화 왕복 없이 항상 현재 툴 구현과 일치한다.
    /// 다이어그램 PNG(Resources/Templates/{Id}.png)는 이 카탈로그로부터 생성·동봉된다.
    /// </summary>
    public static class RecipeTemplateCatalog
    {
        public const string Category3D = "3D";
        public const string CategoryDeepLearning = "Deep Learning";
        public const string Category2D = "2D 측정";
        public const string CategoryId = "식별";

        // 워크스페이스 캔버스 배치 상수 — 노드 MinWidth 150(+여백) 기준
        public const double NodeSpacingX = 190;
        public const double RowSpacingY = 110;
        public const double StartX = 30;
        public const double StartY = 30;

        private const string Badge3DCamera = "3D 카메라 필요";
        private const string BadgeOnnxModel = "ONNX 모델 필요";
        private const string BadgeColorImage = "컬러 이미지 필요";

        public static IReadOnlyList<RecipeTemplate> Templates { get; } = Build();

        private static List<RecipeTemplate> Build() => new()
        {
            // ── 3D ──
            new RecipeTemplate
            {
                Id = "pc-count-basic",
                Title = "3D 객체 카운트·치수 (기본)",
                Category = Category3D,
                Description = "높이 범위로 부품 영역을 잘라(Height Slicer) 점군을 크롭한 뒤 " +
                              "클러스터로 개수·치수를 잰다. Slicer 의 MinZ/MaxZ 를 부품 높이 " +
                              "범위(mm)에 맞게 조정할 것. 치수는 카메라 intrinsics 로 mm 자동 환산.",
                Prerequisites = new[] { Badge3DCamera },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "HeightSlicerTool" },
                    new TemplateToolSpec { ToolType = "PointCloudMaskCropTool" },
                    new TemplateToolSpec
                    {
                        ToolType = "PointCloudClusterTool",
                        Configure = t =>
                        {
                            var c = (PointCloudClusterTool)t;
                            c.OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal;
                            c.ScaleMode = PointCloudClusterTool.DimensionScaleMode.AutoFromCamera;
                        }
                    },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                },
            },
            new RecipeTemplate
            {
                Id = "pc-dl-detect",
                Title = "DL 세그 기반 3D 객체 검출",
                Category = Category3D,
                Description = "YOLOv8-seg 가 만든 인스턴스 마스크로 점군을 크롭해 객체별 " +
                              "개수·좌표·각도·치수를 잰다. YOLOv8-seg 설정에서 학습된 ONNX " +
                              "모델 경로를 지정해야 동작한다 (VMS.DeepLearning 에서 학습).",
                Prerequisites = new[] { Badge3DCamera, BadgeOnnxModel },
                Tools =
                {
                    new TemplateToolSpec
                    {
                        ToolType = "YoloSegTool",
                        Configure = t => ((YoloSegTool)t).OutputMaskImage = true,
                    },
                    new TemplateToolSpec { ToolType = "PointCloudMaskCropTool" },
                    new TemplateToolSpec
                    {
                        ToolType = "PointCloudClusterTool",
                        Configure = t =>
                        {
                            var c = (PointCloudClusterTool)t;
                            c.OutputMode = PointCloudClusterTool.ClusterOutputMode.KeepOriginal;
                            c.ScaleMode = PointCloudClusterTool.DimensionScaleMode.AutoFromCamera;
                        }
                    },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                },
            },
            new RecipeTemplate
            {
                Id = "pc-deviation",
                Title = "3D 기준 형상 편차 검사",
                Category = Category3D,
                Description = "점군 노이즈를 정리(Filter)하고 기준 형상에 정합(Registration)한 뒤 " +
                              "편차(Deviation)를 검사한다. Registration/Deviation 설정에서 기준 " +
                              "점군(.vpc/STL)을 지정할 것.",
                Prerequisites = new[] { Badge3DCamera },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "PointCloudFilterTool" },
                    new TemplateToolSpec { ToolType = "PointCloudRegistrationTool" },
                    new TemplateToolSpec { ToolType = "PointCloudDeviationTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                },
            },

            // ── Deep Learning ──
            new RecipeTemplate
            {
                Id = "dl-detection",
                Title = "DL 객체 검출 판정",
                Category = CategoryDeepLearning,
                Description = "YOLO 객체 검출 결과를 Result 로 판정한다. Detection 설정에서 " +
                              "학습된 ONNX 모델 경로를 지정할 것 (VMS.DeepLearning 에서 학습).",
                Prerequisites = new[] { BadgeOnnxModel },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "DetectionTool" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Result },
                },
            },
            new RecipeTemplate
            {
                Id = "dl-anomaly",
                Title = "DL 이상 탐지 판정",
                Category = CategoryDeepLearning,
                Description = "이상 탐지(Anomaly) 결과를 Result 로 판정한다. 정상 이미지만으로 " +
                              "학습한 모델을 사용 — Anomaly 설정에서 모델 경로를 지정할 것.",
                Prerequisites = new[] { BadgeOnnxModel },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "AnomalyTool" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Result },
                },
            },

            // ── 2D 측정 ──
            new RecipeTemplate
            {
                Id = "2d-blob",
                Title = "Blob 검출",
                Category = Category2D,
                Description = "그레이스케일 → 이진화 → Blob 검출. Threshold 값과 Blob 의 " +
                              "면적 범위를 대상에 맞게 조정할 것.",
                Tools =
                {
                    new TemplateToolSpec { ToolType = "GrayscaleTool" },
                    new TemplateToolSpec { ToolType = "ThresholdTool" },
                    new TemplateToolSpec { ToolType = "BlobTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Image },
                },
            },
            new RecipeTemplate
            {
                Id = "2d-edge-distance",
                Title = "엣지 간 거리 측정",
                Category = Category2D,
                Description = "Caliper 2개로 엣지를 검출하고 Geometry 가 두 점 사이 거리를 " +
                              "계산해 기준값 ± 공차로 판정, Result 가 최종 OK/NG 를 낸다. " +
                              "각 Caliper 의 ROI 를 측정할 엣지 위에 배치하고, Geometry 의 " +
                              "Judgment 에 기준값(mm)·공차를 입력할 것. mm 판정에는 캘리브레이션 " +
                              "또는 스텝 Resolution(mm/px) 설정이 필요하다.",
                Tools =
                {
                    new TemplateToolSpec { ToolType = "CaliperTool", DisplayName = "Caliper A" },
                    new TemplateToolSpec { ToolType = "CaliperTool", DisplayName = "Caliper B" },
                    new TemplateToolSpec
                    {
                        ToolType = "GeometryTool",
                        // 판정 프리셋 — 기준값은 대상마다 다르므로 사용자가 설정 (기본 100mm ± 0.5)
                        Configure = t =>
                        {
                            var g = (VisionTools.Measurement.GeometryTool)t;
                            g.EnableJudgment = true;
                            g.JudgmentUnit = VisionTools.Measurement.GeometryJudgmentUnit.Mm;
                        },
                    },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 2, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 2, TargetIndex = 3, Type = ConnectionType.Result },
                },
            },
            new RecipeTemplate
            {
                Id = "2d-featurematch",
                Title = "패턴 매칭 (Feature Match)",
                Category = Category2D,
                Description = "학습된 패턴을 이미지에서 찾아 위치·각도·스코어를 얻고 Result 로 " +
                              "판정한다. Feature Match 설정에서 기준 패턴을 학습시킬 것.",
                Tools =
                {
                    new TemplateToolSpec { ToolType = "FeatureMatchTool" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Result },
                },
            },
            new RecipeTemplate
            {
                Id = "2d-color-blob",
                Title = "컬러 객체 검출",
                Category = Category2D,
                Description = "지정 색상 영역을 추출(Color Extract)해 Blob 으로 개수·위치를 " +
                              "얻는다. Color Extract 설정에서 대상 색상 범위를 지정할 것.",
                Prerequisites = new[] { BadgeColorImage },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "ColorExtractTool" },
                    new TemplateToolSpec { ToolType = "BlobTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                },
            },

            // ── 식별 ──
            new RecipeTemplate
            {
                Id = "2d-code-reader",
                Title = "코드 판독 (바코드/QR/DataMatrix)",
                Category = CategoryId,
                Description = "바코드·QR·DataMatrix 를 판독하고 Result 로 판정한다. " +
                              "판독 대상이 작으면 Code Reader 의 ROI 를 지정할 것.",
                Tools =
                {
                    new TemplateToolSpec { ToolType = "CodeReaderTool" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Result },
                },
            },
        };

        /// <summary>
        /// 템플릿 정의로부터 툴 인스턴스 생성 (파라미터 프리셋 적용).
        /// 알 수 없는 ToolType 이 있으면 예외 — 카탈로그 무결성 테스트가 전수 검증한다.
        /// </summary>
        public static List<VisionToolBase> CreateTools(RecipeTemplate template)
        {
            var tools = new List<VisionToolBase>(template.Tools.Count);
            foreach (var spec in template.Tools)
            {
                var tool = VisionService.CreateTool(spec.ToolType)
                    ?? throw new InvalidOperationException($"알 수 없는 ToolType: {spec.ToolType} (템플릿 {template.Id})");
                if (!string.IsNullOrEmpty(spec.DisplayName))
                    tool.Name = spec.DisplayName;
                spec.Configure?.Invoke(tool);
                tools.Add(tool);
            }
            return tools;
        }

        /// <summary>
        /// 새 체인의 캔버스 배치 좌표 계산 — 기존 툴과 겹치지 않도록 기존 노드들의
        /// 최하단 아래 새 행에 좌→우로 배치한다.
        /// </summary>
        public static List<(double X, double Y)> ComputeInsertPositions(
            IEnumerable<(double X, double Y)> existing, int count)
        {
            var list = existing.ToList();
            double y = list.Count == 0 ? StartY : list.Max(p => p.Y) + RowSpacingY;

            var positions = new List<(double, double)>(count);
            for (int i = 0; i < count; i++)
                positions.Add((StartX + i * NodeSpacingX, y));
            return positions;
        }
    }
}
