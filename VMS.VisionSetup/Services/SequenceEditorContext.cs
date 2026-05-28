using System;
using System.Collections.Generic;
using System.Linq;
using VMS.PLC.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// SequenceEditor 의 InputCheck / OutputAction 노드 디바이스 콤보에 들어갈 1 항목.
    /// DeviceType 으로 PLC vs IO 보드를 구분 — UI 가 종류별로 주소 입력 힌트 / Bit-only 토글을 분기.
    /// </summary>
    public sealed record SequenceDeviceEntry(string DeviceId, IoDeviceType DeviceType)
    {
        /// <summary>
        /// 콤보 선택 박스 표시 — DeviceId 단독 (짧게 유지). 디바이스 종류는 AddressInputHint /
        /// 모니터 섹션 분리로 사용자에게 시각화됨.
        /// </summary>
        public string Display => DeviceId;

        /// <summary>드롭다운 항목 보조 라벨 — 디바이스 종류 짧은 표기 ("PLC" / "ADLink" / "ADV").</summary>
        public string TypeBadge => DeviceType switch
        {
            IoDeviceType.Plc => "PLC",
            IoDeviceType.AdLinkPci743x => "ADLink",
            IoDeviceType.AdvantechPci17xx => "ADV",
            _ => string.Empty
        };

        /// <summary>IO 보드 (ADLink/Advantech) 여부 — 채널 정수 입력 + Bit-only 분기 트리거.</summary>
        public bool IsBoard => DeviceType == IoDeviceType.AdLinkPci743x
                            || DeviceType == IoDeviceType.AdvantechPci17xx;
    }

    /// <summary>
    /// VMS host 가 부팅 시 SystemConfiguration.IoBoards + 기본 PLC 의 디바이스 entry 를
    /// 주입하는 정적 holder. VMS.VisionSetup 은 VMS 프로젝트(SystemConfiguration / IConfigurationService) 를
    /// 직접 참조하지 않음 — host(VMS) 가 이 holder 에 한 번 set 하면 SequenceEditor 가 열릴 때
    /// AvailableDevices 콤보에 자동으로 추가됨.
    ///
    /// Standalone 으로 VMS.VisionSetup 실행 시에도 App.OnStartup 이 IoBoardConfigLoader /
    /// PlcConfigLoader 를 호출해 holder 를 채움 — host 미경유 경로 보완.
    /// </summary>
    public static class SequenceEditorContext
    {
        /// <summary>
        /// SequenceEditor 콤보에 추가될 디바이스 entry 목록 (PLC + IO 보드).
        /// Host(VMS App.xaml.cs) 또는 standalone(VMS.VisionSetup App.xaml.cs) 이 set.
        /// </summary>
        public static IReadOnlyList<SequenceDeviceEntry> ExtraDevices { get; set; } =
            Array.Empty<SequenceDeviceEntry>();

        /// <summary>
        /// 후방호환 — DeviceId 만 필요한 외부 호출자용 view. ExtraDevices 에서 derived.
        /// </summary>
        public static IReadOnlyCollection<string> ExtraDeviceIds =>
            ExtraDevices.Select(d => d.DeviceId).ToArray();
    }
}
