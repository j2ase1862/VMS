using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;

namespace VMS.VisionSetup.VisionTools.Identification
{
    /// <summary>
    /// OCV 폰트 라이브러리의 단일 문자 템플릿.
    /// 학습된 이진화 패치(고정 크기, 흑=배경/백=문자)를 보관.
    /// </summary>
    public partial class CharTemplate : ObservableObject
    {
        /// <summary>인식 대상 문자 (단일 char 권장 — 다중 문자도 허용).</summary>
        [ObservableProperty]
        private string _char = string.Empty;

        /// <summary>정규화 패치 PNG 바이트 (CanonicalSize x CanonicalSize, 단일 채널).</summary>
        public byte[]? TemplatePng { get; set; }

        private Mat? _cached;

        /// <summary>매칭에 사용할 디코딩된 Mat (캐시). Tool 인스턴스 수명 동안 유지.</summary>
        public Mat? GetMat()
        {
            if (_cached != null && !_cached.IsDisposed) return _cached;
            if (TemplatePng == null || TemplatePng.Length == 0) return null;
            try { _cached = Cv2.ImDecode(TemplatePng, ImreadModes.Grayscale); return _cached; }
            catch { return null; }
        }

        public void InvalidateCache()
        {
            _cached?.Dispose();
            _cached = null;
        }
    }
}
