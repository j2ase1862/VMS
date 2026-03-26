using System;
using System.Collections.ObjectModel;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class Geometry3DToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private Geometry3DTool TypedTool => (Geometry3DTool)Tool;

        public Geometry3DToolSettingsViewModel(Geometry3DTool tool) : base(tool) { }

        public override bool HasCustomROISection => true;

        public Geometry3DOperation Operation
        {
            get => TypedTool.Operation;
            set { TypedTool.Operation = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowManualPoints)); }
        }

        public Array Operations => Enum.GetValues(typeof(Geometry3DOperation));

        public bool UseManualPoints
        {
            get => TypedTool.UseManualPoints;
            set { TypedTool.UseManualPoints = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowManualPoints)); }
        }

        public bool ShowManualPoints => UseManualPoints &&
            (Operation == Geometry3DOperation.PointToPointDistance ||
             Operation == Geometry3DOperation.PointToPlaneDistance ||
             Operation == Geometry3DOperation.PointToLineDistance3D);

        public double PointAX
        {
            get => TypedTool.PointA.X;
            set { TypedTool.PointA = new OpenCvSharp.Point2d(value, TypedTool.PointA.Y); OnPropertyChanged(); }
        }

        public double PointAY
        {
            get => TypedTool.PointA.Y;
            set { TypedTool.PointA = new OpenCvSharp.Point2d(TypedTool.PointA.X, value); OnPropertyChanged(); }
        }

        public double PointBX
        {
            get => TypedTool.PointB.X;
            set { TypedTool.PointB = new OpenCvSharp.Point2d(value, TypedTool.PointB.Y); OnPropertyChanged(); }
        }

        public double PointBY
        {
            get => TypedTool.PointB.Y;
            set { TypedTool.PointB = new OpenCvSharp.Point2d(TypedTool.PointB.X, value); OnPropertyChanged(); }
        }

        public bool ShowPointB => Operation == Geometry3DOperation.PointToPointDistance;

        public ObservableCollection<SourceGeometry3D> SourceGeometries => new(TypedTool.SourceGeometries);

        public void RefreshSourceGeometries()
        {
            OnPropertyChanged(nameof(SourceGeometries));
        }
    }
}
