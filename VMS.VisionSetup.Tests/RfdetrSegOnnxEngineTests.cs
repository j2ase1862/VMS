using System;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// RF-DETR 세그멘테이션(Apache-2.0) 규약 — RfdetrSegOnnxEngine 파서 검증.
    ///
    /// <para>
    /// 실제 가중치 없이 규약만 흉내 낸 초소형 ONNX 스텁(상수 출력)을 base64 로 내장한다.
    /// 질의 3개, 열 3개(클래스 2 + 배경 1), 마스크 8×8.
    /// </para>
    /// <list type="bullet">
    /// <item>q0 dets [0.25, 0.5, 0.2, 0.4] · labels [2.0, 0.9, -5.0] → class0 0.881, class1 0.711
    ///   (한 질의가 두 클래스에서 문턱을 넘는 경우)</item>
    /// <item>q1 dets [0.75, 0.5, 0.2, 0.4] · labels [-5.0, 1.0, -5.0] → class1 0.731</item>
    /// <item>q2 dets [0.50, 0.5, 0.1, 0.1] · labels [-5.0, -5.0, 5.0] → 배경만 높다 → 빠져야 한다</item>
    /// </list>
    /// <para>masks: q0 왼쪽 절반 양수 · q1 오른쪽 절반 양수 · q2 전부 음수.</para>
    /// <para>스텁은 onnx.helper 로 Constant 3개를 만드는 것이 전부라 언제든 다시 만들 수 있다.</para>
    /// </summary>
    public class RfdetrSegOnnxEngineTests
    {
        private const string StubWithMeta =
            "CAg6ggkKYBIEZGV0cyIIQ29uc3RhbnQqTgoFdmFsdWUqQggBCAMIBBABIjAAAIA+AAAAP83MTD7NzMw+AABAPwAAAD/NzEw+zczMPgAAAD8AAAA/zczMPc3MzD1CBmRldHNfdqABBApYEgZsYWJlbHMiCENvbnN0YW50KkQKBXZhbHVlKjgIAQgDCAMQASIkAAAAQGZmZj8AAKDAAACgwAAAgD8AAKDAAACgwAAAoMAAAKBAQghsYWJlbHNfdqABBAq3BhIFbWFza3MiCENvbnN0YW50KqMGCgV2YWx1ZSqWBggBCAMICAgIEAEigAYAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMBCB21hc2tzX3agAQQSDnJmZGV0cnNlZ19zdHViWh8KBWlucHV0EhYKFAgBEhAKAggBCgIIAwoCCEAKAghAYhoKBGRldHMSEgoQCAESDAoCCAEKAggDCgIIBGIcCgZsYWJlbHMSEgoQCAESDAoCCAEKAggDCgIIA2IfCgVtYXNrcxIWChQIARIQCgIIAQoCCAMKAggICgIICEIECgAQDXIiCgVuYW1lcxIZezA6ICdzY3JhdGNoJywgMTogJ2RlbnQnfXIRCgVpbWdzehIIWzY0LCA2NF1yGQoMbW9kZWxfZm9ybWF0EglyZmRldHJzZWdyGAoTYmFja2dyb3VuZF9jbGFzc19pZBIBMg==";

        private const string StubNoMeta =
            "CAg6ggkKYBIEZGV0cyIIQ29uc3RhbnQqTgoFdmFsdWUqQggBCAMIBBABIjAAAIA+AAAAP83MTD7NzMw+AABAPwAAAD/NzEw+zczMPgAAAD8AAAA/zczMPc3MzD1CBmRldHNfdqABBApYEgZsYWJlbHMiCENvbnN0YW50KkQKBXZhbHVlKjgIAQgDCAMQASIkAAAAQGZmZj8AAKDAAACgwAAAgD8AAKDAAACgwAAAoMAAAKBAQghsYWJlbHNfdqABBAq3BhIFbWFza3MiCENvbnN0YW50KqMGCgV2YWx1ZSqWBggBCAMICAgIEAEigAYAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMBAAADAQAAAwEAAAMBAAADAwAAAwMAAAMDAAADAwAAAwEAAAMBAAADAQAAAwEAAAMDAAADAwAAAwMAAAMDAAADAQAAAwEAAAMBAAADAQAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMAAAMDAAADAwAAAwMBCB21hc2tzX3agAQQSDnJmZGV0cnNlZ19zdHViWh8KBWlucHV0EhYKFAgBEhAKAggBCgIIAwoCCEAKAghAYhoKBGRldHMSEgoQCAESDAoCCAEKAggDCgIIBGIcCgZsYWJlbHMSEgoQCAESDAoCCAEKAggDCgIIA2IfCgVtYXNrcxIWChQIARIQCgIIAQoCCAMKAggICgIICEIECgAQDQ==";

        private sealed class TempModel : IDisposable
        {
            public string Path { get; }
            public TempModel(string base64, string tag)
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vms-{tag}-{Guid.NewGuid():N}.onnx");
                File.WriteAllBytes(Path, Convert.FromBase64String(base64));
            }
            public void Dispose()
            {
                try { File.Delete(Path); } catch { /* best effort */ }
            }
        }

        // 800(w) × 400(h) — 늘려 맞추는 리사이즈라 비율이 달라도 좌표 환산이 맞아야 한다
        private static Mat MakeImage() => new Mat(400, 800, MatType.CV_8UC3, Scalar.All(30));

        [Fact]
        public void Reads_class_names_and_background_from_metadata()
        {
            using var model = new TempModel(StubWithMeta, "rfseg-meta");
            var engine = new RfdetrSegOnnxEngine(model.Path);

            Assert.Equal(new[] { "scratch", "dent" }, engine.LastClassNames);
            Assert.Equal(2, engine.BackgroundClassId);
        }

        /// <summary>
        /// 배경 열이 최고 점수인 질의는 물체가 아니다. 이 열을 빼지 않으면
        /// 아무것도 없는 자리에 인스턴스가 생기고, 그 마스크는 전부 음수라 0 픽셀짜리로 나온다.
        /// </summary>
        [Fact]
        public void Drops_the_background_query()
        {
            using var model = new TempModel(StubWithMeta, "rfseg-bg");
            var engine = new RfdetrSegOnnxEngine(model.Path);
            using var image = MakeImage();

            var instances = engine.Segment(image, 64, confThr: 0.5f, maxInstances: 100);
            try
            {
                Assert.All(instances, i => Assert.InRange(i.ClassId, 0, 1));
                // q2 의 박스는 w=0.1 → 80px. 남아 있으면 그 크기로 드러난다.
                Assert.DoesNotContain(instances, i => i.Box.Width < 100);
            }
            finally { foreach (var i in instances) i.Mask?.Dispose(); }
        }

        /// <summary>
        /// 한 질의가 두 클래스에서 문턱을 넘으면 둘 다 나와야 한다.
        /// 질의마다 최고 하나만 고르면 낮은 쪽이 조용히 사라진다 (rf-detr 이 같은 이유로 argmax 를 버렸다).
        /// </summary>
        [Fact]
        public void Keeps_both_classes_of_one_query()
        {
            using var model = new TempModel(StubWithMeta, "rfseg-multi");
            var engine = new RfdetrSegOnnxEngine(model.Path);
            using var image = MakeImage();

            var instances = engine.Segment(image, 64, confThr: 0.5f, maxInstances: 100);
            try
            {
                // q0 → class0(0.881) + class1(0.711), q1 → class1(0.731)
                Assert.Equal(3, instances.Count);
                Assert.Equal(2, instances.Count(i => i.ClassId == 1));
                Assert.Single(instances.Where(i => i.ClassId == 0));

                // 점수 순으로 내려온다
                Assert.True(instances[0].Score >= instances[1].Score);
                Assert.InRange(instances[0].Score, 0.87f, 0.89f);
            }
            finally { foreach (var i in instances) i.Mask?.Dispose(); }
        }

        /// <summary>
        /// dets 는 0~1 정규화 cxcywh 이고 전처리가 늘려 맞추는 리사이즈라,
        /// 원본 크기를 그대로 곱하면 픽셀 좌표가 된다 (레터박스 여백을 빼면 안 된다).
        /// </summary>
        [Fact]
        public void Maps_normalized_boxes_onto_the_original_image()
        {
            using var model = new TempModel(StubWithMeta, "rfseg-box");
            var engine = new RfdetrSegOnnxEngine(model.Path);
            using var image = MakeImage();   // 800 × 400

            var instances = engine.Segment(image, 64, confThr: 0.5f, maxInstances: 100);
            try
            {
                var first = instances.First(i => i.ClassId == 0);   // q0
                // cx .25 → 200px, w .2 → 160px  ⇒ x 200±80 = 120..280
                // cy .5  → 200px, h .4 → 160px  ⇒ y 200±80 = 120..280
                Assert.Equal(120, first.Box.X);
                Assert.Equal(120, first.Box.Y);
                Assert.Equal(160, first.Box.Width);
                Assert.Equal(160, first.Box.Height);
            }
            finally { foreach (var i in instances) i.Mask?.Dispose(); }
        }

        /// <summary>
        /// masks 는 로짓이다. 0 에서 자른다 — 시그모이드 0.5 와 같은 자리다.
        /// q0 의 마스크는 왼쪽 절반만 양수인데, q0 박스(x 120..280)는 이미지 왼쪽 절반(0..400) 안에
        /// 온전히 들어가므로 박스 전체가 채워져야 한다.
        /// </summary>
        [Fact]
        public void Thresholds_mask_logits_at_zero()
        {
            using var model = new TempModel(StubWithMeta, "rfseg-mask");
            var engine = new RfdetrSegOnnxEngine(model.Path);
            using var image = MakeImage();

            var instances = engine.Segment(image, 64, confThr: 0.5f, maxInstances: 100);
            try
            {
                var q0 = instances.First(i => i.ClassId == 0);
                Assert.NotNull(q0.Mask);
                Assert.Equal(q0.Box.Width, q0.Mask!.Width);
                Assert.Equal(q0.Box.Height, q0.Mask.Height);
                Assert.Equal((long)q0.Box.Width * q0.Box.Height, q0.MaskPixelCount);
            }
            finally { foreach (var i in instances) i.Mask?.Dispose(); }
        }

        /// <summary>문턱을 올리면 낮은 점수부터 빠지고, 상한을 걸면 최고 점수만 남는다.</summary>
        [Fact]
        public void Threshold_and_cap_limit_what_comes_back()
        {
            using var model = new TempModel(StubWithMeta, "rfseg-thr");
            var engine = new RfdetrSegOnnxEngine(model.Path);
            using var image = MakeImage();

            var high = engine.Segment(image, 64, confThr: 0.8f, maxInstances: 100);
            var capped = engine.Segment(image, 64, confThr: 0.5f, maxInstances: 1);
            try
            {
                Assert.Single(high);                    // class0 0.881 만 남는다
                Assert.Equal(0, high[0].ClassId);
                Assert.Single(capped);                  // 상한 1 → 최고 점수 하나
                Assert.Equal(0, capped[0].ClassId);
            }
            finally
            {
                foreach (var i in high) i.Mask?.Dispose();
                foreach (var i in capped) i.Mask?.Dispose();
            }
        }

        /// <summary>
        /// 메타데이터가 없으면 마지막 열을 배경으로 본다 — rf-detr 이 클래스 수 + 1 로 머리를 만들고
        /// 마지막 칸을 배경으로 쓰기 때문이다. 클래스 이름은 없으니 null 로 남는다.
        /// </summary>
        [Fact]
        public void Falls_back_to_the_last_column_without_metadata()
        {
            using var model = new TempModel(StubNoMeta, "rfseg-nometa");
            var engine = new RfdetrSegOnnxEngine(model.Path);
            using var image = MakeImage();

            var instances = engine.Segment(image, 64, confThr: 0.5f, maxInstances: 100);
            try
            {
                Assert.Equal(3, instances.Count);
                Assert.All(instances, i => Assert.InRange(i.ClassId, 0, 1));
                Assert.All(instances, i => Assert.Null(i.ClassName));
            }
            finally { foreach (var i in instances) i.Mask?.Dispose(); }
        }
    }
}
