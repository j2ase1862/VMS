using OpenCvSharp;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 도구별 오버레이를 한 장의 합성 오버레이로 모은다 — VisionSetup(VisionService)과
    /// VMS 메인(InspectionService) 두 실행 엔진의 단일 정의처.
    ///
    /// <para>오버레이는 채널 수가 제각각이다: 대부분 입력 위에 그린 3채널 BGR, 일부는 1채널,
    /// PlaneFit 은 반투명 BGRA(4채널) 레이어. 종전 합성은 1채널만 변환해 4채널이 섞이면
    /// Absdiff 가 예외를 던져 Run 전체가 중단됐다 (2026-09-23 현장 3D 검증).</para>
    /// </summary>
    public static class OverlayComposer
    {
        /// <summary>첫 오버레이로 합성 캔버스(3채널 BGR)를 만든다.</summary>
        public static Mat CreateComposite(Mat overlay, Mat? baseImage)
            => overlay.Channels() == 3 ? overlay.Clone() : ToBgr(overlay, baseImage);

        /// <summary>
        /// 이후 오버레이에서 입력과 달라진 픽셀(=그래픽)만 합성 캔버스에 옮긴다.
        /// 크기가 맞지 않으면 건너뛴다.
        /// </summary>
        public static void Merge(Mat overlay, Mat baseInput, Mat composite)
        {
            Mat overlayBGR = overlay;
            Mat inputBGR = baseInput;
            bool disposeOverlay = false, disposeInput = false;

            if (overlay.Channels() != 3)
            {
                overlayBGR = ToBgr(overlay, baseInput);
                disposeOverlay = true;
            }
            if (baseInput.Channels() != 3)
            {
                inputBGR = ToBgr(baseInput);
                disposeInput = true;
            }

            try
            {
                if (overlayBGR.Size() != composite.Size() || inputBGR.Size() != overlayBGR.Size()) return;

                using var diff = new Mat();
                Cv2.Absdiff(overlayBGR, inputBGR, diff);
                using var grayDiff = new Mat();
                Cv2.CvtColor(diff, grayDiff, ColorConversionCodes.BGR2GRAY);
                using var mask = new Mat();
                Cv2.Threshold(grayDiff, mask, 1, 255, ThresholdTypes.Binary);
                overlayBGR.CopyTo(composite, mask);
            }
            finally
            {
                if (disposeOverlay) overlayBGR.Dispose();
                if (disposeInput) inputBGR.Dispose();
            }
        }

        /// <summary>
        /// 3채널 BGR 로 맞춘다. 1채널은 색 변환, 4채널(반투명 레이어)은 <paramref name="under"/> 위에
        /// 알파 합성한다 — 알파를 버리면 레이어 밖이 검게 칠해진다.
        /// </summary>
        public static Mat ToBgr(Mat src, Mat? under = null)
        {
            var dst = new Mat();
            switch (src.Channels())
            {
                case 1:
                    Cv2.CvtColor(src, dst, ColorConversionCodes.GRAY2BGR);
                    return dst;
                case 4:
                    Cv2.CvtColor(src, dst, ColorConversionCodes.BGRA2BGR);
                    if (under == null || under.Empty() || under.Size() != src.Size())
                        return dst;

                    using (var baseBgr = under.Channels() == 3 ? under.Clone() : ToBgr(under))
                    {
                        var ch = Cv2.Split(src);
                        try
                        {
                            using var a3 = new Mat();
                            Cv2.Merge(new[] { ch[3], ch[3], ch[3] }, a3);
                            using var af = new Mat();
                            a3.ConvertTo(af, MatType.CV_32FC3, 1.0 / 255.0);
                            using var inv = new Mat();
                            Cv2.Subtract(Scalar.All(1.0), af, inv);
                            using var fg = new Mat();
                            using var bg = new Mat();
                            dst.ConvertTo(fg, MatType.CV_32FC3);
                            baseBgr.ConvertTo(bg, MatType.CV_32FC3);
                            Cv2.Multiply(fg, af, fg);
                            Cv2.Multiply(bg, inv, bg);
                            Cv2.Add(fg, bg, fg);
                            fg.ConvertTo(dst, MatType.CV_8UC3);
                        }
                        finally
                        {
                            foreach (var m in ch) m.Dispose();
                        }
                    }
                    return dst;
                default:
                    src.CopyTo(dst);
                    return dst;
            }
        }
    }
}
