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
        /// <summary>다중 선택 파일 열기 — 취소 시 null (안정 특징 정제 샘플 이미지 등).</summary>
        string[]? ShowOpenFilesDialog(string title, string filter);
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

        /// <summary>
        /// FeatureMatch 학습 마스크 편집기 — 템플릿 위에 브러시/사각형/지우개로 don't-care
        /// 영역을 칠한다. cannyLow/cannyHigh 는 엣지 미리보기용 — 도구의 학습 임계와 같은
        /// 값을 넘겨야 미리보기가 실제 학습 후보 엣지와 일치한다. 반환: 새 마스크(8UC1,
        /// 템플릿 크기, 255=제외 — 소유권 호출자, 전부 지웠으면 빈 마스크), 취소 시 null.
        /// </summary>
        OpenCvSharp.Mat? ShowTrainMaskEditorDialog(OpenCvSharp.Mat templateImage, OpenCvSharp.Mat? existingMask,
            double cannyLow = 50, double cannyHigh = 150);
    }
}
