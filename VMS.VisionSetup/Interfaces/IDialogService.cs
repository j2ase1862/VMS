using System.Collections.Generic;
using VMS.VisionSetup.Models;


namespace VMS.VisionSetup.Interfaces
{
    public interface IDialogService
    {
        void ShowInformation(string message, string title);
        void ShowWarning(string message, string title);
        void ShowError(string message, string title);
        bool ShowConfirmation(string message, string title);
        string? ShowOpenFileDialog(string title, string filter);
        string? ShowFolderBrowserDialog(string description);
        string? ShowSaveFileDialog(string filter, string defaultExt, string? fileName = null);
        string? ShowRenameDialog(string currentName);
        void ShowCameraManagerDialog();
        Recipe? ShowRecipeManagerDialog();
        /// <summary>
        /// Sequence Editor 다이얼로그. extraDeviceIds 는 InputCheck/OutputAction 노드의
        /// 디바이스 콤보에 추가될 IO 보드 DeviceId 들 (기본 "MainPLC" 외).
        /// 호출자(MainViewModel) 가 SystemConfiguration.IoBoards 에서 추출해 전달.
        /// </summary>
        void ShowSequenceEditorDialog(IEnumerable<string>? extraDeviceIds = null);
        void ShowCalibrationManagerDialog();
    }
}
