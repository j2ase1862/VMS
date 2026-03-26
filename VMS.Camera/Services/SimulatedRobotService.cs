using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// 시뮬레이션 로봇 서비스 (개발/테스트용)
    /// 미리 정의된 포즈 시퀀스를 순차 반환
    /// </summary>
    public class SimulatedRobotService : IRobotService
    {
        private bool _disposed;
        private int _poseIndex;

        public bool IsConnected { get; private set; }

        public EulerConvention Convention { get; set; } = EulerConvention.UR_RotationVector;

        /// <summary>
        /// 시뮬레이션 포즈 시퀀스 (사용자가 설정 가능)
        /// </summary>
        public List<RobotPose> PoseSequence { get; set; } = new();

        public SimulatedRobotService()
        {
            // 기본 시뮬레이션: 주사위 5면 촬영 포즈 (UR rotation vector, 단위 mm/rad)
            PoseSequence = new List<RobotPose>
            {
                // 상면 (위에서 아래로)
                new() { X = 0, Y = 0, Z = 500, Rx = 0, Ry = 0, Rz = 0,
                    Convention = EulerConvention.UR_RotationVector },
                // 전면 (앞에서)
                new() { X = 0, Y = -300, Z = 300, Rx = -1.2092, Ry = 0, Rz = 0,
                    Convention = EulerConvention.UR_RotationVector },
                // 우측면
                new() { X = 300, Y = 0, Z = 300, Rx = 0, Ry = 1.2092, Rz = 0,
                    Convention = EulerConvention.UR_RotationVector },
                // 후면
                new() { X = 0, Y = 300, Z = 300, Rx = 1.2092, Ry = 0, Rz = 0,
                    Convention = EulerConvention.UR_RotationVector },
                // 좌측면
                new() { X = -300, Y = 0, Z = 300, Rx = 0, Ry = -1.2092, Rz = 0,
                    Convention = EulerConvention.UR_RotationVector },
            };
        }

        public Task<bool> ConnectAsync(string ipAddress, int port)
        {
            IsConnected = true;
            _poseIndex = 0;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            _poseIndex = 0;
            return Task.CompletedTask;
        }

        public Task<RobotPose?> GetCurrentPoseAsync(int timeoutMs = 3000)
        {
            if (!IsConnected || PoseSequence.Count == 0)
                return Task.FromResult<RobotPose?>(null);

            var pose = PoseSequence[_poseIndex % PoseSequence.Count];
            _poseIndex++;

            return Task.FromResult<RobotPose?>(pose);
        }

        public Task<string?> SendCommandAsync(string command, int timeoutMs = 3000)
        {
            return Task.FromResult<string?>($"SIM_OK: {command}");
        }

        public Task WaitForSettlingAsync(int settlingTimeMs = 200)
        {
            // 시뮬레이션에서는 대기 불필요
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsConnected = false;
        }
    }
}
