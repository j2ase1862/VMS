using VMS.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VMS.ViewModels
{
    /// <summary>
    /// ViewModel for individual camera display
    /// </summary>
    public partial class CameraViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _id = string.Empty;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _ipAddress = string.Empty;

        [ObservableProperty]
        private CameraManufacturer _manufacturer;

        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private bool _isPassed = false;  // Bypass checkbox: checked = always OK without inspection

        [ObservableProperty]
        private bool _isInspected = false;  // True after trigger + inspection is performed

        [ObservableProperty]
        private bool _inspectionOk = true;  // Actual inspection result (true=OK, false=NG)

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private BitmapSource? _currentImage;

        // Layout properties
        [ObservableProperty]
        private double _x;

        [ObservableProperty]
        private double _y;

        [ObservableProperty]
        private double _width = 400;

        [ObservableProperty]
        private double _height = 300;

        // Step support
        [ObservableProperty]
        private ObservableCollection<StepViewModel> _steps = new();

        [ObservableProperty]
        private StepViewModel? _selectedStep;

        [ObservableProperty]
        private int _currentStepIndex;

        // Current step's camera settings (bound to selected step)
        public double Exposure
        {
            get => SelectedStep?.Exposure ?? 5000;
            set
            {
                if (SelectedStep != null)
                {
                    SelectedStep.Exposure = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Gain
        {
            get => SelectedStep?.Gain ?? 1.0;
            set
            {
                if (SelectedStep != null)
                {
                    SelectedStep.Gain = value;
                    OnPropertyChanged();
                }
            }
        }

        partial void OnSelectedStepChanged(StepViewModel? value)
        {
            OnPropertyChanged(nameof(Exposure));
            OnPropertyChanged(nameof(Gain));
        }

        // Inspection results
        [ObservableProperty]
        private string _resultMessage = string.Empty;

        [ObservableProperty]
        private int _inspectionCount;

        [ObservableProperty]
        private int _passCount;

        [ObservableProperty]
        private int _failCount;

        // Waiting color: #EB782A (orange)
        private static readonly Brush WaitingBrush = new SolidColorBrush(Color.FromRgb(0xEB, 0x78, 0x2A));
        private static readonly Brush OkBrush = Brushes.LimeGreen;
        private static readonly Brush NgBrush = Brushes.Red;

        // Status logic:
        // IsPassed checked   → always "OK" (green), bypass inspection
        // IsPassed unchecked → "WAIT" (orange) until trigger received
        //   after trigger    → "OK" (green) or "NG" (red) based on InspectionOk
        public string StatusText => IsPassed ? "OK" : (!IsInspected ? "WAIT" : (InspectionOk ? "OK" : "NG"));
        public Brush StatusColor => IsPassed ? OkBrush : (!IsInspected ? WaitingBrush : (InspectionOk ? OkBrush : NgBrush));

        partial void OnIsPassedChanged(bool value)
        {
            // When bypass is toggled, reset inspection state
            if (value)
            {
                IsInspected = false;
            }
            NotifyStatusChanged();
        }

        partial void OnIsInspectedChanged(bool value)
        {
            NotifyStatusChanged();
        }

        partial void OnInspectionOkChanged(bool value)
        {
            NotifyStatusChanged();
        }

        private void NotifyStatusChanged()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }

        /// <summary>
        /// Called when a trigger is received and inspection is performed.
        /// Sets the inspection result (OK or NG).
        /// </summary>
        public void SetInspectionResult(bool ok)
        {
            InspectionOk = ok;
            IsInspected = true;
            InspectionCount++;
            if (ok) PassCount++;
            else FailCount++;
        }

        /// <summary>
        /// Reset inspection state to waiting for next cycle
        /// </summary>
        public void ResetInspection()
        {
            IsInspected = false;
            InspectionOk = true;
        }

        public static CameraViewModel FromConfiguration(CameraConfiguration config)
        {
            var vm = new CameraViewModel
            {
                Id = config.Id,
                Name = config.Name,
                IpAddress = config.IpAddress,
                Manufacturer = config.Manufacturer,
                IsEnabled = config.IsEnabled
            };

            // Create steps from configuration or default
            if (config.Steps != null && config.Steps.Count > 0)
            {
                foreach (var step in config.Steps)
                {
                    vm.Steps.Add(new StepViewModel
                    {
                        StepNumber = step.StepNumber,
                        Name = step.Name,
                        Exposure = step.Exposure,
                        Gain = step.Gain
                    });
                }
            }
            else
            {
                // Create default steps based on StepCount
                var stepCount = Math.Max(1, config.StepCount);
                for (int i = 1; i <= stepCount; i++)
                {
                    vm.Steps.Add(new StepViewModel
                    {
                        StepNumber = i,
                        Name = $"Step {i}",
                        Exposure = 5000,
                        Gain = 1.0
                    });
                }
            }

            // Select first step by default
            if (vm.Steps.Count > 0)
            {
                vm.SelectedStep = vm.Steps[0];
            }

            return vm;
        }

        public void ApplyLayout(CameraWindowLayout layout)
        {
            X = layout.X;
            Y = layout.Y;
            Width = layout.Width;
            Height = layout.Height;
        }

        public CameraWindowLayout ToLayout()
        {
            return new CameraWindowLayout
            {
                CameraId = Id,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height
            };
        }
    }

    /// <summary>
    /// ViewModel for camera step (robot position with exposure/gain settings)
    /// </summary>
    public partial class StepViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _stepNumber = 1;

        [ObservableProperty]
        private string _name = "Step 1";

        [ObservableProperty]
        private double _exposure = 5000;

        [ObservableProperty]
        private double _gain = 1.0;

        public override string ToString() => Name;
    }
}
