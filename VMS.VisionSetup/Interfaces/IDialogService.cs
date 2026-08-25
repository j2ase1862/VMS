using System.Collections.Generic;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;


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
        /// Sequence Editor 다이얼로그. extraDevices 는 InputCheck/OutputAction 노드의
        /// 디바이스 콤보에 표시될 PLC + IO 보드 entry 리스트.
        /// 호출자(MainViewModel) 가 SequenceEditorContext.ExtraDevices 를 그대로 전달.
        /// </summary>
        void ShowSequenceEditorDialog(IEnumerable<SequenceDeviceEntry>? extraDevices = null);
        void ShowCalibrationManagerDialog();
        /// <summary>예제 템플릿 갤러리 — 선택된 템플릿 반환 (취소 시 null).</summary>
        RecipeTemplate? ShowTemplateGalleryDialog();

        /// <summary>
        /// 미등록 카메라 재연결 다이얼로그 — 다른 PC 레시피의 스텝 CameraId 를 이 PC 카메라로
        /// 다시 연결. 반환: 구 CameraId → 새 CameraInfo.Id (건너뛰기 시 null, 전부 "매핑 안 함"이면 빈 dict).
        /// </summary>
        Dictionary<string, string>? ShowCameraRemapDialog(
            IReadOnlyList<(string oldId, int stepCount)> unregistered,
            IReadOnlyList<VMS.Camera.Models.CameraInfo> cameras);
    }
}
