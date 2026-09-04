using OpenCvSharp;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.PatternMatching;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// 실증 PC 보고 (2026-09-04): 객체와 Train ROI 를 옮겨 재학습한 뒤 같은 이미지로 Run 하면
    /// Match Align Δ 가 0 이어야 하는데 다른 값이 나옴. 툴 계층에서 재학습 → 원점 갱신 → Δ=0 을 재현.
    /// </summary>
    [Collection("VisionServiceSingleton")]
    public class FeatureMatchRetrainOriginTests
    {
        private static Mat Scene(Point center, int size = 40)
        {
            var img = new Mat(300, 300, MatType.CV_8UC1, Scalar.Black);
            Cv2.Rectangle(img, new Rect(center.X - size / 2, center.Y - size / 2, size, size), Scalar.White, -1);
            return img;
        }

        /// <summary>MainViewModel.TrainPattern 과 동일 경로: ROI 크롭(GetAlignedROIImage) → TrainPattern(SelectedModel).</summary>
        private static void TrainLikeUi(FeatureMatchTool tool, Mat scene)
        {
            using var crop = tool.GetAlignedROIImage(scene);
            Assert.True(tool.TrainPattern(crop, tool.SelectedModel), "학습 실패");
        }

        private static (double dx, double dy, double dth) RunAlign(FeatureMatchTool fm, Mat scene)
        {
            var match = fm.Execute(scene);
            Assert.True(match.Success, match.Message);
            var align = new MatchAlignTool { DrawOverlay = false, SourceMatchResult = match };
            var r = align.Execute(scene);
            Assert.True(r.Success, r.Message);
            var res = ((double)r.Data["DeltaX"], (double)r.Data["DeltaY"], (double)r.Data["DeltaTheta"]);
            match.ReleaseMats(); r.ReleaseMats();
            return res;
        }

        [Fact]
        public void Retrain_AfterMovingObjectAndRoi_AlignDeltaIsZero()
        {
            var fm = new FeatureMatchTool { UseROI = true, ROI = new Rect(60, 60, 80, 80) };

            using var sceneA = Scene(new Point(100, 100));
            TrainLikeUi(fm, sceneA);
            Assert.Equal(100, fm.SelectedModel!.TrainedCenterX, 3);
            Assert.Single(fm.Models);
            var (ax, ay, ath) = RunAlign(fm, sceneA);
            Assert.InRange(ax, -2, 2); Assert.InRange(ay, -2, 2); Assert.InRange(ath, -2, 2);

            // 객체·ROI 이동 후 같은 모델 재학습 (ROI 가 객체 중심에서 살짝 비껴도 Δ 는 0 이어야 함)
            fm.ROI = new Rect(155, 135, 80, 80);
            using var sceneB = Scene(new Point(200, 180));
            TrainLikeUi(fm, sceneB);
            Assert.Single(fm.Models);                               // 새 모델이 아니라 재학습
            Assert.Equal(195, fm.SelectedModel!.TrainedCenterX, 3);  // ROI 중심으로 갱신
            Assert.Equal(175, fm.SelectedModel!.TrainedCenterY, 3);

            var (bx, by, bth) = RunAlign(fm, sceneB);
            Assert.InRange(bx, -2, 2);
            Assert.InRange(by, -2, 2);
            Assert.InRange(bth, -2, 2);
        }

        private static Mat RotatedScene(Point2f center, double angleDeg, int w = 60, int h = 30)
        {
            var img = new Mat(300, 300, MatType.CV_8UC1, Scalar.Black);
            var rr = new RotatedRect(center, new Size2f(w, h), (float)angleDeg);
            var pts = rr.Points();
            Cv2.FillConvexPoly(img, System.Array.ConvertAll(pts, p => new Point((int)System.Math.Round(p.X), (int)System.Math.Round(p.Y))), Scalar.White);
            return img;
        }

        /// <summary>회전 ROI(RectangleAffine 규약: ROICenterX/Y + ROIAngle) 로 학습 → 같은 장면 Run → Δ 는 0 이어야 한다.</summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(20.0)]
        [InlineData(-35.0)]
        public void Train_WithRotatedRoi_SameSceneAlignDeltaIsZero(double angle)
        {
            var fm = new FeatureMatchTool { UseROI = true };
            fm.ROICenterX = 150; fm.ROICenterY = 140; fm.ROIAngle = angle;
            fm.ROI = new Rect(150 - 40, 140 - 25, 80, 50);   // MainViewModel 규약: 중심 기준 축 정렬 Rect

            using var scene = RotatedScene(new Point2f(150, 140), angle);
            TrainLikeUi(fm, scene);

            var (dx, dy, dth) = RunAlign(fm, scene);
            Assert.True(System.Math.Abs(dx) <= 2 && System.Math.Abs(dy) <= 2 && System.Math.Abs(dth) <= 2,
                $"angle={angle}: ΔX={dx:F1} ΔY={dy:F1} Δθ={dth:F1} (TrainedCenter=({fm.SelectedModel!.TrainedCenterX},{fm.SelectedModel.TrainedCenterY}))");
        }

        [Fact]
        public void Retrain_WithRotatedRoi_UpdatesTrainedAngle_AndAlignDeltaIsZero()
        {
            // 실증 시나리오: 축 정렬 ROI 로 학습 → 부품·ROI 를 옮기고 ROI 를 회전시켜 재학습 → 같은 장면 Run
            var fm = new FeatureMatchTool { UseROI = true, ROI = new Rect(60, 60, 80, 50) };
            using var sceneA = RotatedScene(new Point2f(100, 85), 0);
            TrainLikeUi(fm, sceneA);
            Assert.Equal(0, fm.SelectedModel!.TrainedAngle);

            fm.ROICenterX = 200; fm.ROICenterY = 180; fm.ROIAngle = 25;
            fm.ROI = new Rect(200 - 40, 180 - 25, 80, 50);
            using var sceneB = RotatedScene(new Point2f(200, 180), 25);
            TrainLikeUi(fm, sceneB);

            Assert.Single(fm.Models);
            Assert.Equal(25, fm.SelectedModel!.TrainedAngle, 3);
            Assert.Equal(200, fm.SelectedModel.TrainedCenterX, 3);

            var (dx, dy, dth) = RunAlign(fm, sceneB);
            Assert.InRange(dx, -2, 2); Assert.InRange(dy, -2, 2); Assert.InRange(dth, -2, 2);
        }

        [Fact]
        public void Retrain_PreserveCenter_KeepsAngleAndCenter()
        {
            // "기준 이미지 유지"(KeepReference) 경로 = preserveTrainedCenter — 원점과 각도 모두 원 학습 값 유지
            var fm = new FeatureMatchTool { UseROI = true };
            fm.ROICenterX = 150; fm.ROICenterY = 140; fm.ROIAngle = 20;
            fm.ROI = new Rect(110, 115, 80, 50);
            using var sceneA = RotatedScene(new Point2f(150, 140), 20);
            TrainLikeUi(fm, sceneA);
            var model = fm.SelectedModel!;

            fm.ROICenterX = 220; fm.ROICenterY = 200; fm.ROIAngle = -30;
            fm.ROI = new Rect(180, 175, 80, 50);
            using var sceneB = RotatedScene(new Point2f(220, 200), -30);
            using var crop = fm.GetAlignedROIImage(sceneB);
            Assert.True(fm.TrainPattern(crop, model, preserveTrainedCenter: true));

            Assert.Equal(150, model.TrainedCenterX, 3);
            Assert.Equal(140, model.TrainedCenterY, 3);
            Assert.Equal(20, model.TrainedAngle, 3);
        }

        [Fact]
        public void Serialize_RoundTrip_RestoresTrainedOrigin_EvenIfRoiMovedBeforeSave()
        {
            // 복원은 템플릿 재학습이라 "현재 ROI" 로 중심을 재계산하던 잠재 결함 — 명시 저장값으로 고정돼야 한다
            var fm = new FeatureMatchTool { UseROI = true, RetrainOriginMode = RetrainOriginMode.KeepReference };
            fm.ROICenterX = 150; fm.ROICenterY = 140; fm.ROIAngle = 20;
            fm.ROI = new Rect(110, 115, 80, 50);
            using var scene = RotatedScene(new Point2f(150, 140), 20);
            TrainLikeUi(fm, scene);

            // 학습 후 ROI 를 옮기고 저장
            fm.ROI = new Rect(10, 10, 80, 50); fm.ROICenterX = 50; fm.ROICenterY = 35; fm.ROIAngle = 0;
            var config = ToolSerializer.SerializeTool(fm);
            var json = System.Text.Json.JsonSerializer.Serialize(config);
            var back = System.Text.Json.JsonSerializer.Deserialize<ToolConfig>(json);
            var restored = Assert.IsType<FeatureMatchTool>(ToolSerializer.DeserializeTool(back!));

            var m = Assert.Single(restored.Models);
            Assert.Equal(150, m.TrainedCenterX, 3);
            Assert.Equal(140, m.TrainedCenterY, 3);
            Assert.Equal(20, m.TrainedAngle, 3);
            Assert.Equal(RetrainOriginMode.KeepReference, restored.RetrainOriginMode);
        }

        [Fact]
        public void Deserialize_LegacyModelWithoutOrigin_FallsBackToRoiRecompute()
        {
            var fm = new FeatureMatchTool { UseROI = true, ROI = new Rect(60, 60, 80, 80) };
            using var scene = Scene(new Point(100, 100));
            TrainLikeUi(fm, scene);
            var config = ToolSerializer.SerializeTool(fm);
            var models = (System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>)config.Parameters["Models"];
            models[0].Remove("TrainedCenterX"); models[0].Remove("TrainedCenterY"); models[0].Remove("TrainedAngle");
            config.Parameters.Remove("RetrainOriginMode");

            var restored = Assert.IsType<FeatureMatchTool>(ToolSerializer.DeserializeTool(config));
            var m = Assert.Single(restored.Models);
            Assert.Equal(100, m.TrainedCenterX, 3);   // 구 레시피: 종전대로 ROI 기준 재계산
            Assert.Equal(0, m.TrainedAngle);
            Assert.Equal(RetrainOriginMode.UseCurrentImage, restored.RetrainOriginMode);
        }
    }
}
