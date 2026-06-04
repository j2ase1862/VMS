using System;
using System.IO;
using VMS.VisionSetup.VisionTools.DeepLearning;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// OnnxModelBase.LoadModel 의 결함 허용성 검증 (GS P1-#4).
    /// 손상된 ONNX 파일 / 누락 파일에 대해 native exception 이 호출자로 누출되지 않고
    /// OnnxLoadException 으로 wrap 되며 _session 이 null 로 유지되는지 확인.
    /// </summary>
    public class OnnxLoadResilienceTests
    {
        /// <summary>
        /// 테스트 전용 OnnxModelBase 서브클래스 — protected LoadModel / _session 접근 노출.
        /// </summary>
        private sealed class TestOnnxModel : OnnxModelBase
        {
            public void LoadPublic(string path) => LoadModel(path);
            public bool SessionIsNull => !IsLoaded;
        }

        [Fact]
        public void LoadModel_throws_FileNotFoundException_when_missing()
        {
            var model = new TestOnnxModel();
            var missing = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".onnx");

            Assert.Throws<FileNotFoundException>(() => model.LoadPublic(missing));
            Assert.True(model.SessionIsNull, "파일 없을 때는 _session 이 null 유지");
        }

        [Fact]
        public void LoadModel_wraps_native_exception_as_OnnxLoadException_for_corrupt_file()
        {
            // 손상된 ONNX 시뮬레이션 — 텍스트 파일을 .onnx 로 저장
            var corruptPath = Path.Combine(Path.GetTempPath(), "corrupt-" + Guid.NewGuid() + ".onnx");
            File.WriteAllText(corruptPath, "이 파일은 valid ONNX 가 아닙니다 — 텍스트 본문");

            try
            {
                var model = new TestOnnxModel();
                var ex = Assert.Throws<OnnxLoadException>(() => model.LoadPublic(corruptPath));

                Assert.Equal(corruptPath, ex.ModelPath);
                Assert.NotNull(ex.InnerException);
                Assert.True(model.SessionIsNull, "로드 실패 후 _session 이 null 로 유지돼야 함");
            }
            finally
            {
                try { File.Delete(corruptPath); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void LoadModel_wraps_native_exception_for_truncated_binary()
        {
            // 손상된 ONNX 시뮬레이션 — protobuf magic header 가 끊긴 바이너리
            var truncated = Path.Combine(Path.GetTempPath(), "truncated-" + Guid.NewGuid() + ".onnx");
            File.WriteAllBytes(truncated, new byte[] { 0x08, 0x07, 0x12, 0x04 });

            try
            {
                var model = new TestOnnxModel();
                Assert.Throws<OnnxLoadException>(() => model.LoadPublic(truncated));
                Assert.True(model.SessionIsNull);
            }
            finally
            {
                try { File.Delete(truncated); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void OnnxLoadException_preserves_inner_exception_chain()
        {
            // 호출자가 inner exception 으로 native error 종류를 식별 가능해야 함
            var corrupt = Path.Combine(Path.GetTempPath(), "chain-" + Guid.NewGuid() + ".onnx");
            File.WriteAllText(corrupt, "garbage");

            try
            {
                var model = new TestOnnxModel();
                var ex = Assert.Throws<OnnxLoadException>(() => model.LoadPublic(corrupt));

                Assert.NotNull(ex.InnerException);
                Assert.False(string.IsNullOrEmpty(ex.Message));
                Assert.Contains(Path.GetFileName(corrupt), ex.Message);
            }
            finally
            {
                try { File.Delete(corrupt); } catch { /* best effort */ }
            }
        }
    }
}
