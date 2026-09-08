using VMS.Core.Models.Annotation;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>TrainingConfig → 스크립트 인자 조립 — 스크립트별 화이트리스트와 경로 정규화 (TrainingService·학습 워커 공용).</summary>
    public class TrainingArgumentBuilderTests
    {
        private static TrainingConfig Config(string script) => new()
        {
            TrainingScriptPath = script,
            DatasetPath = @"D:\data\set1\",
            OutputDir = @"D:\out",
            Epochs = 60,
            LearningRate = 0.00025,
            BatchSize = 8,
            ExportOnnx = true,
            Mosaic = 1.0, Mixup = 0.0, HsvH = 0.015, HsvS = 0.7, HsvV = 0.4,
            AnomalyMethod = "patchcore", AnomalyBackbone = "resnet18", CoresetRatio = 0.1,
        };

        [Fact]
        public void Dfine_gets_common_and_augmentation_args_but_no_target_or_anomaly()
        {
            var a = TrainingArgumentBuilder.Build(Config(@"C:\vms\scripts\train_dfine.py"));

            Assert.StartsWith("\"C:\\vms\\scripts\\train_dfine.py\"", a);
            Assert.Contains(" --dataset \"D:\\data\\set1\"", a);   // 후행 구분자 제거
            Assert.Contains(" --output \"D:\\out\"", a);
            Assert.Contains(" --epochs 60", a);
            Assert.Contains(" --lr 0.00025", a);
            Assert.Contains(" --batch_size 8", a);
            Assert.Contains(" --export_onnx", a);
            Assert.Contains(" --hsv_v 0.4", a);
            Assert.Contains(" --mosaic 1", a);
            Assert.DoesNotContain("--target", a);
            Assert.DoesNotContain("--method", a);
            Assert.DoesNotContain("--pretrained", a);           // 비어 있으면 생략 (스크립트 기본값)
        }

        [Fact]
        public void Yolo_gets_augmentation_and_pretrained()
        {
            var c = Config("train_yolo.py");
            c.PretrainedModelPath = @"D:\Models\yolov8n.pt";
            var a = TrainingArgumentBuilder.Build(c);
            Assert.Contains(" --pretrained \"D:\\Models\\yolov8n.pt\"", a);
            Assert.Contains(" --mixup 0", a);
        }

        [Fact]
        public void Classifier_gets_no_augmentation_no_target()
        {
            var a = TrainingArgumentBuilder.Build(Config("train_classifier.py"));
            Assert.DoesNotContain("--hsv_h", a);
            Assert.DoesNotContain("--target", a);
            Assert.DoesNotContain("--method", a);
        }

        [Fact]
        public void Ppocr_gets_target_lowercase()
        {
            var c = Config("train_ppocr.py");
            c.Target = TrainingTarget.Recognition;
            var a = TrainingArgumentBuilder.Build(c);
            Assert.Contains(" --target recognition", a);
        }

        [Fact]
        public void Anomaly_gets_method_backbone_coreset()
        {
            var a = TrainingArgumentBuilder.Build(Config("train_anomaly.py"));
            Assert.Contains(" --method patchcore", a);
            Assert.Contains(" --backbone resnet18", a);
            Assert.Contains(" --coreset_ratio 0.1", a);
            Assert.DoesNotContain("--hsv_h", a);
        }

        [Theory]
        [InlineData(@"D:\", @"D:\.")]
        [InlineData("D:/", @"D:\.")]
        [InlineData(@"D:\data\", @"D:\data")]
        [InlineData("D:/data/", "D:/data")]
        [InlineData("", "")]
        public void NormalizeDir_handles_drive_roots_and_trailing_separators(string input, string expected)
        {
            Assert.Equal(expected, TrainingArgumentBuilder.NormalizeDir(input));
        }

        [Theory]
        [InlineData("train_dfine.py", true)]
        [InlineData("train_yolo.py", true)]
        [InlineData("TRAIN_YOLO_SEG.PY", true)]
        [InlineData("train_classifier.py", false)]
        [InlineData("train_anomaly.py", false)]
        public void UsesAugmentation_by_script_name(string script, bool expected)
        {
            Assert.Equal(expected, TrainingArgumentBuilder.UsesAugmentation(script));
        }
    }
}
