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

        public ObservableCollection<NgImageItem> NgImageHistory { get; } = new();

        public void RecordInspectionResult(bool ok, string cameraName, BitmapSource? image = null)
        {
            // Update tact time
            if (_tactStopwatch.IsRunning)
            {
                TactTime = Math.Round(_tactStopwatch.Elapsed.TotalSeconds, 2);
                IsTactTimeExceeded = TactTime > TargetTactTime;
            }
            _tactStopwatch.Restart();

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
                        Thumbnail = image,
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

        [RelayCommand]
        private void ResetStatistics()
        {
            TotalCount = 0;
            OkCount = 0;
            NgCount = 0;
            Yield = 0;
            TactTime = 0;
            IsTactTimeExceeded = false;
            NgImageHistory.Clear();
            _tactStopwatch.Reset();
        }
    }
}
