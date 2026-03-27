namespace VMS.Camera.Models
{
    /// <summary>
    /// MemoryMappedFile 기반 프레임 공유 프로토콜 상수
    /// </summary>
    public static class SharedFrameConstants
    {
        // ── 동기화 객체 이름 ──
        public const string MmfName = "Local\\VMS_SharedFrame_Mmf";
        public const string MutexName = "Local\\VMS_SharedFrame_Mutex";
        public const string FrameReadyEventName = "Local\\VMS_SharedFrame_FrameReady";
        public const string WriterAliveEventName = "Local\\VMS_SharedFrame_WriterAlive";

        // ── MMF 용량 ──
        public const long MmfCapacity = 100 * 1024 * 1024; // 100 MB

        // ── 헤더 레이아웃 ──
        public const int HeaderSize = 64;
        public const uint Magic = 0x564D5346; // "VMSF"
        public const uint Version = 1;

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
        public const int OffsetReserved = 60;
    }
}
