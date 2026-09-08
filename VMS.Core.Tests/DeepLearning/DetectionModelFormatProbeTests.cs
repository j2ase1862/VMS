using System;
using System.IO;
using VMS.Core.DeepLearning;
using Xunit;

namespace VMS.Core.Tests.DeepLearning
{
    /// <summary>
    /// 검출 ONNX 규약 판별 — InferenceSession 없이 protobuf 만 읽는 순수 로직 (VMS.Core 로 승격, MLOps Phase 1 §4 재사용).
    /// 스텁 ONNX 는 VMS.VisionSetup.Tests/DFineOnnxEngineTests.cs 와 동일 (8 쿼리·2 클래스 상수 그래프).
    /// </summary>
    public class DetectionModelFormatProbeTests
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
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vms-core-{tag}-{Guid.NewGuid():N}.onnx");
                File.WriteAllBytes(Path, Convert.FromBase64String(base64));
            }
            public void Dispose() { try { File.Delete(Path); } catch { } }
        }

        [Fact]
        public void ReadGraphIoNames_returns_inputs_and_outputs_without_session()
        {
            using var m = new TempModel(DeployNoMeta, "deploy");
            var (inputs, outputs) = OnnxMetadataReader.ReadGraphIoNames(m.Path);
            Assert.Equal(new[] { "images", "orig_target_sizes" }, inputs);
            Assert.Equal(new[] { "labels", "boxes", "scores" }, outputs);
        }

        [Fact]
        public void Read_returns_metadata_props()
        {
            using var m = new TempModel(DeployWithMeta, "meta");
            var meta = OnnxMetadataReader.Read(m.Path);
            Assert.Equal("{0: 'good', 1: 'defect'}", meta["names"]);
            Assert.Equal("[640, 640]", meta["imgsz"]);
            Assert.Equal("dfine", meta["model_format"]);
        }

        [Fact]
        public void ReadGraphIoNames_missing_file_returns_empty()
        {
            var (inputs, outputs) = OnnxMetadataReader.ReadGraphIoNames(
                Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".onnx"));
            Assert.Empty(inputs);
            Assert.Empty(outputs);
        }

        [Fact]
        public void Probe_uses_model_format_metadata_first()
        {
            using var m = new TempModel(DeployWithMeta, "meta");
            Assert.Equal(DetectionModelFormat.DFine, DetectionModelFormatProbe.Probe(m.Path));
        }

        [Fact]
        public void Probe_falls_back_to_graph_io_structure_without_metadata()
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
        [InlineData(new[] { "images" }, new[] { "output0", "output1" }, false)]
        public void IsDFineLayout_by_io_names(string[] inputs, string[] outputs, bool expected)
        {
            Assert.Equal(expected, DetectionModelFormatProbe.IsDFineLayout(inputs, outputs));
        }
    }
}
