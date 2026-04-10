namespace VMS.Camera.Models
{
    /// <summary>
    /// 로봇 통신 프로토콜 모드
    /// </summary>
    public enum RobotProtocolMode
    {
        /// <summary>제조사 네이티브 프로토콜 (UR RT Binary, Doosan JSON, ABB RAPID 등)</summary>
        VendorNative,

        /// <summary>사용자 소켓 서버 — CSV 텍스트 프로토콜 ("x,y,z,rx,ry,rz")</summary>
        CustomSocket,

        /// <summary>Modbus-TCP 프로토콜 (포트 502) — Doosan 등 Modbus 지원 로봇용</summary>
        ModbusTcp
    }

    /// <summary>
    /// 로봇 연결 설정 모델
    /// </summary>
    public class RobotConnectionConfig
    {
        /// <summary>로봇 제조사별 회전 표현 방식</summary>
        public EulerConvention Convention { get; set; } = EulerConvention.UR_RotationVector;

        /// <summary>로봇 IP 주소 ("SIM" 또는 빈 문자열이면 시뮬레이션)</summary>
        public string IpAddress { get; set; } = "192.168.0.200";

        /// <summary>통신 포트</summary>
        public int Port { get; set; } = 30003;

        /// <summary>통신 프로토콜 모드</summary>
        public RobotProtocolMode ProtocolMode { get; set; } = RobotProtocolMode.VendorNative;

        /// <summary>포즈 요청 커맨드 (null이면 제조사 기본값 사용)</summary>
        public string? PoseRequestCommand { get; set; }

        /// <summary>수신 데이터 구분자 (null이면 제조사 기본값 사용)</summary>
        public string? Delimiter { get; set; }

        /// <summary>Modbus Unit ID (기본 1)</summary>
        public byte ModbusUnitId { get; set; } = 1;

        /// <summary>Modbus TCP 포즈 데이터 시작 레지스터 주소 (기본 270)</summary>
        public ushort ModbusPoseStartRegister { get; set; } = 270;
    }
}
