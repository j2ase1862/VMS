using System;
using OpenCvSharp;
using VMS.Models;

namespace VMS.Interfaces
{
    /// <summary>
    /// 롤러 위 A4 용지 감지 및 연속 캡처 서비스.
    /// 라인스캔 카메라의 Live 프레임을 분석하여 용지 진입/이탈을 감지하고,
    /// 용지 영역의 프레임을 누적하여 완성 이미지를 생성합니다.
    /// </summary>
    public interface IRollerInspectionService
    {
        /// <summary>
        /// 서비스 동작 중 여부
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 용지 감지 밝기 임계값 (0-255). 프레임 평균 밝기가 이 값 이상이면 용지로 판별.
        /// </summary>
        double BrightnessThreshold { get; set; }

        /// <summary>
        /// 용지 확정에 필요한 최소 연속 프레임 수 (노이즈 방지)
        /// </summary>
        int MinConsecutiveFrames { get; set; }

        /// <summary>
        /// 용지 이탈 후 재감지 방지 쿨다운 프레임 수
        /// </summary>
        int CooldownFrames { get; set; }

        /// <summary>
        /// 서비스 시작
        /// </summary>
        void Start();

        /// <summary>
        /// 서비스 중지 및 상태 초기화
        /// </summary>
        void Stop();

        /// <summary>
        /// Live 프레임을 전달하여 분석합니다.
        /// Mat는 호출자가 관리하며, 내부에서 필요 시 Clone합니다.
        /// </summary>
        void ProcessFrame(Mat frame);

        /// <summary>
        /// 용지 캡처 완료 시 발생. 누적된 프레임이 조합된 결과를 전달합니다.
        /// </summary>
        event Action<RollerInspectionResult>? PaperCaptured;

        /// <summary>
        /// 용지 진입 감지 시 발생
        /// </summary>
        event Action? PaperEntered;

        /// <summary>
        /// 용지 이탈 감지 시 발생
        /// </summary>
        event Action? PaperExited;
    }
}
