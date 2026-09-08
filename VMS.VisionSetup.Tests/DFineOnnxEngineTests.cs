using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// D-FINE(Apache-2.0) 검출 백본 도입 — ONNX 규약 판별 + DFineOnnxEngine 파서 검증.
    /// 실제 가중치 없이 검증하기 위해 D-FINE deploy / HF raw / YOLO 출력 구조를 흉내 낸
    /// 초소형 ONNX 스텁(상수 출력, 8 쿼리, 2 클래스)을 base64 로 내장한다.
    ///   deploy 스텁: boxes = 정규화 상수 × orig_target_sizes(w,h,w,h) → 크기 입력 반영 여부까지 검증
    ///     q0: class1 0.9 [0.1,0.2,0.5,0.6] · q1: class0 0.6 [0.5,0.5,0.9,0.9] · q2: class1 0.5 (q0 근사 중복, NMS 대상)
    ///   raw 스텁: logits(sigmoid 전) + pred_boxes(cxcywh) 로 동일 박스
    /// 생성 스크립트: onnx.helper 로 Cast→Gather→Unsqueeze→Mul 그래프 (세션 없이 재생성 가능).
    /// </summary>
    public class DFineOnnxEngineTests
    {
        private const string DeployWithMeta =
            "CAg61AYKLQoRb3JpZ190YXJnZXRfc2l6ZXMSB3NpemVzX2YiBENhc3QqCQoCdG8YAaABAgpMEgRnaWR4IghDb25zdGFudCo6CgV2YWx1ZSouCAQQB0IGZ2lkeF92SiABAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAKABBAoqCgdzaXplc19mCgRnaWR4EgR3aHdoIgZHYXRoZXIqCwoEYXhpcxgBoAECCjISA3VheCIIQ29uc3RhbnQqIQoFdmFsdWUqFQgBEAdCBXVheF92SggBAAAAAAAAAKABBAodCgR3aHdoCgN1YXgSBXdod2gzIglVbnNxdWVlemUKvwESCmJveGVzX25vcm0iCENvbnN0YW50KqYBCgV2YWx1ZSqZAQgBCAgIBBABQgxib3hlc19ub3JtX3ZKgAHNzMw9zcxMPgAAAD+amRk/AAAAPwAAAD9mZmY/ZmZmP65H4T09Clc+XI8CP/YoHD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKABBAofCgpib3hlc19ub3JtCgV3aHdoMxIFYm94ZXMiA011bApyEgZsYWJlbHMiCENvbnN0YW50Kl4KBXZhbHVlKlIIAQgIEAdCCGxhYmVsc192SkABAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAoAEEClISBnNjb3JlcyIIQ29uc3RhbnQqPgoFdmFsdWUqMggBCAgQAUIIc2NvcmVzX3ZKIGZmZj+amRk/AAAAPwAAAAAAAAAAAAAAAAAAAAAAAAAAoAEEEhFkZmluZV9kZXBsb3lfc3R1YloiCgZpbWFnZXMSGAoWCAESEgoCCAEKAggDCgMIgAUKAwiABVojChFvcmlnX3RhcmdldF9zaXplcxIOCgwIBxIICgIIAQoCCAJiGAoGbGFiZWxzEg4KDAgHEggKAggBCgIICGIbCgVib3hlcxISChAIARIMCgIIAQoCCAgKAggEYhgKBnNjb3JlcxIOCgwIARIICgIIAQoCCAhCBAoAEA1yIQoFbmFtZXMSGHswOiAnZ29vZCcsIDE6ICdkZWZlY3QnfXITCgVpbWdzehIKWzY0MCwgNjQwXXIVCgxtb2RlbF9mb3JtYXQSBWRmaW5l";

        private const string DeployNoMeta =
            "CAg61AYKLQoRb3JpZ190YXJnZXRfc2l6ZXMSB3NpemVzX2YiBENhc3QqCQoCdG8YAaABAgpMEgRnaWR4IghDb25zdGFudCo6CgV2YWx1ZSouCAQQB0IGZ2lkeF92SiABAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAKABBAoqCgdzaXplc19mCgRnaWR4EgR3aHdoIgZHYXRoZXIqCwoEYXhpcxgBoAECCjISA3VheCIIQ29uc3RhbnQqIQoFdmFsdWUqFQgBEAdCBXVheF92SggBAAAAAAAAAKABBAodCgR3aHdoCgN1YXgSBXdod2gzIglVbnNxdWVlemUKvwESCmJveGVzX25vcm0iCENvbnN0YW50KqYBCgV2YWx1ZSqZAQgBCAgIBBABQgxib3hlc19ub3JtX3ZKgAHNzMw9zcxMPgAAAD+amRk/AAAAPwAAAD9mZmY/ZmZmP65H4T09Clc+XI8CP/YoHD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKABBAofCgpib3hlc19ub3JtCgV3aHdoMxIFYm94ZXMiA011bApyEgZsYWJlbHMiCENvbnN0YW50Kl4KBXZhbHVlKlIIAQgIEAdCCGxhYmVsc192SkABAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAoAEEClISBnNjb3JlcyIIQ29uc3RhbnQqPgoFdmFsdWUqMggBCAgQAUIIc2NvcmVzX3ZKIGZmZj+amRk/AAAAPwAAAAAAAAAAAAAAAAAAAAAAAAAAoAEEEhFkZmluZV9kZXBsb3lfc3R1YloiCgZpbWFnZXMSGAoWCAESEgoCCAEKAggDCgMIgAUKAwiABVojChFvcmlnX3RhcmdldF9zaXplcxIOCgwIBxIICgIIAQoCCAJiGAoGbGFiZWxzEg4KDAgHEggKAggBCgIICGIbCgVib3hlcxISChAIARIMCgIIAQoCCAgKAggEYhgKBnNjb3JlcxIOCgwIARIICgIIAQoCCAhCBAoAEA0=";

        private const string RawHf =
            "CAg6sgMKdBIGbG9naXRzIghDb25zdGFudCpgCgV2YWx1ZSpUCAEICAgCEAFCCGxvZ2l0c192SkAAACDBVJ8MQB+Zzz4AACDBAAAgwQAAIMEAACDBAAAgwQAAIMEAACDBAAAgwQAAIMEAACDBAAAgwQAAIMEAACDBoAEECr8BEgpwcmVkX2JveGVzIghDb25zdGFudCqmAQoFdmFsdWUqmQEIAQgICAQQAUIMcHJlZF9ib3hlc192SoABmpmZPs3MzD7NzMw+zczMPjMzMz8zMzM/zczMPs3MzD4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACgAQQSDmRmaW5lX3Jhd19zdHViWigKDHBpeGVsX3ZhbHVlcxIYChYIARISCgIIAQoCCAMKAwiABQoDCIAFYhwKBmxvZ2l0cxISChAIARIMCgIIAQoCCAgKAggCYiAKCnByZWRfYm94ZXMSEgoQCAESDAoCCAEKAggICgIIBEIECgAQDXIhCgVuYW1lcxIYezA6ICdnb29kJywgMTogJ2RlZmVjdCd9";

        private const string YoloStub =
            "CAg6ygIK+QESB291dHB1dDAiCENvbnN0YW50KuMBCgV2YWx1ZSrWAQgBCAYICBABQglvdXRwdXQwX3ZKwAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACgAQQSCXlvbG9fc3R1YloiCgZpbWFnZXMSGAoWCAESEgoCCAEKAggDCgMIgAUKAwiABWIdCgdvdXRwdXQwEhIKEAgBEgwKAggBCgIIBgoCCAhCBAoAEA1yIQoFbmFtZXMSGHswOiAnZ29vZCcsIDE6ICdkZWZlY3QnfQ==";

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

        // 800(w) × 400(h) BGR 이미지 — stretch 규약이라 비율이 달라도 좌표 환산이 맞아야 한다
        private static Mat MakeImage() => new Mat(400, 800, MatType.CV_8UC3, Scalar.All(30));

        // ───────── OnnxMetadataReader.ReadGraphIoNames ─────────

        [Fact]
        public void ReadGraphIoNames_returns_inputs_and_outputs_without_session()
        {
            using var m = new TempModel(DeployNoMeta, "deploy");
            var (inputs, outputs) = OnnxMetadataReader.ReadGraphIoNames(m.Path);

            Assert.Equal(new[] { "images", "orig_target_sizes" }, inputs);
            Assert.Equal(new[] { "labels", "boxes", "scores" }, outputs);
        }

        [Fact]
        public void ReadGraphIoNames_missing_file_returns_empty()
        {
            var (inputs, outputs) = OnnxMetadataReader.ReadGraphIoNames(
                Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".onnx"));
            Assert.Empty(inputs);
            Assert.Empty(outputs);
        }

        // ───────── DetectionModelFormatProbe ─────────

        [Fact]
        public void Probe_uses_model_format_metadata_first()
        {
            using var m = new TempModel(DeployWithMeta, "meta");
            Assert.Equal(DetectionModelFormat.DFine, DetectionModelFormatProbe.Probe(m.Path));
        }

        [Fact]
        public void Probe_falls_back_to_graph_io_structure_for_official_export_without_metadata()
        {
            using var m = new TempModel(DeployNoMeta, "nometa");
            Assert.Equal(DetectionModelFormat.DFine, DetectionModelFormatProbe.Probe(m.Path));
        }

        [Fact]
        public void Probe_recognizes_hf_raw_layout()
        {
            using var m = new TempModel(RawHf, "raw");
            Assert.Equal(DetectionModelFormat.DFine, DetectionModelFormatProbe.Probe(m.Path));
        }

        [Fact]
        public void Probe_keeps_yolo_for_single_output_models_and_missing_files()
        {
            using var m = new TempModel(YoloStub, "yolo");
            Assert.Equal(DetectionModelFormat.Yolo, DetectionModelFormatProbe.Probe(m.Path));
            Assert.Equal(DetectionModelFormat.Yolo,
                DetectionModelFormatProbe.Probe(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".onnx")));
        }

        [Theory]
        [InlineData(new[] { "images", "orig_target_sizes" }, new[] { "labels", "boxes", "scores" }, true)]
        [InlineData(new[] { "images" }, new[] { "labels", "boxes", "scores" }, true)]
        [InlineData(new[] { "pixel_values" }, new[] { "logits", "pred_boxes" }, true)]
        [InlineData(new[] { "images" }, new[] { "output0" }, false)]
        [InlineData(new[] { "images" }, new[] { "output0", "output1" }, false)] // YOLOv8-seg
        public void IsDFineLayout_by_io_names(string[] inputs, string[] outputs, bool expected)
        {
            Assert.Equal(expected, DetectionModelFormatProbe.IsDFineLayout(inputs, outputs));
        }

        // ───────── DFineOnnxEngine — deploy 규약 ─────────

        [Fact]
        public void Deploy_layout_scales_boxes_by_orig_target_sizes_and_reads_labels()
        {
            using var m = new TempModel(DeployWithMeta, "deploy");
            using var engine = new DFineOnnxEngine(m.Path);
            using var img = MakeImage();

            Assert.True(engine.IsDeployLayout);
            Assert.True(engine.HasSizesInput);
            Assert.Equal(new[] { "good", "defect" }, engine.GetClassNames());

            var dets = engine.Detect(img, 640, 0.25f, 0.45f);

            Assert.Equal(2, dets.Count); // q2 는 q0 와 IoU 0.9 → NMS 제거
            var top = dets[0];
            Assert.Equal(1, top.ClassId);
            Assert.Equal(0.9f, top.Confidence, 3);
            Assert.Equal(80, top.X);      // 0.1 × 800
            Assert.Equal(80, top.Y);      // 0.2 × 400
            Assert.Equal(320, top.Width); // (0.5 − 0.1) × 800
            Assert.Equal(160, top.Height);// (0.6 − 0.2) × 400

            var second = dets[1];
            Assert.Equal(0, second.ClassId);
            Assert.Equal(400, second.X);
            Assert.Equal(200, second.Y);
            Assert.Equal(320, second.Width);
            Assert.Equal(160, second.Height);
        }

        [Fact]
        public void Deploy_layout_nms_is_controlled_by_iou_threshold()
        {
            using var m = new TempModel(DeployWithMeta, "deploy");
            using var engine = new DFineOnnxEngine(m.Path);
            using var img = MakeImage();

            var loose = engine.Detect(img, 640, 0.25f, 0.99f);
            Assert.Equal(3, loose.Count);
        }

        [Fact]
        public void Deploy_layout_applies_per_class_thresholds()
        {
            using var m = new TempModel(DeployWithMeta, "deploy");
            using var engine = new DFineOnnxEngine(m.Path);
            using var img = MakeImage();

            // class0 은 0.7 이상만 → q1(0.6) 탈락, class1 은 0.5 이상 → q0·q2 통과 후 NMS 로 q2 제거
            var dets = engine.Detect(img, 640, 0.25f, 0.45f, new[] { 0.7f, 0.5f });

            Assert.Single(dets);
            Assert.Equal(1, dets[0].ClassId);
        }

        [Fact]
        public void Deploy_layout_works_without_metadata()
        {
            using var m = new TempModel(DeployNoMeta, "nometa");
            using var engine = new DFineOnnxEngine(m.Path);
            using var img = MakeImage();

            Assert.Empty(engine.GetClassNames());
            var dets = engine.Detect(img, 640, 0.25f, 0.45f);
            Assert.Equal(2, dets.Count);
            Assert.Equal(80, dets[0].X);
        }

        // ───────── DFineOnnxEngine — HF raw 규약 ─────────

        [Fact]
        public void Raw_layout_applies_sigmoid_and_converts_cxcywh()
        {
            using var m = new TempModel(RawHf, "raw");
            using var engine = new DFineOnnxEngine(m.Path);
            using var img = MakeImage();

            Assert.False(engine.IsDeployLayout);
            Assert.False(engine.HasSizesInput);

            var dets = engine.Detect(img, 640, 0.25f, 0.45f);

            Assert.Equal(2, dets.Count);
            Assert.Equal(1, dets[0].ClassId);
            Assert.Equal(0.9f, dets[0].Confidence, 3);
            Assert.Equal(80, dets[0].X);
            Assert.Equal(80, dets[0].Y);
            Assert.Equal(320, dets[0].Width);
            Assert.Equal(160, dets[0].Height);
            Assert.Equal(0, dets[1].ClassId);
            Assert.Equal(0.6f, dets[1].Confidence, 3);

            // 높은 임계값이면 0.6 짜리는 탈락
            Assert.Single(engine.Detect(img, 640, 0.8f, 0.45f));
        }

        // ───────── OnnxEngineCache.GetDetector — 규약별 엔진 선택 ─────────

        [Fact]
        public void Cache_GetDetector_picks_engine_by_probe()
        {
            using var dfine = new TempModel(DeployWithMeta, "cache-dfine");
            using var yolo = new TempModel(YoloStub, "cache-yolo");

            var d = OnnxEngineCache.GetDetector(dfine.Path, 640);
            var y = OnnxEngineCache.GetDetector(yolo.Path, 640);

            Assert.IsType<DFineOnnxEngine>(d);
            Assert.IsType<YoloOnnxEngine>(y);
            Assert.Same(d, OnnxEngineCache.GetDetector(dfine.Path, 640)); // 캐시 재사용
        }

        [Fact]
        public void YoloOnnxEngine_implements_IDetectionEngine()
        {
            using var yolo = new TempModel(YoloStub, "iface");
            using var engine = new YoloOnnxEngine(yolo.Path);
            IDetectionEngine iface = engine;
            using var img = MakeImage();
            Assert.Empty(iface.Detect(img, 640, 0.25f, 0.45f));
        }
    }
}
