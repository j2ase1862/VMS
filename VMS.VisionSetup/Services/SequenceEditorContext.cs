using System;
using System.Collections.Generic;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// VMS host 가 부팅 시 SystemConfiguration.IoBoards 의 DeviceId 들을 주입하는 정적 holder.
    /// VMS.VisionSetup 은 VMS 프로젝트(SystemConfiguration / IConfigurationService) 를 직접 참조하지 않음 —
    /// host(VMS) 가 이 holder 에 한 번 set 하면 SequenceEditor 가 열릴 때 ExtraDeviceIds 가 자동으로
    /// AvailableDeviceIds 콤보에 추가됨.
    ///
    /// Standalone 으로 VMS.VisionSetup 실행 시 ExtraDeviceIds 가 비어있어 기본 "MainPLC" 만 표시 — 안전.
    /// </summary>
    public static class SequenceEditorContext
    {
        /// <summary>
        /// SequenceEditor 의 InputCheck/OutputAction 노드 디바이스 콤보에 추가될 ID 목록.
        /// VMS App.xaml.cs 가 SystemConfiguration.IoBoards 에서 추출해 set.
        /// </summary>
        public static IReadOnlyCollection<string> ExtraDeviceIds { get; set; } = Array.Empty<string>();
    }
}
