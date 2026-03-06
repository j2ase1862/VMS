using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Interfaces
{
    public interface IVisionService
    {
        Mat? CurrentImage { get; set; }
        ObservableCollection<VisionToolBase> Tools { get; }
        ObservableCollection<VisionResult> Results { get; }
        double TotalExecutionTime { get; }
        bool IsRunning { get; }
        bool LastRunSuccess { get; }
        Mat? LastCompositeOverlay { get; }

        void SetImage(Mat image);
        void AddTool(VisionToolBase tool);
        void RemoveTool(VisionToolBase tool);
        void MoveTool(int fromIndex, int toIndex);
        void ClearTools();
        void AddConnection(VisionToolBase source, VisionToolBase target, ConnectionType type);
        void RemoveConnection(VisionToolBase source, VisionToolBase target, ConnectionType type);
        void ClearConnections();
        VisionResult ExecuteTool(VisionToolBase tool, Mat? inputImage = null);
        Task<List<VisionResult>> ExecuteAllAsync();
        (Mat HeightMap, HeightMapMetadata Metadata) GenerateHeightMap(
            PointCloudData pointCloud, float zRef, float zMin, float zMax);
    }
}
