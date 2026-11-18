using System.Collections.Generic;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// VS-34: 단독 모드(Web 없음)에서도 레시피 봇이 제안한 파라미터가 도구에 들어간다.
    /// 종전에는 Web 동기화가 없으면 값 적용 서비스를 만들지 않아 제안 값이 조용히 버려졌다.
    /// </summary>
    public class ParameterApplyStandaloneTests
    {
        [Fact]
        public void WithoutWebSync_DictionaryValuesAreApplied()
        {
            var service = new ParameterApplyService(null);
            var blob = new BlobTool { MinArea = 100 };

            var (applied, _) = service.ApplyParameters(blob, new Dictionary<string, object?>
            {
                ["SegmentationPolarity"] = "DarkOnLight",
                ["MinArea"] = 50,
                ["MaxArea"] = 2000,
            });

            Assert.Equal(3, applied);
            Assert.Equal(SegmentationPolarity.DarkOnLight, blob.SegmentationPolarity);
            Assert.Equal(50, blob.MinArea);
            Assert.Equal(2000, blob.MaxArea);
        }

        [Fact]
        public void WithoutWebSync_LinkedParamCodePath_DoesNothing()
        {
            var service = new ParameterApplyService(null);
            var blob = new BlobTool();
            blob.LinkedParamCodes["MinArea"] = 7;

            Assert.Equal(0, service.ApplyParameters(blob));
        }
    }
}
