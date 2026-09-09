using System;
using VMS.Core.Services;
using Xunit;

namespace VMS.Core.Tests.Services
{
    /// <summary>
    /// 검사 결과에 싣는 모델 식별자.
    ///
    /// <para>
    /// 운영 웹의 DlModelVersion 은 50자까지만 받고, 넘기면 FluentValidation 이 400 을 내
    /// <b>검사 결과 업로드 전체가 거절된다</b>. 모델 이름만 잃는 것이 아니라 그 사이클의
    /// 측정값이 통째로 사라지므로, 길이는 기능 요구가 아니라 안전 요구다.
    /// </para>
    /// </summary>
    public class DlModelIdentityTests
    {
        [Fact]
        public void Version_identity_fits_the_limit()
        {
            var identity = DlModelIdentity.ForVersion(Guid.NewGuid());

            Assert.Equal(35, identity.Length);          // "mv:" + 32
            Assert.True(identity.Length <= DlModelIdentity.MaxLength);
            Assert.StartsWith("mv:", identity);
        }

        /// <summary>MLOps 가 이 값을 되읽어 모델별로 집계한다. 왕복이 안 되면 집계가 통째로 빈다.</summary>
        [Fact]
        public void Version_identity_round_trips()
        {
            var id = Guid.NewGuid();

            Assert.Equal(id, DlModelIdentity.TryReadVersionId(DlModelIdentity.ForVersion(id)));
        }

        [Fact]
        public void Non_version_identities_read_back_as_null()
        {
            Assert.Null(DlModelIdentity.TryReadVersionId(null));
            Assert.Null(DlModelIdentity.TryReadVersionId(""));
            Assert.Null(DlModelIdentity.TryReadVersionId("DetectionTool:best.onnx"));
            Assert.Null(DlModelIdentity.TryReadVersionId("mv:이건GUID가아니다"));
        }

        /// <summary>
        /// 예전 형식이 넘치던 바로 그 값. 참조가 들어오면서 69자가 되어 업로드가 거절됐다.
        /// </summary>
        [Theory]
        [InlineData("model://f47ac10b-58cc-4372-a567-0e02b2c3d479@production")]
        [InlineData("model://f47ac10b-58cc-4372-a567-0e02b2c3d479@3")]
        [InlineData(@"D:\아주\깊고\긴\경로\모음\models\detection\line-a\best-2026-09-09-final.onnx")]
        public void Path_identity_never_exceeds_the_limit(string modelPath)
        {
            var identity = DlModelIdentity.ForPath("DetectionTool", modelPath);

            Assert.NotNull(identity);
            Assert.True(identity!.Length <= DlModelIdentity.MaxLength,
                $"{identity.Length}자 — 50자를 넘으면 업로드가 통째로 거절된다");
        }

        /// <summary>짧으면 그대로 둔다 — 멀쩡한 값을 굳이 바꿀 이유가 없다.</summary>
        [Fact]
        public void Short_path_identity_is_left_alone()
            => Assert.Equal("DetectionTool:best.onnx",
                DlModelIdentity.ForPath("DetectionTool", @"D:\models\best.onnx"));

        /// <summary>
        /// 줄일 때는 뒤를 남긴다. 앞은 도구 타입이라 어느 파일인지 가리는 것은 뒤쪽이다.
        /// 잘렸다는 표시를 남겨, 나중에 이 값으로 대조하려다 헛돌지 않게 한다.
        /// </summary>
        [Fact]
        public void Shortening_keeps_the_tail_and_marks_it()
        {
            var identity = DlModelIdentity.ForPath("DetectionTool",
                @"D:\models\아주긴이름-2026-09-09-final-candidate-v3.onnx");

            Assert.True(identity!.Length <= DlModelIdentity.MaxLength);
            Assert.StartsWith("…", identity);
            Assert.EndsWith(".onnx", identity);
        }

        /// <summary>
        /// 참조는 경로가 아니다. Path.GetFileName 에 넣으면 "@production" 같은 꼬리만 남아
        /// 무슨 모델이었는지 알 수 없게 된다.
        /// </summary>
        [Fact]
        public void Reference_is_not_treated_as_a_file_path()
        {
            var identity = DlModelIdentity.ForPath("DetectionTool",
                "model://f47ac10b-58cc-4372-a567-0e02b2c3d479@production");

            Assert.NotEqual("DetectionTool:production", identity);
            Assert.Contains("f47ac10b", identity);
        }

        [Fact]
        public void Empty_path_has_no_identity()
        {
            Assert.Null(DlModelIdentity.ForPath("DetectionTool", null));
            Assert.Null(DlModelIdentity.ForPath("DetectionTool", "   "));
        }
    }
}
