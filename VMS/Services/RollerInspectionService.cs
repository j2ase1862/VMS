using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using VMS.Interfaces;
using VMS.Models;
using LogLevel = VMS.Interfaces.LogLevel;

namespace VMS.Services
{
    /// <summary>
    /// 롤러 위 A4 용지를 밝기 변화로 감지하고 프레임을 누적하여 전체 용지 이미지를 생성하는 서비스.
    ///
    /// 동작 원리:
    /// 1. 각 프레임의 평균 밝기를 계산
    /// 2. 밝기가 임계값 이상이면 "용지" 영역으로 판별
    /// 3. 연속 N프레임 이상 밝으면 용지 진입 확정 → 프레임 누적 시작
    /// 4. 밝기가 다시 떨어지면 용지 이탈 → 누적 종료 → 결과 이미지 생성
    /// </summary>
    public class RollerInspectionService : IRollerInspectionService
    {
        private enum DetectionState
        {
            Idle,           // 대기: 용지 미감지
            Confirming,     // 감지 확인 중: 연속 밝은 프레임 카운트
            Capturing,      // 캡처 중: 용지 프레임 누적
            Cooldown        // 쿨다운: 이탈 후 재감지 방지
        }

        private DetectionState _state = DetectionState.Idle;
        private readonly List<Mat> _accumulatedFrames = new();
        private int _consecutiveBrightCount;
        private int _consecutiveDarkCount;
        private int _cooldownCounter;
        private Stopwatch? _captureStopwatch;
        private readonly ISystemLogService? _logService;

        // 용지 이탈 확정에 필요한 연속 어두운 프레임 수
        private const int DarkFramesForExit = 3;

        public bool IsRunning { get; private set; }
        public double BrightnessThreshold { get; set; } = 120.0;
        public int MinConsecutiveFrames { get; set; } = 3;
        public int CooldownFrames { get; set; } = 10;

        public event Action<RollerInspectionResult>? PaperCaptured;
        public event Action? PaperEntered;
        public event Action? PaperExited;

        public RollerInspectionService(ISystemLogService? logService = null)
        {
            _logService = logService;
        }

        public void Start()
        {
            if (IsRunning) return;

            IsRunning = true;
            ResetState();
            _logService?.Log("Roller inspection started", LogLevel.Success, "Roller");
        }

        public void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            DisposeAccumulatedFrames();
            ResetState();
            _logService?.Log("Roller inspection stopped", LogLevel.Info, "Roller");
        }

        public void ProcessFrame(Mat frame)
        {
            if (!IsRunning || frame == null || frame.Empty()) return;

            double brightness = ComputeAverageBrightness(frame);
            bool isBright = brightness >= BrightnessThreshold;

            switch (_state)
            {
                case DetectionState.Idle:
                    if (isBright)
                    {
                        _consecutiveBrightCount++;
                        if (_consecutiveBrightCount >= MinConsecutiveFrames)
                        {
                            // 용지 진입 확정
                            _state = DetectionState.Capturing;
                            _captureStopwatch = Stopwatch.StartNew();
                            _consecutiveDarkCount = 0;
                            PaperEntered?.Invoke();
                            _logService?.Log($"Paper detected (brightness: {brightness:F1})", LogLevel.Info, "Roller");
                        }
                    }
                    else
                    {
                        _consecutiveBrightCount = 0;
                    }
                    break;

                case DetectionState.Confirming:
                    // Merged into Idle for simplicity
                    break;

                case DetectionState.Capturing:
                    if (isBright)
                    {
                        // 용지 영역 — 프레임 누적
                        _accumulatedFrames.Add(frame.Clone());
                        _consecutiveDarkCount = 0;
                    }
                    else
                    {
                        _consecutiveDarkCount++;
                        if (_consecutiveDarkCount >= DarkFramesForExit)
                        {
                            // 용지 이탈 확정
                            _captureStopwatch?.Stop();
                            CompletePaperCapture();
                            _state = DetectionState.Cooldown;
                            _cooldownCounter = 0;
                            PaperExited?.Invoke();
                        }
                        else
                        {
                            // 아직 이탈 미확정 — 경계 프레임도 누적
                            _accumulatedFrames.Add(frame.Clone());
                        }
                    }
                    break;

                case DetectionState.Cooldown:
                    _cooldownCounter++;
                    if (_cooldownCounter >= CooldownFrames)
                    {
                        ResetState();
                    }
                    break;
            }
        }

        private void CompletePaperCapture()
        {
            if (_accumulatedFrames.Count == 0) return;

            double captureMs = _captureStopwatch?.Elapsed.TotalMilliseconds ?? 0;
            int frameCount = _accumulatedFrames.Count;

            _logService?.Log(
                $"Paper captured: {frameCount} frames, {captureMs:F0}ms",
                LogLevel.Success, "Roller");

            try
            {
                // 누적된 프레임을 세로로 이어붙여 하나의 이미지로 조합
                using var stitched = StitchFrames(_accumulatedFrames);

                if (stitched != null && !stitched.Empty())
                {
                    var bmp = MatToBitmapSource(stitched);

                    var result = new RollerInspectionResult
                    {
                        PaperImage = bmp,
                        FrameCount = frameCount,
                        CaptureTimeMs = captureMs
                    };

                    PaperCaptured?.Invoke(result);
                }
            }
            catch (Exception ex)
            {
                _logService?.Log($"Paper capture assembly error: {ex.Message}", LogLevel.Error, "Roller");
            }
            finally
            {
                DisposeAccumulatedFrames();
            }
        }

        /// <summary>
        /// 프레임들을 세로로 이어붙여 하나의 이미지로 조합
        /// </summary>
        private static Mat? StitchFrames(List<Mat> frames)
        {
            if (frames.Count == 0) return null;
            if (frames.Count == 1) return frames[0].Clone();

            // 모든 프레임의 너비가 같다고 가정 (라인스캔 카메라)
            int width = frames[0].Width;
            int totalHeight = 0;
            var type = frames[0].Type();

            foreach (var f in frames)
            {
                totalHeight += f.Height;
            }

            var result = new Mat(totalHeight, width, type);
            int yOffset = 0;

            foreach (var f in frames)
            {
                var roi = new OpenCvSharp.Rect(0, yOffset, width, f.Height);
                f.CopyTo(result[roi]);
                yOffset += f.Height;
            }

            return result;
        }

        /// <summary>
        /// 프레임의 평균 밝기를 계산합니다.
        /// 컬러 이미지인 경우 그레이스케일로 변환 후 계산합니다.
        /// </summary>
        private static double ComputeAverageBrightness(Mat frame)
        {
            if (frame.Channels() == 1)
            {
                return Cv2.Mean(frame).Val0;
            }

            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            return Cv2.Mean(gray).Val0;
        }

        private static BitmapSource? MatToBitmapSource(Mat mat)
        {
            if (mat.Empty()) return null;

            var format = mat.Channels() switch
            {
                1 => PixelFormats.Gray8,
                3 => PixelFormats.Bgr24,
                4 => PixelFormats.Bgra32,
                _ => PixelFormats.Bgr24
            };

            int stride = (int)mat.Step();
            var bmp = BitmapSource.Create(
                mat.Width, mat.Height, 96, 96, format, null,
                mat.Data, stride * mat.Height, stride);
            bmp.Freeze();
            return bmp;
        }

        private void ResetState()
        {
            _state = DetectionState.Idle;
            _consecutiveBrightCount = 0;
            _consecutiveDarkCount = 0;
            _cooldownCounter = 0;
            _captureStopwatch = null;
        }

        private void DisposeAccumulatedFrames()
        {
            foreach (var f in _accumulatedFrames)
                f.Dispose();
            _accumulatedFrames.Clear();
        }
    }
}
