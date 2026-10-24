using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 한 스텝의 도구별 오버레이를 합성 이미지 한 장으로 모은다 — VisionSetup(VisionService)과
    /// VMS 메인(InspectionService) 두 실행 엔진이 같이 쓴다.
    ///
    /// <para>판정 배지·요약 글자 같은 머리표는 도구마다 왼쪽 위 같은 자리에 그려진다. 그대로 합치면
    /// 마지막 도구의 머리표만 남아, 스텝은 NG 인데 이미지 배지는 OK 로 보일 수 있었다. 그래서
    /// 머리표 영역(<see cref="VisionResult.HeaderRegions"/>)은 도구 순서대로 아래로 쌓고,
    /// 판정 테두리(<see cref="VisionResult.VerdictFrameThickness"/>)는 스텝 판정 색 하나로 다시 그린다.</para>
    /// </summary>
    public sealed class CompositeOverlayBuilder : IDisposable
    {
        /// <summary>쌓은 머리표 사이 간격(px).</summary>
        public const int HeaderGap = 4;

        private Mat? _composite;
        private int _headerBottom;
        private int _frameThickness;

        /// <summary>합성된 오버레이가 하나라도 있는지.</summary>
        public bool HasComposite => _composite != null;

        /// <summary>
        /// 도구 결과의 오버레이를 합친다. <paramref name="baseImage"/> 는 도구가 그 위에 그린 원본 —
        /// 원본과 달라진 픽셀만 그래픽으로 옮긴다.
        /// </summary>
        public void Add(VisionResult result, Mat? baseImage)
        {
            var overlay = result.OverlayImage;
            if (overlay == null || overlay.Empty()) return;

            if (_composite == null)
            {
                // 첫 오버레이는 통째로 바탕이 된다 — 그 머리표는 제자리에 둔다.
                _composite = OverlayComposer.CreateComposite(overlay, baseImage);
                foreach (var r in result.HeaderRegions)
                    _headerBottom = Math.Max(_headerBottom, r.Bottom);
                _frameThickness = Math.Max(_frameThickness, result.VerdictFrameThickness);
                return;
            }

            if (baseImage == null) return;
            _frameThickness = Math.Max(_frameThickness, result.VerdictFrameThickness);
            OverlayComposer.Merge(overlay, baseImage, _composite,
                result.HeaderRegions, ref _headerBottom, result.VerdictFrameThickness);
        }

        /// <summary>
        /// 합성 이미지를 넘긴다(소유권 이전). 판정 테두리를 그린 도구가 있었으면 스텝 판정
        /// (<paramref name="stepPass"/>) 색으로 테두리를 다시 그린다. 합칠 오버레이가 없었으면 null.
        /// </summary>
        public Mat? Build(bool stepPass)
        {
            var composite = _composite;
            _composite = null;
            if (composite != null && _frameThickness > 0)
            {
                var color = stepPass ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
                Cv2.Rectangle(composite, new Rect(0, 0, composite.Width, composite.Height), color, _frameThickness);
            }
            return composite;
        }

        public void Dispose()
        {
            _composite?.Dispose();
            _composite = null;
        }
    }
}
