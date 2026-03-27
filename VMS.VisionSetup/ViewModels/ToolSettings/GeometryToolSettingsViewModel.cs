using System;
using System.Collections.ObjectModel;
using VMS.VisionSetup.VisionTools.Measurement;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public class GeometryToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private GeometryTool TypedTool => (GeometryTool)Tool;

        public GeometryToolSettingsViewModel(GeometryTool tool) : base(tool) { }

        public override bool HasCustomROISection => true;

        public GeometryOperation Operation
        {
            get => TypedTool.Operation;
            set
            {
                TypedTool.Operation = value;
                OnPropertyChanged();
            }
        }

        public Array Operations => Enum.GetValues(typeof(GeometryOperation));

        public ObservableCollection<SourceGeometry> SourceGeometries => new(TypedTool.SourceGeometries);

        public void RefreshSourceGeometries()
        {
            OnPropertyChanged(nameof(SourceGeometries));
        }
    }
}
