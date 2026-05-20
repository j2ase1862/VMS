using System;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.ImageProcessing;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PolarUnwrapToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PolarUnwrapTool TypedTool => (PolarUnwrapTool)Tool;

        public PolarUnwrapToolSettingsViewModel(PolarUnwrapTool tool) : base(tool)
        {
            DrawAnnulusCommand = new RelayCommand(() =>
                WeakReferenceMessenger.Default.Send(new RequestDrawROIMessage(useAnnulus: true)));
        }

        public IRelayCommand DrawAnnulusCommand { get; }

        public double CenterX { get => TypedTool.CenterX; set { TypedTool.CenterX = value; OnPropertyChanged(); } }
        public double CenterY { get => TypedTool.CenterY; set { TypedTool.CenterY = value; OnPropertyChanged(); } }
        public double InnerRadius { get => TypedTool.InnerRadius; set { TypedTool.InnerRadius = value; OnPropertyChanged(); } }
        public double OuterRadius { get => TypedTool.OuterRadius; set { TypedTool.OuterRadius = value; OnPropertyChanged(); } }
        public double StartAngleDeg { get => TypedTool.StartAngleDeg; set { TypedTool.StartAngleDeg = value; OnPropertyChanged(); } }
        public PolarUnwrapDirection Direction { get => TypedTool.Direction; set { TypedTool.Direction = value; OnPropertyChanged(); } }
        public int OutputWidth { get => TypedTool.OutputWidth; set { TypedTool.OutputWidth = value; OnPropertyChanged(); } }
        public int OutputHeight { get => TypedTool.OutputHeight; set { TypedTool.OutputHeight = value; OnPropertyChanged(); } }

        public Array Directions => Enum.GetValues(typeof(PolarUnwrapDirection));
    }
}
