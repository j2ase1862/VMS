using System;
using System.Diagnostics;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;
using VMS.PLC.Services.Native;

namespace VMS.PLC.Services
{
    /// <summary>
    /// IoDeviceConfig → IIoBoardConnection 인스턴스 생성.
    ///
    /// Phase 3: 실 SDK try-load 분기 추가 —
    ///   • ADLink: ConnectAsync 단계에서 dask.dll 미발견 시 Mock 폴백
    ///   • Advantech: DaqNaviReflection.EnsureLoaded() 로 Automation.BDaq4.dll 사전 체크,
    ///     실패 시 Mock 폴백
    /// 호출자(App.xaml.cs) 는 항상 IIoBoardConnection 받음 — SDK 가용성에 따라 native or Mock.
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
        /// Advantech: DAQNavi 의 .NET assembly 사전 체크. EnsureLoaded() 가 GAC/PATH 에서
        /// Automation.BDaq4.dll 로드 시도 후 성공 여부 반환. 실패 시 Mock 폴백 — 명시적.
        /// </summary>
        private static IIoBoardConnection CreateAdvantech(IoDeviceConfig c)
        {
            if (c.Description?.Contains("[MOCK]", StringComparison.OrdinalIgnoreCase) == true)
            {
                Debug.WriteLine($"[IoFactory:{c.DeviceId}] Advantech → Mock (description 표식)");
                return new AdvantechMockBoard(c.DeviceId, c.InputChannelCount, c.OutputChannelCount);
            }

            if (!DaqNaviReflection.EnsureLoaded())
            {
                Debug.WriteLine($"[IoFactory:{c.DeviceId}] Advantech → Mock 폴백 " +
                                "(Automation.BDaq4.dll 로드 실패 — DAQNavi 미설치)");
                return new AdvantechMockBoard(c.DeviceId, c.InputChannelCount, c.OutputChannelCount);
            }

            Debug.WriteLine($"[IoFactory:{c.DeviceId}] Advantech → DAQNavi native (board={c.BoardId})");
            return new AdvantechDaqNaviConnection(c.DeviceId, c.BoardId,
                c.InputChannelCount, c.OutputChannelCount);
        }
    }
}
