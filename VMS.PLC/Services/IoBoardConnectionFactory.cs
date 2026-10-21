using System;
using System.Diagnostics;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// IoDeviceConfig → IIoBoardConnection 인스턴스 생성.
    ///
    /// 두 벤더 모두 native 연결을 돌려주고, SDK(dask.dll · Automation.BDaq4.dll)가 없으면
    /// ConnectAsync 가 false + IIoBoardDiagnostics.LastError 로 사유를 남긴다.
    /// Mock 은 Description 에 "[MOCK]" 표식이 있을 때만 (개발 · 시험용).
    /// </summary>
    public static class IoBoardConnectionFactory
    {
        public static IIoBoardConnection? Create(IoDeviceConfig? config)
        {
            if (config is null || !config.IsEnabled) return null;
            if (string.IsNullOrWhiteSpace(config.DeviceId))
                throw new ArgumentException("IoDeviceConfig.DeviceId is required.", nameof(config));

            return config.Vendor switch
            {
                IoBoardVendor.AdLink => CreateAdLink(config),
                IoBoardVendor.Advantech => CreateAdvantech(config),
                _ => null
            };
        }

        /// <summary>
        /// ADLink: dask.dll 의 P/Invoke 는 메서드 호출 시점에 LoadLibrary 시도 → DllNotFoundException
        /// 가 ConnectAsync 안에서 catch 됨. 따라서 Factory 는 항상 AdLinkDaskConnection 반환하고
        /// ConnectAsync 실패 시 호출자(App.xaml.cs) 가 디바이스 단위로 격리 처리.
        ///
        /// 그러나 운영 환경에서 SDK 미설치 시 의도된 Mock 폴백을 명시적으로 원하면 — config 에
        /// 별도 플래그(예: ForceMock) 추가하거나, 환경변수로 override. 현재는 가장 단순한 동작:
        /// 항상 native 시도, 실패 시 ConnectAsync 가 false → ioDeviceRegistry 에 등록 안 됨.
        /// 시퀀스가 그 DeviceId 호출 시 lookup 실패 → 기본 PLC 경로로 폴백.
        ///
        /// "정말로 보드는 없지만 시뮬레이션 하고 싶다" 의 경우 IoDeviceConfig.Vendor 를 다르게 두거나
        /// Description 필드에 ":mock" 표식. 현재 슬라이스에서는 단순화 위해 native 우선.
        /// </summary>
        private static IIoBoardConnection CreateAdLink(IoDeviceConfig c)
        {
            // Mock 강제 표식: Description 에 "[MOCK]" 포함 시 즉시 Mock 반환 — 개발/테스트용.
            if (c.Description?.Contains("[MOCK]", StringComparison.OrdinalIgnoreCase) == true)
            {
                Debug.WriteLine($"[IoFactory:{c.DeviceId}] ADLink → Mock (description 표식)");
                return new AdLinkMockBoard(c.DeviceId, c.InputChannelCount, c.OutputChannelCount);
            }

            Debug.WriteLine($"[IoFactory:{c.DeviceId}] ADLink → DASK native (model={c.Model}, board={c.BoardId})");
            return new AdLinkDaskConnection(c.DeviceId, c.Model, c.BoardId,
                c.InputChannelCount, c.OutputChannelCount);
        }

        /// <summary>
        /// Advantech: ADLink 와 같이 항상 DAQNavi native 를 돌려준다. Automation.BDaq4.dll 이 없으면
        /// ConnectAsync 가 false + LastError("DAQNavi 없음") → 호출자가 사유를 로그에 남기고 등록하지 않는다.
        /// (종전에는 여기서 조용히 Mock 으로 바꿔, 운영 PC 에 드라이버가 없으면 출력이 어디로도 나가지 않는데
        /// 연결 · 쓰기는 성공으로 보였다.) 시뮬레이션은 Description 의 "[MOCK]" 표식으로만.
        /// </summary>
        private static IIoBoardConnection CreateAdvantech(IoDeviceConfig c)
        {
            if (c.Description?.Contains("[MOCK]", StringComparison.OrdinalIgnoreCase) == true)
            {
                Debug.WriteLine($"[IoFactory:{c.DeviceId}] Advantech → Mock (description 표식)");
                return new AdvantechMockBoard(c.DeviceId, c.InputChannelCount, c.OutputChannelCount);
            }

            Debug.WriteLine($"[IoFactory:{c.DeviceId}] Advantech → DAQNavi native (board={c.BoardId})");
            return new AdvantechDaqNaviConnection(c.DeviceId, c.BoardId,
                c.InputChannelCount, c.OutputChannelCount);
        }
    }
}
