using CommunityToolkit.Mvvm.ComponentModel;
using VMS.PLC.Models;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// SequenceEditor 모니터 패널의 IO 보드 채널 1 행 (PlcMonitorItem 의 IO 보드 짝).
    /// PLC 와 달리 어드레스가 정수 채널 번호 — Bit-only.
    /// 라이브 폴링은 Phase B 에서 추가 — 현재는 노드 구성만 표시 (CurrentValue = "—").
    /// </summary>
    public partial class IoBoardMonitorItem : ObservableObject
    {
        /// <summary>대상 IO 보드 식별자 (SequenceDeviceEntry.DeviceId).</summary>
        public string DeviceId { get; }

        /// <summary>벤더/모델 라벨 — 표시용 (예: "ADLink", "Advantech").</summary>
        public string VendorLabel { get; }

        /// <summary>채널 번호 (0 ~ N-1).</summary>
        public int Channel { get; }

        /// <summary>I/O 방향.</summary>
        public PlcIoDirection Direction { get; }

        /// <summary>이 채널을 참조하는 노드 이름들 (콤마 구분).</summary>
        public string ReferencedNodes { get; }

        /// <summary>현재값 — "ON" / "OFF" / "—" (미연결).</summary>
        [ObservableProperty]
        private string _currentValue = "—";

        /// <summary>마지막 갱신 시각 (HH:mm:ss).</summary>
        [ObservableProperty]
        private string _lastUpdated = string.Empty;

        /// <summary>에러 메시지 (포링 실패 시).</summary>
        [ObservableProperty]
        private string? _errorMessage;

        /// <summary>상태 색상 — true=ON(녹색), false=OFF(회색), null=미연결(어두움).</summary>
        public string ConditionColor => CurrentValue switch
        {
            "ON" => "#4CAF50",
            "OFF" => "#888888",
            "ERR" => "#F44336",
            _ => "#555555"
        };

        partial void OnCurrentValueChanged(string value)
        {
            OnPropertyChanged(nameof(ConditionColor));
        }

        public IoBoardMonitorItem(string deviceId, string vendorLabel, int channel,
            PlcIoDirection direction, string referencedNodes)
        {
            DeviceId = deviceId;
            VendorLabel = vendorLabel;
            Channel = channel;
            Direction = direction;
            ReferencedNodes = referencedNodes;
        }
    }
}
