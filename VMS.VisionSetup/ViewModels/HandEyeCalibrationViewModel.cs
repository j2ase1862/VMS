using System;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Services;
using VMS.VisionSetup.Interfaces;

namespace VMS.VisionSetup.ViewModels
{
    /// <summary>
    /// 캘리브레이션 데이터 쌍 표시 모델
    /// </summary>
    public class CalibrationPairInfo
    {
        public int Index { get; set; }
        public string PoseText { get; set; } = string.Empty;
        public int CornerCount { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Hand-Eye 캘리브레이션 위저드 ViewModel
    /// Step 0: Setup — 보드 파라미터 설정
    /// Step 1: Collect — 로봇 포즈 + 이미지 수집
    /// Step 2: Calibrate — 카메라/핸드아이 캘리브레이션 실행
    /// Step 3: Save — 결과 저장
    /// </summary>
    public partial class HandEyeCalibrationViewModel : ObservableObject
    {
        private readonly IRobotService _robotService;
        private readonly ICameraAcquisition _cameraAcquisition;
        private readonly IDialogService _dialogService;
        private readonly HandEyeCalibrationService _calibrationService = new();
        private OpenCvSharp.Size _lastImageSize;

        #region Observable Properties

        [ObservableProperty]
        private int _boardCornersX = 9;

        [ObservableProperty]
        private int _boardCornersY = 6;

        [ObservableProperty]
        private double _squareSize = 25.0;

        [ObservableProperty]
        private HandEyeCalibrationMethod _selectedMethod = HandEyeCalibrationMethod.TSAI;

        [ObservableProperty]
        private int _currentStep;

        [ObservableProperty]
        private bool _isCapturing;

        [ObservableProperty]
        private bool _isCalibrating;

        [ObservableProperty]
        private WriteableBitmap? _previewImage;

        [ObservableProperty]
        private int _pairCount;

        [ObservableProperty]
        private double _reprojectionError;

        [ObservableProperty]
        private bool _isCalibrated;

        [ObservableProperty]
        private string _resultMatrixText = string.Empty;

        [ObservableProperty]
        private string? _savedFilePath;

        [ObservableProperty]
        private string _statusMessage = "Setup: 보드 파라미터를 설정하세요";

        #endregion

        public ObservableCollection<CalibrationPairInfo> CalibrationPairs { get; } = new();

        public HandEyeCalibrationMethod[] Methods { get; } =
            (HandEyeCalibrationMethod[])Enum.GetValues(typeof(HandEyeCalibrationMethod));

        public HandEyeCalibrationViewModel(
            IRobotService robotService,
            ICameraAcquisition cameraAcquisition,
            IDialogService dialogService)
        {
            _robotService = robotService;
            _cameraAcquisition = cameraAcquisition;
            _dialogService = dialogService;
        }

        #region Navigation Commands

        [RelayCommand]
        private void NextStep()
        {
            if (CurrentStep < 3)
            {
                // Step 1→2 진행 시 최소 데이터 확인
                if (CurrentStep == 1 && PairCount < 3)
                {
                    StatusMessage = "최소 3개 이상의 데이터가 필요합니다";
                    return;
                }
                CurrentStep++;
                UpdateStatusForStep();
            }
        }

        [RelayCommand]
        private void PreviousStep()
        {
            if (CurrentStep > 0)
            {
                CurrentStep--;
                UpdateStatusForStep();
            }
        }

        private void UpdateStatusForStep()
        {
            StatusMessage = CurrentStep switch
            {
                0 => "Setup: 보드 파라미터를 설정하세요",
                1 => $"Collect: Capture 버튼으로 데이터를 수집하세요 ({PairCount}개 수집됨)",
                2 => IsCalibrated ? $"캘리브레이션 완료 (오차: {ReprojectionError:F3}px)" : "Calibrate: 캘리브레이션을 실행하세요",
                3 => SavedFilePath != null ? $"저장 완료: {SavedFilePath}" : "Save: 결과를 저장하세요",
                _ => string.Empty
            };
        }

        #endregion

        #region Capture Commands

        [RelayCommand]
        private async Task CaptureAsync()
        {
            if (IsCapturing) return;
            IsCapturing = true;

            try
            {
                // 보드 파라미터 동기화
                _calibrationService.BoardCornersX = BoardCornersX;
                _calibrationService.BoardCornersY = BoardCornersY;
                _calibrationService.SquareSize = SquareSize;
                _calibrationService.Method = SelectedMethod;

                // 로봇 포즈 수집
                var pose = await _robotService.GetCurrentPoseAsync();
                if (pose == null)
                {
                    StatusMessage = "로봇 포즈를 읽을 수 없습니다";
                    return;
                }

                // 이미지 촬영
                var result = await _cameraAcquisition.AcquireAsync();
                if (result.Image2D == null)
                {
                    StatusMessage = "이미지를 촬영할 수 없습니다";
                    return;
                }

                _lastImageSize = new OpenCvSharp.Size(result.Image2D.Width, result.Image2D.Height);

                // 체스보드 검출 + 페어 추가
                int cornerCount = _calibrationService.AddCalibrationImage(result.Image2D, pose);

                if (cornerCount > 0)
                {
                    PairCount = _calibrationService.Pairs.Count;

                    var pairInfo = new CalibrationPairInfo
                    {
                        Index = PairCount,
                        PoseText = $"({pose.X:F1}, {pose.Y:F1}, {pose.Z:F1})",
                        CornerCount = cornerCount,
                        Timestamp = DateTime.Now
                    };
                    CalibrationPairs.Add(pairInfo);

                    // 미리보기: 체스보드 코너 오버레이
                    UpdatePreview(result.Image2D);

                    StatusMessage = $"데이터 #{PairCount} 수집 완료 (코너 {cornerCount}개)";
                }
                else
                {
                    StatusMessage = "체스보드를 검출할 수 없습니다. 보드 위치를 조정하세요.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"캡처 오류: {ex.Message}";
            }
            finally
            {
                IsCapturing = false;
            }
        }

        [RelayCommand]
        private void RemoveLastPair()
        {
            if (CalibrationPairs.Count == 0) return;

            _calibrationService.RemoveLastPair();
            CalibrationPairs.RemoveAt(CalibrationPairs.Count - 1);
            PairCount = _calibrationService.Pairs.Count;
            StatusMessage = $"마지막 데이터 제거, {PairCount}개 남음";
        }

        [RelayCommand]
        private void ClearAllPairs()
        {
            _calibrationService.ClearPairs();
            CalibrationPairs.Clear();
            PairCount = 0;
            IsCalibrated = false;
            ResultMatrixText = string.Empty;
            PreviewImage = null;
            StatusMessage = "모든 데이터 초기화됨";
        }

        #endregion

        #region Calibration Commands

        [RelayCommand]
        private async Task CalibrateAsync()
        {
            if (IsCalibrating) return;
            IsCalibrating = true;
            StatusMessage = "캘리브레이션 진행 중...";

            try
            {
                bool success = await Task.Run(() =>
                {
                    // 카메라 내부 파라미터 캘리브레이션 (없으면 자동 수행)
                    if (_calibrationService.CameraMatrix == null)
                    {
                        if (!_calibrationService.CalibrateCamera(_lastImageSize))
                            return false;
                    }

                    // Hand-Eye 캘리브레이션
                    return _calibrationService.Calibrate();
                });

                IsCalibrated = success;

                if (success)
                {
                    ReprojectionError = _calibrationService.ReprojectionError;
                    ResultMatrixText = FormatMatrix(_calibrationService.ResultMatrix);
                    StatusMessage = $"캘리브레이션 완료 (재투영 오차: {ReprojectionError:F3}px)";
                }
                else
                {
                    StatusMessage = _calibrationService.StatusMessage;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"캘리브레이션 오류: {ex.Message}";
            }
            finally
            {
                IsCalibrating = false;
            }
        }

        #endregion

        #region Save Command

        [RelayCommand]
        private void SaveResult()
        {
            if (!IsCalibrated) return;

            var path = _dialogService.ShowSaveFileDialog(
                "JSON Files (*.json)|*.json", ".json", "hand_eye_calibration");

            if (path == null) return;

            try
            {
                _calibrationService.SaveResult(path);
                SavedFilePath = path;
                StatusMessage = $"저장 완료: {path}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"저장 오류: {ex.Message}";
            }
        }

        #endregion

        #region Helpers

        private void UpdatePreview(Mat image)
        {
            try
            {
                using var preview = image.Clone();
                var boardSize = new OpenCvSharp.Size(BoardCornersX, BoardCornersY);

                // 체스보드 코너 다시 검출하여 그리기
                if (Cv2.FindChessboardCorners(preview, boardSize, out var corners))
                {
                    Cv2.DrawChessboardCorners(preview, boardSize, corners, true);
                }

                PreviewImage = preview.ToWriteableBitmap();
            }
            catch
            {
                // 미리보기 실패 시 무시
            }
        }

        private static string FormatMatrix(Matrix4x4 m)
        {
            return $"[{m.M11,9:F4} {m.M12,9:F4} {m.M13,9:F4} {m.M14,9:F4}]\n" +
                   $"[{m.M21,9:F4} {m.M22,9:F4} {m.M23,9:F4} {m.M24,9:F4}]\n" +
                   $"[{m.M31,9:F4} {m.M32,9:F4} {m.M33,9:F4} {m.M34,9:F4}]\n" +
                   $"[{m.M41,9:F4} {m.M42,9:F4} {m.M43,9:F4} {m.M44,9:F4}]";
        }

        #endregion
    }
}
