using VMS.Core.Models.Annotation;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>train_*.py stdout 프로토콜 파서 — TrainingService·학습 워커 공용 (MLOps Phase 3 §5).</summary>
    public class TrainingOutputParserTests
    {
        [Fact]
        public void Epoch_updates_epoch_progress_and_message()
        {
            var s = new TrainingStatus();
            Assert.Equal(TrainingOutputKind.Epoch, TrainingOutputParser.Apply("[EPOCH] 5/20", s));
            Assert.Equal(5, s.CurrentEpoch);
            Assert.Equal(20, s.TotalEpochs);
            Assert.Equal(25.0, s.Progress, 6);
            Assert.Equal("Epoch 5/20", s.Message);
        }

        [Fact]
        public void Loss_acc_progress_are_parsed_invariant_culture()
        {
            var s = new TrainingStatus();
            Assert.Equal(TrainingOutputKind.Loss, TrainingOutputParser.Apply("[LOSS] 13.0834", s));
            Assert.Equal(13.0834, s.Loss, 6);
            Assert.Equal(TrainingOutputKind.Accuracy, TrainingOutputParser.Apply("[ACC] 0.8700", s));
            Assert.Equal(0.87, s.Accuracy, 6);
            Assert.Equal(TrainingOutputKind.Progress, TrainingOutputParser.Apply("[PROGRESS] 56.2", s));
            Assert.Equal(56.2, s.Progress, 6);
        }

        [Fact]
        public void Onnx_done_error_are_recognized()
        {
            var s = new TrainingStatus();
            Assert.Equal(TrainingOutputKind.Onnx, TrainingOutputParser.Apply(@"[ONNX] C:\out\best.onnx  ", s));
            Assert.Equal(@"C:\out\best.onnx", s.OnnxOutputPath);

            Assert.Equal(TrainingOutputKind.Done, TrainingOutputParser.Apply("[DONE]", s));
            Assert.Equal(100, s.Progress);

            Assert.Equal(TrainingOutputKind.Error, TrainingOutputParser.Apply("[ERROR] 사전학습 모델 로드 실패 (x)", s));
            Assert.Equal("사전학습 모델 로드 실패 (x)", s.Message);
        }

        [Theory]
        [InlineData("[INFO] classes=2 device=cuda")]
        [InlineData("[WARN] lr=0.001 는 권장값보다 큽니다")]
        [InlineData("Loading weights: 100%")]
        [InlineData("")]
        public void Non_protocol_lines_do_not_touch_status(string line)
        {
            var s = new TrainingStatus { CurrentEpoch = 3, Progress = 42, Message = "keep" };
            Assert.Equal(TrainingOutputKind.None, TrainingOutputParser.Apply(line, s));
            Assert.Equal(3, s.CurrentEpoch);
            Assert.Equal(42, s.Progress);
            Assert.Equal("keep", s.Message);
        }
    }
}
