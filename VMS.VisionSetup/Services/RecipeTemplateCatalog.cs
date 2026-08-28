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
        public const string CategoryAlign = "얼라인";

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
                Description = "그레이스케일 → 이진화 → Blob 검출 → Result 판정. Threshold 값과 " +
                              "Blob 의 면적 범위를 대상에 맞게 조정할 것. 기본 판정은 'Blob 1개 " +
                              "이상 = OK' — 개수·면적 판정이 필요하면 Blob 설정의 Judgment 사용.",
                Tools =
                {
                    new TemplateToolSpec { ToolType = "GrayscaleTool" },
                    new TemplateToolSpec { ToolType = "ThresholdTool" },
                    new TemplateToolSpec { ToolType = "BlobTool" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 2, TargetIndex = 3, Type = ConnectionType.Result },
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
                Description = "그레이 변환 후 학습된 패턴을 이미지에서 찾아 위치·각도·스코어를 " +
                              "얻고 Result 로 판정한다. Feature Match 는 8-bit Gray 입력만 " +
                              "받으므로 Grayscale 을 앞에 둔다 (이미 그레이면 통과). " +
                              "Feature Match 설정에서 기준 패턴을 학습시킬 것.",
                Tools =
                {
                    new TemplateToolSpec { ToolType = "GrayscaleTool" },
                    new TemplateToolSpec { ToolType = "FeatureMatchTool" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                },
            },
            new RecipeTemplate
            {
                Id = "2d-color-blob",
                Title = "컬러 객체 검출",
                Category = Category2D,
                Description = "지정 색상 영역을 추출(Color Extract)해 Blob 으로 개수·위치를 " +
                              "얻고 Result 로 판정한다. Color Extract 설정에서 대상 색상 범위를 " +
                              "지정할 것. 기본 판정은 'Blob 1개 이상 = OK'.",
                Prerequisites = new[] { BadgeColorImage },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "ColorExtractTool" },
                    new TemplateToolSpec { ToolType = "BlobTool" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                },
            },

            // ── 얼라인 ──
            new RecipeTemplate
            {
                Id = "2d-match-align",
                Title = "표준 2D 매치 얼라인 (ΔX/ΔY/Δθ)",
                Category = CategoryAlign,
                Description = "카메라 1대로 부품을 보며 기준(Origin) 포즈 대비 현재 부품의 " +
                              "변위량 ΔX/ΔY/Δθ 를 계산해 로봇/스테이지 보정에 쓰는 표준 얼라인 " +
                              "구성이다. ① Feature Match 에서 기준 부품의 패턴을 학습 → " +
                              "② Run 마다 Match Align 이 학습 중심 대비 Δ를 산출 → " +
                              "③ mm 변위는 캘리브레이션(또는 스텝 Resolution) 설정 시 자동, " +
                              "로봇 좌표 변위는 Match Align 의 Robot Transform(핸드아이 상위 " +
                              "2x2)을 켜면 RobotDX/DY/DTheta 로 출력된다.",
                Tools =
                {
                    new TemplateToolSpec { ToolType = "GrayscaleTool" },
                    new TemplateToolSpec { ToolType = "FeatureMatchTool" },
                    new TemplateToolSpec { ToolType = "MatchAlignTool" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 2, TargetIndex = 3, Type = ConnectionType.Result },
                },
            },

            new RecipeTemplate
            {
                Id = "2d-match-align-2pt",
                Title = "2점 매치 얼라인 (회전 정밀)",
                Category = CategoryAlign,
                Description = "부품 양단의 특징 2개(예: 두 모서리·두 홀)를 각각 Feature Match 로 " +
                              "찾아, 두 점을 잇는 벡터의 회전으로 Δθ 를, 두 점 중심의 이동으로 " +
                              "ΔX/ΔY 를 계산한다. 기저선이 길수록 1점 방식보다 각도 정밀도가 " +
                              "높다. Feature Match A/B 에 각각 패턴을 학습시킬 것 — 연결 순서가 " +
                              "1번/2번 포인트. ScaleRatio 가 1.0 에서 벗어나면 오검출 의심.",
                Tools =
                {
                    new TemplateToolSpec { ToolType = "GrayscaleTool" },
                    new TemplateToolSpec { ToolType = "FeatureMatchTool", DisplayName = "Feature Match A" },
                    new TemplateToolSpec { ToolType = "FeatureMatchTool", DisplayName = "Feature Match B" },
                    new TemplateToolSpec
                    {
                        ToolType = "MatchAlignTool",
                        Configure = t => ((VisionTools.PatternMatching.MatchAlignTool)t).Mode =
                            VisionTools.PatternMatching.MatchAlignMode.TwoPoint,
                    },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 2, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 3, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 2, TargetIndex = 3, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 3, TargetIndex = 4, Type = ConnectionType.Result },
                },
            },

            new RecipeTemplate
            {
                Id = "2d-multistep-align",
                Title = "2-스텝 얼라인 (카메라 이동, 대형 부품)",
                Category = CategoryAlign,
                Description = "부품이 한 FOV 에 다 들어오지 않을 때 — 카메라(로봇/스테이지)가 " +
                              "이동하며 스텝 A/B 에서 부품 양단의 특징을 하나씩 매칭하고, " +
                              "Multi-Step Align 이 두 포즈를 합쳐 ΔX/ΔY/Δθ 를 계산한다. " +
                              "선택 시 레시피에 스텝 2개가 새로 생성된다. 적용 후: ① 각 스텝 " +
                              "Feature Match 에 패턴 학습 ② Multi-Step Align 의 Baseline(두 촬영 " +
                              "위치 간 오프셋, mm 권장) 입력 ③ 기준 부품으로 A→B Run 후 " +
                              "[현재 두 점을 기준으로 등록].",
                Steps =
                {
                    new TemplateStepSpec
                    {
                        Title = "Align A",
                        Tools =
                        {
                            new TemplateToolSpec { ToolType = "GrayscaleTool" },
                            new TemplateToolSpec { ToolType = "FeatureMatchTool", DisplayName = "Feature Match A" },
                        },
                        Connections =
                        {
                            new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                        },
                    },
                    new TemplateStepSpec
                    {
                        Title = "Align B + 합산",
                        Tools =
                        {
                            new TemplateToolSpec { ToolType = "GrayscaleTool" },
                            new TemplateToolSpec { ToolType = "FeatureMatchTool", DisplayName = "Feature Match B" },
                            new TemplateToolSpec { ToolType = "MultiStepAlignTool" },
                            new TemplateToolSpec { ToolType = "ResultTool" },
                        },
                        Connections =
                        {
                            new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                            new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                            new TemplateConnectionSpec { SourceIndex = 2, TargetIndex = 3, Type = ConnectionType.Result },
                        },
                    },
                },
                // 생성된 실제 스텝/툴 Id 를 Multi-Step Align 소스로 배선
                ConfigureAcrossSteps = (steps, tools) =>
                {
                    var align = (VisionTools.PatternMatching.MultiStepAlignTool)tools[1][2];
                    align.SourceStepIdA = steps[0].Id;
                    align.SourceToolIdA = tools[0][1].Id;   // Feature Match A
                    align.SourceStepIdB = steps[1].Id;
                    align.SourceToolIdB = tools[1][1].Id;   // Feature Match B
                },
            },

            new RecipeTemplate
            {
                Id = "3d-registration-align",
                Title = "표준 3D 얼라인 (6DOF, 기준 형상 정합)",
                Category = CategoryAlign,
                Description = "2D 매치 얼라인의 3D 판 — 기준 형상(.vpc 스캔 또는 .stl CAD) 대비 " +
                              "현재 부품의 6축 변위 ΔX/ΔY/ΔZ(mm) + 회전 RX/RY/RZ(도)를 산출해 " +
                              "로봇 보정에 쓴다. Filter 로 배경·노이즈를 걷어낸 뒤 Registration 이 " +
                              "정합한다. 적용 후: ① 기준 부품을 촬영해 Registration 설정의 " +
                              "[현재 점군을 Reference 로 저장] (또는 CAD .stl 지정) ② Run 마다 " +
                              "Translation*/Rotation* 이 변위로 출력된다. 얼라인 용도라 점군을 " +
                              "덮어쓰지 않도록 Apply Transform 은 꺼둔 프리셋 — 정합된 점군을 " +
                              "후속 툴에 넘기려면 켤 것. 정합 품질은 Confidence·MeanError 로 확인.",
                Prerequisites = new[] { Badge3DCamera },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "PointCloudFilterTool" },
                    new TemplateToolSpec
                    {
                        ToolType = "PointCloudRegistrationTool",
                        // 얼라인은 "변위량 측정"이 목적 — 원본 점군을 정합 결과로 덮어쓰지 않는다
                        Configure = t => ((PointCloudRegistrationTool)t).ApplyTransformToSource = false,
                    },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                },
            },

            new RecipeTemplate
            {
                Id = "3d-plane-tilt-align",
                Title = "평면 틸트 얼라인 (기울기 보정)",
                Category = CategoryAlign,
                Description = "기준면(지그·정반)과 대상면(부품)에 각각 Plane Fit 의 ROI 를 올려 " +
                              "두 평면의 법선 사이각을 재고, 그 각도로 스테이지·척의 기울기를 " +
                              "보정한다. 전체 형상 정합이 필요 없는 레벨링·평행도 맞춤에 쓴다. " +
                              "적용 후: ① Plane Fit 기준/대상의 ROI 를 각 면 위에 배치 " +
                              "② 3D Geometry 가 AngleDeg(도)로 기울기를 출력 — 0°가 평행. " +
                              "3D Geometry 에는 공차 판정이 없으므로, 합불 판정이 필요하면 " +
                              "AngleDeg 를 PLC 매핑하거나 시퀀스에서 비교할 것. 면이 거칠면 " +
                              "Plane Fit 의 RANSAC 임계값을 키운다.",
                Prerequisites = new[] { Badge3DCamera },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "PlaneFitTool", DisplayName = "Plane Fit Ref" },
                    new TemplateToolSpec { ToolType = "PlaneFitTool", DisplayName = "Plane Fit Target" },
                    new TemplateToolSpec
                    {
                        ToolType = "Geometry3DTool",
                        Configure = t =>
                        {
                            var g = (VisionTools.Measurement.Geometry3DTool)t;
                            g.Operation = VisionTools.Measurement.Geometry3DOperation.PlaneToPlaneAngle;
                            // 평면 연산은 연결된 Plane Fit 두 개를 소스로 쓴다 (수동 점 입력 아님)
                            g.UseManualPoints = false;
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
                Id = "3d-hybrid-align",
                Title = "2D+3D 하이브리드 얼라인 (XYθ + 기울기)",
                Category = CategoryAlign,
                Description = "평면 내 위치·회전은 2D 매치 얼라인이 정밀하고, 높이·기울기는 3D 가 " +
                              "정확한 점을 합친 구성이다. Feature Match → Match Align 이 " +
                              "ΔX/ΔY/Δθ 를, Plane Fit 이 대상면의 법선(기울기)과 평면식 계수 " +
                              "PlaneD(높이 오프셋)를 낸다. 형상 정합(6DOF)보다 가볍고 빠르며, " +
                              "부품에 뚜렷한 2D 특징이 있고 안착면이 평면일 때 적합하다. " +
                              "적용 후: ① Feature Match 에 기준 부품 패턴 학습 ② Plane Fit 의 " +
                              "ROI 를 안착면 위에 배치 ③ mm 변위는 캘리브레이션(또는 스텝 " +
                              "Resolution) 설정 시 자동.",
                Prerequisites = new[] { Badge3DCamera },
                Tools =
                {
                    new TemplateToolSpec { ToolType = "GrayscaleTool" },
                    new TemplateToolSpec { ToolType = "FeatureMatchTool" },
                    new TemplateToolSpec { ToolType = "MatchAlignTool" },
                    new TemplateToolSpec { ToolType = "PlaneFitTool", DisplayName = "Plane Fit Tilt" },
                    new TemplateToolSpec { ToolType = "ResultTool" },
                },
                Connections =
                {
                    new TemplateConnectionSpec { SourceIndex = 0, TargetIndex = 1, Type = ConnectionType.Image },
                    new TemplateConnectionSpec { SourceIndex = 1, TargetIndex = 2, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 2, TargetIndex = 4, Type = ConnectionType.Result },
                    new TemplateConnectionSpec { SourceIndex = 3, TargetIndex = 4, Type = ConnectionType.Result },
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
            => CreateTools(template.Tools, template.Id);

        /// <summary>스텝 스펙의 툴 인스턴스 생성 (다중 스텝 템플릿용).</summary>
        public static List<VisionToolBase> CreateTools(TemplateStepSpec step, string templateId)
            => CreateTools(step.Tools, templateId);

        private static List<VisionToolBase> CreateTools(List<TemplateToolSpec> specs, string templateId)
        {
            var tools = new List<VisionToolBase>(specs.Count);
            foreach (var spec in specs)
            {
                var tool = VisionService.CreateTool(spec.ToolType)
                    ?? throw new InvalidOperationException($"알 수 없는 ToolType: {spec.ToolType} (템플릿 {templateId})");
                if (!string.IsNullOrEmpty(spec.DisplayName))
                    tool.Name = spec.DisplayName;
                spec.Configure?.Invoke(tool);
                tools.Add(tool);
            }
            return tools;
        }

        /// <summary>
        /// 다중 스텝 템플릿으로부터 레시피에 추가할 스텝들을 생성.
        /// 각 스텝의 툴을 배치·연결·직렬화해 InspectionStep.Tools 에 담는다.
        /// ConfigureAcrossSteps 훅은 직렬화 **전에** 호출 — 스텝 간 참조(예: MultiStepAlign
        /// 의 소스 스텝/툴 Id)를 생성된 실제 Id 로 배선할 수 있다.
        /// CameraId 는 호출자가 지정 (보통 현재 선택 스텝과 같은 카메라).
        /// </summary>
        public static List<(InspectionStep Step, List<VisionToolBase> Tools)> BuildSteps(
            RecipeTemplate template, string cameraId)
        {
            var built = new List<(InspectionStep, List<VisionToolBase>)>(template.Steps.Count);

            foreach (var stepSpec in template.Steps)
            {
                var step = new InspectionStep
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = stepSpec.Title,
                    CameraId = cameraId,
                    Exposure = 1000,
                    Gain = 1.0,
                    LightingChannel = 0,
                    LightingIntensity = 100,
                    Tools = new List<ToolConfig>(),
                    IsEnabled = true
                };

                var tools = CreateTools(stepSpec, template.Id);
                var positions = ComputeInsertPositions(Array.Empty<(double, double)>(), tools.Count);
                for (int i = 0; i < tools.Count; i++)
                {
                    tools[i].X = positions[i].X;
                    tools[i].Y = positions[i].Y;
                }

                built.Add((step, tools));
            }

            // 스텝 간 참조 배선 (직렬화 전 — 이후 직렬화가 배선된 값을 담는다)
            template.ConfigureAcrossSteps?.Invoke(
                built.Select(b => b.Item1).ToList(),
                built.Select(b => (IReadOnlyList<VisionToolBase>)b.Item2).ToList());

            // 직렬화 + 스텝 내부 연결 저장 (SaveWorkspaceToStep 과 동일 포맷)
            for (int s = 0; s < built.Count; s++)
            {
                var (step, tools) = built[s];
                var stepSpec = template.Steps[s];

                foreach (var tool in tools)
                    step.Tools.Add(ToolSerializer.SerializeTool(tool));

                foreach (var conn in stepSpec.Connections)
                {
                    var targetConfig = step.Tools[conn.TargetIndex];
                    targetConfig.Connections.Add(new ToolConnectionConfig
                    {
                        SourceToolId = step.Tools[conn.SourceIndex].Id,
                        ConnectionType = conn.Type.ToString()
                    });
                }
            }

            return built;
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
