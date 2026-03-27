using System;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class PlaneFitToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private PlaneFitTool TypedTool => (PlaneFitTool)Tool;

        public PlaneFitToolSettingsViewModel(PlaneFitTool tool) : base(tool) { }

        public PlaneFitMethod FitMethod
        {
            get => TypedTool.FitMethod;
            set { TypedTool.FitMethod = value; OnPropertyChanged(); }
        }

        public Array FitMethods => Enum.GetValues(typeof(PlaneFitMethod));

        public int RansacIterations
        {
            get => TypedTool.RansacIterations;
            set { TypedTool.RansacIterations = value; OnPropertyChanged(); }
        }

        public double RansacThreshold
        {
            get => TypedTool.RansacThreshold;
            set { TypedTool.RansacThreshold = value; OnPropertyChanged(); }
        }

        public int SampleStride
        {
            get => TypedTool.SampleStride;
            set { TypedTool.SampleStride = value; OnPropertyChanged(); }
        }

        public bool IsRansac => FitMethod == PlaneFitMethod.RANSAC;
    }
}
