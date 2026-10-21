using System;
using System.IO;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// AnomalyOnnxEngine 출력 규약 — 점수 [1]/[1,1] 또는 이상 맵 [1,1,H,W] 만 받아들인다.
    ///
    /// 종전에는 출력이 하나뿐이면 모양을 보지 않고 첫 값을 점수로 써서, 분류 모델([1,N])을 지정해도
    /// 오류 없이 "이상 감지" 같은 그럴듯한 판정이 나왔고, 출력을 하나도 알아보지 못하면 점수 0 = 정상으로
    /// 합격시켰다 (2026-10-13 GS 결함 허용성 시험자료). 스텁은 입력 input[1,3,16,16] 의 초소형 그래프
    /// (생성: onnx.helper — ReduceMean · GlobalAveragePool · Identity).
    /// </summary>
    public class AnomalyModelContractTests
    {
        internal const string ScoreStub = "CAcSDXZtcy10ZXN0LXN0dWI6hAEKQQoFaW5wdXQSCnByZWRfc2NvcmUiClJlZHVjZU1lYW4qDwoEYXhlc0ABQAJAA6ABByoPCghrZWVwZGltcxgAoAECEgRzdHViWh8KBWlucHV0EhYKFAgBEhAKAggBCgIIAwoCCBAKAggQYhgKCnByZWRfc2NvcmUSCgoICAESBAoCCAFCBAoAEA0=";
        private const string MapStub = "CAcSDXZtcy10ZXN0LXN0dWI6jgEKPgoFaW5wdXQSC2Fub21hbHlfbWFwIgpSZWR1Y2VNZWFuKgsKBGF4ZXNAAaABByoPCghrZWVwZGltcxgBoAECEgRzdHViWh8KBWlucHV0EhYKFAgBEhAKAggBCgIIAwoCCBAKAggQYiUKC2Fub21hbHlfbWFwEhYKFAgBEhAKAggBCgIIAQoCCBAKAggQQgQKABAN";
        private const string ClassifierStub = "CAcSDXZtcy10ZXN0LXN0dWI6hwEKHwoFaW5wdXQSA2dhcCIRR2xvYmFsQXZlcmFnZVBvb2wKIwoDZ2FwEgZsb2dpdHMiB0ZsYXR0ZW4qCwoEYXhpcxgBoAECEgRzdHViWh8KBWlucHV0EhYKFAgBEhAKAggBCgIIAwoCCBAKAggQYhgKBmxvZ2l0cxIOCgwIARIICgIIAQoCCANCBAoAEA0=";
        private const string FeaturesStub = "CAcSDXZtcy10ZXN0LXN0dWI6aAobCgVpbnB1dBIIZmVhdHVyZXMiCElkZW50aXR5EgRzdHViWh8KBWlucHV0EhYKFAgBEhAKAggBCgIIAwoCCBAKAggQYiIKCGZlYXR1cmVzEhYKFAgBEhAKAggBCgIIAwoCCBAKAggQQgQKABAN";

        private sealed class TempModel : IDisposable
        {
            public string Path { get; }
            public TempModel(string base64, string tag)
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vms-anomaly-{tag}-{Guid.NewGuid():N}.onnx");
                File.WriteAllBytes(Path, Convert.FromBase64String(base64));
            }
            public void Dispose() { try { File.Delete(Path); } catch { } }
        }

        // 균일한 회색(128) 영상의 ImageNet 정규화 값을 채널 평균한 것 = 두 스텁(점수 · 맵)의 기대 점수
        private static readonly float Gray128Score = (float)(
            ((128 / 255.0 - 0.485) / 0.229 + (128 / 255.0 - 0.456) / 0.224 + (128 / 255.0 - 0.406) / 0.225) / 3.0);

        private static Mat Gray() => new(32, 32, MatType.CV_8UC3, new Scalar(128, 128, 128));

        [Fact]
        public void ScoreOutput_IsUsedAsScore()
        {
            using var m = new TempModel(ScoreStub, "score");
            using var engine = new AnomalyOnnxEngine(m.Path);
            using var img = Gray();
            Assert.Equal(Gray128Score, engine.Detect(img, 16).AnomalyScore, 3);
        }

        [Fact]
        public void MapOnlyOutput_ScoreIsMapMaximum()
        {
            using var m = new TempModel(MapStub, "map");
            using var engine = new AnomalyOnnxEngine(m.Path);
            using var img = Gray();
            var r = engine.Detect(img, 16);
            Assert.NotNull(r.AnomalyMap);
            Assert.Equal(Gray128Score, r.AnomalyScore, 3);
            r.AnomalyMap!.Dispose();
        }

        [Theory]
        [InlineData("Classifier")]
        [InlineData("Features")]
        public void UnrecognizedOutput_Throws(string kind)
        {
            using var m = new TempModel(kind == "Classifier" ? ClassifierStub : FeaturesStub, kind);
            using var engine = new AnomalyOnnxEngine(m.Path);
            using var img = Gray();
            var ex = Assert.Throws<InvalidOperationException>(() => engine.Detect(img, 16));
            Assert.Contains("규약", ex.Message);
        }

        [Fact]
        public void Tool_WithClassifierModel_FailsWithContractMessage_NotAnomalyVerdict()
        {
            using var m = new TempModel(ClassifierStub, "tool");
            var tool = new AnomalyTool { ModelPath = m.Path, InputSize = 16 };
            using var img = Gray();
            var r = tool.Execute(img);
            Assert.False(r.Success);
            Assert.Contains("규약", r.Message);
            Assert.False(r.Data.ContainsKey("AnomalyScore"));
        }
    }
}
