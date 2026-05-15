using CommunityToolkit.Mvvm.ComponentModel;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// NPoint 캘리브레이션용 점 쌍 — 픽셀 좌표(이미지에서 클릭) + 월드 mm 좌표(사용자 입력).
    /// </summary>
    public partial class CalibPoint : ObservableObject
    {
        [ObservableProperty] private double _pixelX;
        [ObservableProperty] private double _pixelY;
        [ObservableProperty] private double _worldXmm;
        [ObservableProperty] private double _worldYmm;
    }
}
