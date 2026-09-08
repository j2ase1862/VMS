using System;
using System.IO;
using OpenCvSharp;
using VMS.DeepLearning.Services;
using Xunit;

namespace VMS.Tests.Services
{
    /// <summary>
    /// VMS.DeepLearning 앱의 학습 후 미리보기 추론(OnnxDetectionInference)이 D-FINE deploy 규약
    /// (images + orig_target_sizes → labels/boxes/scores) 을 자동 판별하고 원본 좌표로 환산하는지 검증.
    /// 스텁 모델은 VMS.VisionSetup.Tests/DFineOnnxEngineTests.cs 와 동일 (8 쿼리, 2 클래스, 상수 출력).
    /// </summary>
    public class OnnxDetectionInferenceDFineTests
    {
        private const string DeployWithMeta =
            "CAg61AYKLQoRb3JpZ190YXJnZXRfc2l6ZXMSB3NpemVzX2YiBENhc3QqCQoCdG8YAaABAgpMEgRnaWR4IghDb25zdGFudCo6CgV2YWx1ZSouCAQQB0IGZ2lkeF92SiABAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAKABBAoqCgdzaXplc19mCgRnaWR4EgR3aHdoIgZHYXRoZXIqCwoEYXhpcxgBoAECCjISA3VheCIIQ29uc3RhbnQqIQoFdmFsdWUqFQgBEAdCBXVheF92SggBAAAAAAAAAKABBAodCgR3aHdoCgN1YXgSBXdod2gzIglVbnNxdWVlemUKvwESCmJveGVzX25vcm0iCENvbnN0YW50KqYBCgV2YWx1ZSqZAQgBCAgIBBABQgxib3hlc19ub3JtX3ZKgAHNzMw9zcxMPgAAAD+amRk/AAAAPwAAAD9mZmY/ZmZmP65H4T09Clc+XI8CP/YoHD8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAKABBAofCgpib3hlc19ub3JtCgV3aHdoMxIFYm94ZXMiA011bApyEgZsYWJlbHMiCENvbnN0YW50Kl4KBXZhbHVlKlIIAQgIEAdCCGxhYmVsc192SkABAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAoAEEClISBnNjb3JlcyIIQ29uc3RhbnQqPgoFdmFsdWUqMggBCAgQAUIIc2NvcmVzX3ZKIGZmZj+amRk/AAAAPwAAAAAAAAAAAAAAAAAAAAAAAAAAoAEEEhFkZmluZV9kZXBsb3lfc3R1YloiCgZpbWFnZXMSGAoWCAESEgoCCAEKAggDCgMIgAUKAwiABVojChFvcmlnX3RhcmdldF9zaXplcxIOCgwIBxIICgIIAQoCCAJiGAoGbGFiZWxzEg4KDAgHEggKAggBCgIICGIbCgVib3hlcxISChAIARIMCgIIAQoCCAgKAggEYhgKBnNjb3JlcxIOCgwIARIICgIIAQoCCAhCBAoAEA1yIQoFbmFtZXMSGHswOiAnZ29vZCcsIDE6ICdkZWZlY3QnfXITCgVpbWdzehIKWzY0MCwgNjQwXXIVCgxtb2RlbF9mb3JtYXQSBWRmaW5l";

        private const string YoloStub =
            "CAg6ygIK+QESB291dHB1dDAiCENvbnN0YW50KuMBCgV2YWx1ZSrWAQgBCAYICBABQglvdXRwdXQwX3ZKwAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACgAQQSCXlvbG9fc3R1YloiCgZpbWFnZXMSGAoWCAESEgoCCAEKAggDCgMIgAUKAwiABWIdCgdvdXRwdXQwEhIKEAgBEgwKAggBCgIIBgoCCAhCBAoAEA1yIQoFbmFtZXMSGHswOiAnZ29vZCcsIDE6ICdkZWZlY3QnfQ==";

        private static string WriteTemp(string base64, string tag)
        {
            var path = Path.Combine(Path.GetTempPath(), $"vms-dl-{tag}-{Guid.NewGuid():N}.onnx");
            File.WriteAllBytes(path, Convert.FromBase64String(base64));
            return path;
        }

        [Fact]
        public void Predict_dfine_deploy_model_returns_boxes_in_original_coordinates()
        {
            var path = WriteTemp(DeployWithMeta, "deploy");
            try
            {
                using var svc = new OnnxDetectionInference();
                svc.LoadModel(path);

                Assert.True(svc.IsDFineModel);
                Assert.Equal(2, svc.NumClasses);      // names 메타데이터 2개
                Assert.Equal(640, svc.InputSize);     // imgsz 메타데이터

                using var img = new Mat(400, 800, MatType.CV_8UC3, Scalar.All(40)); // 800(w) × 400(h)
                var result = svc.Predict(img, confThreshold: 0.25f, iouThreshold: 0.45f);

                Assert.Equal(2, result.Predictions.Count);       // q2 는 q0 와 중복 → NMS
                Assert.Equal(0.9f, result.MaxRawConfidence, 3);
                Assert.Equal(3, result.CandidatesAboveZero);

                var top = result.Predictions.Find(p => p.ClassId == 1)!;
                Assert.Equal("defect", top.ClassName);
                Assert.Equal(80, top.X);
                Assert.Equal(80, top.Y);
                Assert.Equal(320, top.Width);
                Assert.Equal(160, top.Height);

                var other = result.Predictions.Find(p => p.ClassId == 0)!;
                Assert.Equal("good", other.ClassName);
                Assert.Equal(400, other.X);
                Assert.Equal(200, other.Y);
            }
            finally
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void LoadModel_yolo_layout_is_not_flagged_as_dfine()
        {
            var path = WriteTemp(YoloStub, "yolo");
            try
            {
                using var svc = new OnnxDetectionInference();
                svc.LoadModel(path);
                Assert.False(svc.IsDFineModel);
                Assert.Equal(2, svc.NumClasses); // [1, 4+2, 8] → 6 − 4
            }
            finally
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
    }
}
