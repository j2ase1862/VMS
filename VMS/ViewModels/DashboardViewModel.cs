using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using VMS.Models;

namespace VMS.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        private const int MaxNgHistory = 10;
        private readonly Stopwatch _tactStopwatch = new();

        [ObservableProperty]
        private int _totalCount;

        [ObservableProperty]
        private int _okCount;

        [ObservableProperty]
        private int _ngCount;

        [ObservableProperty]
        private double _yield;

        [ObservableProperty]
        private double _tactTime;

        [ObservableProperty]
        private double _targetTactTime = 5.0;

        [ObservableProperty]
        private bool _isTactTimeExceeded;

        // 마지막 검사의 처리 시간 (스텝 실행 실측, ms) — Tact 와 달리 수동/자동 모두 유의미
        [ObservableProperty]
        private double _processingTimeMs;

        public ObservableCollection<NgImageItem> NgImageHistory { get; } = new();

        public void RecordInspectionResult(bool ok, string cameraName, BitmapSource? image = null,
            double processingTimeMs = 0, bool updateTact = true)
        {
            // Tact = 직전 검사와의 간격 — 연속 운전(AUTO RUN) 중에만 의미가 있다.
            // 수동 Grab+Inspect 는 조작 대기 시간이 통째로 잡혀 수백 초가 표시되던
            // 현장 혼선(2026-08-10, 337.89s)이 있어 수동 검사는 측정 체인을 끊는다.
            if (updateTact)
            {
                if (_tactStopwatch.IsRunning)
                {
                    TactTime = Math.Round(_tactStopwatch.Elapsed.TotalSeconds, 2);
                    IsTactTimeExceeded = TactTime > TargetTactTime;
                }
                _tactStopwatch.Restart();
            }
            else
            {
                // 수동 검사 후 첫 자동 검사가 수동 시점과의 간격을 Tact 로 오인하지 않도록 중단
                _tactStopwatch.Reset();
            }

            ProcessingTimeMs = Math.Round(processingTimeMs, 1);

            TotalCount++;
            if (ok)
            {
                OkCount++;
            }
            else
            {
                NgCount++;

                // Add to NG image history
                if (image != null)
                {
                    var item = new NgImageItem
                    {
                        // 원본 BitmapSource(5MP≈15MB)를 그대로 보관하면 이력 10장이
                        // 최대 150MB 를 상시 점유한다 (실증 PC 2026-08-29 AUTO RUN
                        // 기준선 상승 원인 중 하나) — 축소 실체화본만 보관한다.
                        // 픽셀 단위 정밀 확인은 저장 파일/Web NG 이미지가 원본 경로.
                        Thumbnail = CreateThumbnail(image),
                        CameraName = cameraName,
                        Timestamp = DateTime.Now
                    };

                    NgImageHistory.Insert(0, item);
                    while (NgImageHistory.Count > MaxNgHistory)
                    {
                        NgImageHistory.RemoveAt(NgImageHistory.Count - 1);
                    }
                }
            }

            // Update yield
            Yield = TotalCount > 0
                ? Math.Round((double)OkCount / TotalCount * 100, 1)
                : 0;
        }

        // 갤러리 셀과 클릭 확대 보기(카메라 타일 표시) 양쪽에 충분한 크기.
        private const int ThumbnailMaxEdge = 960;

        /// <summary>
        /// 긴 변 기준 ThumbnailMaxEdge 로 축소한 실체화 사본을 반환.
        /// TransformedBitmap 을 그대로 보관하면 원본 BitmapSource 를 계속 참조해
        /// 축소 의미가 없다 — WriteableBitmap 으로 픽셀을 복사해 원본과 분리한다.
        /// </summary>
        internal static BitmapSource CreateThumbnail(BitmapSource source)
        {
            int maxEdge = Math.Max(source.PixelWidth, source.PixelHeight);
            if (maxEdge <= ThumbnailMaxEdge) return source;

            double scale = ThumbnailMaxEdge / (double)maxEdge;
            var scaled = new TransformedBitmap(source,
                new System.Windows.Media.ScaleTransform(scale, scale));
            var thumb = new WriteableBitmap(scaled);
            thumb.Freeze();
            return thumb;
        }

        [RelayCommand]
        private void ResetStatistics()
        {
            TotalCount = 0;
            OkCount = 0;
            NgCount = 0;
            Yield = 0;
            TactTime = 0;
            IsTactTimeExceeded = false;
            ProcessingTimeMs = 0;
            NgImageHistory.Clear();
            _tactStopwatch.Reset();
        }
    }
}
