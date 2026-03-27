using OpenCvSharp;
using VMS.VisionSetup.VisionTools.ImageProcessing;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class MorphologyToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private MorphologyTool TypedTool => (MorphologyTool)Tool;

        public MorphologyToolSettingsViewModel(MorphologyTool tool) : base(tool) { }

        public MorphologyOperation Operation { get => TypedTool.Operation; set => TypedTool.Operation = value; }
        public MorphShapes KernelShape { get => TypedTool.KernelShape; set => TypedTool.KernelShape = value; }
        public int KernelWidth { get => TypedTool.KernelWidth; set => TypedTool.KernelWidth = value; }
        public int KernelHeight { get => TypedTool.KernelHeight; set => TypedTool.KernelHeight = value; }
        public int Iterations { get => TypedTool.Iterations; set => TypedTool.Iterations = value; }
    }
}
