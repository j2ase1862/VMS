using System.Collections.Generic;
using VMS.Camera.Models;

namespace VMS.VisionSetup.Interfaces
{
    public interface ICameraService
    {
        List<CameraInfo> LoadCameraRegistry();
        bool SaveCameraRegistry(List<CameraInfo>? cameras = null);
        bool AddCamera(CameraInfo camera);
        bool UpdateCamera(CameraInfo camera);
        bool RemoveCamera(string id);
        CameraInfo? GetCamera(string id);
        List<CameraInfo> GetAllCameras();
        List<CameraInfo> GetEnabledCameras();
        CameraInfo CreateNewCamera();
        string GetAppDataFolderPath();
    }
}
