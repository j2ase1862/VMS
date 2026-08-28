using VMS.Camera.Configuration;

namespace VMS.Camera.Models
{
    /// <summary>
    /// MemoryMappedFile 기반 프레임 공유 프로토콜 상수
    /// </summary>
    public static class SharedFrameConstants
    {
        // ── 동기화 객체 이름 ──
        // 인스턴스별 접미사(예: ".line2")로 분리 — 한 PC 에서 VMS 2개가 떠도
        // 서로의 프레임 채널을 밟지 않는다. 기본 인스턴스는 기존 이름 유지.
        public static string MmfName => AppDataPaths.QualifyIpcName("Local\\VMS_SharedFrame_Mmf");
        public static string MutexName => AppDataPaths.QualifyIpcName("Local\\VMS_SharedFrame_Mutex");
        public static string FrameReadyEventName => AppDataPaths.QualifyIpcName("Local\\VMS_SharedFrame_FrameReady");
        public static string WriterAliveEventName => AppDataPaths.QualifyIpcName("Local\\VMS_SharedFrame_WriterAlive");
        public static string ReaderAliveEventName => AppDataPaths.QualifyIpcName("Local\\VMS_SharedFrame_ReaderAlive");

        // ── Grab 요청 채널 (VisionSetup → VMS) ──
        // 프레임 채널과 반대 방향. VisionSetup 이 요청을 쓰고 RequestReady 를 Set,
        // VMS 가 Grab 후 결과(성공/거절 사유)를 쓰고 ResponseReady 를 Set 한다.
        // 거절(AUTO RUN·라이브 중)을 돌려주지 않으면 요청 측이 이유도 모른 채
        // 타임아웃까지 기다리게 되므로 단방향 신호로는 부족하다.
        public static string GrabRequestMmfName => AppDataPaths.QualifyIpcName("Local\\VMS_GrabRequest_Mmf");
        public static string GrabRequestMutexName => AppDataPaths.QualifyIpcName("Local\\VMS_GrabRequest_Mutex");
        public static string GrabRequestReadyEventName => AppDataPaths.QualifyIpcName("Local\\VMS_GrabRequest_Ready");
        public static string GrabResponseReadyEventName => AppDataPaths.QualifyIpcName("Local\\VMS_GrabRequest_Done");

        // ── MMF 용량 ──
        public const long MmfCapacity = 100 * 1024 * 1024; // 100 MB

        // ── 헤더 레이아웃 ──
        // v2: 헤더를 128B 로 넓히고 카메라 식별자를 실었다. v1 은 프레임이 어느 카메라
        // 것인지 알 수 없어, 수신 측이 요청한 카메라와 다른 프레임을 받고도 모르는
        // 구멍이 있었다 (VMS 가 다른 카메라로 Grab 하거나 라이브가 끼어드는 경우).
        // Reader 는 Version 불일치 프레임을 버리므로 구버전과 섞여도 오독은 없다.
        public const int HeaderSize = 128;
        public const uint Magic = 0x564D5346; // "VMSF"
        public const uint Version = 2;

        // DataFlags
        public const uint FlagHas2D = 0x01;
        public const uint FlagHas3D = 0x02;

        // Header field offsets
        public const int OffsetMagic = 0;
        public const int OffsetVersion = 4;
        public const int OffsetDataFlags = 8;
        public const int OffsetTimestamp = 12;
        public const int OffsetFrameCounter = 20;
        public const int OffsetImageWidth = 28;
        public const int OffsetImageHeight = 32;
        public const int OffsetImageChannels = 36;
        public const int OffsetImageStride = 40;
        public const int OffsetPointCount = 44;
        public const int OffsetGridWidth = 48;
        public const int OffsetGridHeight = 52;
        public const int OffsetNameLengthBytes = 56;

        // 카메라 식별자 (v2) — 길이 + 고정 슬롯. 바디가 아니라 헤더에 두어
        // 기존 바디 레이아웃(2D → 점군)을 건드리지 않는다.
        public const int OffsetCameraIdLengthBytes = 60;
        public const int OffsetCameraId = 64;
        public const int CameraIdMaxBytes = 64;   // 64 + 64 = HeaderSize(128)
    }
}
