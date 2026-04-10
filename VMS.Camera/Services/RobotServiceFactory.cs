using System;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Services.Robots;

namespace VMS.Camera.Services
{
    /// <summary>
    /// 로봇 서비스 팩토리 — EulerConvention 기반으로 제조사별 서비스 생성
    /// PlcConnectionFactory 패턴 참고
    /// </summary>
    public static class RobotServiceFactory
    {
        /// <summary>
        /// 로봇 연결 설정에 따라 적절한 IRobotService 생성
        /// IP가 "SIM" 또는 빈 문자열이면 SimulatedRobotService 반환
        /// </summary>
        public static IRobotService Create(RobotConnectionConfig config)
        {
            // 시뮬레이션 모드
            if (string.IsNullOrWhiteSpace(config.IpAddress) ||
                config.IpAddress.Equals("SIM", StringComparison.OrdinalIgnoreCase))
            {
                return new SimulatedRobotService { Convention = config.Convention };
            }

            // Modbus-TCP 모드: 전용 서비스 생성 (TcpRobotService 비사용)
            if (config.ProtocolMode == RobotProtocolMode.ModbusTcp)
            {
                return new DoosanModbusRobotService
                {
                    Convention = config.Convention,
                    UnitId = config.ModbusUnitId,
                    PoseStartRegister = config.ModbusPoseStartRegister
                };
            }

            TcpRobotService service = config.Convention switch
            {
                EulerConvention.UR_RotationVector => new UrRobotService(),
                EulerConvention.Doosan_ZYX => new DoosanRobotService(),
                EulerConvention.Jaka_XYZ => new JakaRobotService(),
                EulerConvention.ABB_Quaternion => new AbbRobotService(),
                EulerConvention.Fanuc_WPR => new FanucRobotService(),
                _ => new TcpRobotService { Convention = config.Convention }
            };

            // 프로토콜 모드 적용: CustomSocket이면 네이티브 프로토콜 비활성화
            if (config.ProtocolMode == RobotProtocolMode.CustomSocket)
            {
                ApplyCustomSocketMode(service);
            }

            // 사용자가 명시적으로 설정한 경우 기본값 덮어쓰기
            if (config.PoseRequestCommand != null)
                service.PoseRequestCommand = config.PoseRequestCommand;
            if (config.Delimiter != null)
                service.Delimiter = config.Delimiter;

            return service;
        }

        /// <summary>
        /// CustomSocket 모드: 제조사 네이티브 프로토콜 비활성화 → 범용 CSV 텍스트 프로토콜 사용
        /// </summary>
        private static void ApplyCustomSocketMode(TcpRobotService service)
        {
            switch (service)
            {
                case UrRobotService ur:
                    ur.UseRealtimeInterface = false;
                    break;
                case DoosanRobotService doosan:
                    doosan.UseJsonProtocol = false;
                    break;
                case JakaRobotService jaka:
                    jaka.UseJsonProtocol = false;
                    break;
                case AbbRobotService abb:
                    abb.UseRapidProtocol = false;
                    break;
                case FanucRobotService fanuc:
                    fanuc.UseKarelFormat = false;
                    break;
            }
        }

        /// <summary>
        /// 제조사별 기본 포트 반환
        /// </summary>
        public static int GetDefaultPort(EulerConvention convention) => convention switch
        {
            EulerConvention.UR_RotationVector => 30003,
            EulerConvention.Doosan_ZYX => 12345,
            EulerConvention.Jaka_XYZ => 10001,
            EulerConvention.ABB_Quaternion => 6511,
            EulerConvention.Fanuc_WPR => 18735,
            _ => 30003
        };
    }
}
