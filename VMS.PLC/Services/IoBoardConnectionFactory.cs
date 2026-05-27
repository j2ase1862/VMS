using System;
using VMS.PLC.Interfaces;
using VMS.PLC.Models;

namespace VMS.PLC.Services
{
    /// <summary>
    /// IoDeviceConfig → IIoBoardConnection 인스턴스 생성.
    ///
    /// Phase 1: Mock 구현만 반환 — 실 SDK 미설치 환경에서도 안전 동작.
    /// Phase 3: AdLinkDaskConnection / AdvantechDaqNaviConnection 추가 (SDK 설치 환경에서만).
    ///
    /// SDK 가용성에 따른 분기는 Factory 내부에서 try/catch — 호출자는 항상 동일 인터페이스 받음.
    /// </summary>
    public static class IoBoardConnectionFactory
    {
        /// <summary>
        /// config 의 Vendor + Model 로 적절한 IIoBoardConnection 인스턴스 생성.
        /// config 가 null/Disabled 면 null 반환.
        /// </summary>
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

        private static IIoBoardConnection CreateAdLink(IoDeviceConfig c)
        {
            // Phase 3: DASK SDK 로드 시도 → 성공 시 AdLinkDaskConnection, 실패 시 Mock 폴백.
            // 현재 Phase 1: 무조건 Mock.
            return new AdLinkMockBoard(c.DeviceId, c.InputChannelCount, c.OutputChannelCount);
        }

        private static IIoBoardConnection CreateAdvantech(IoDeviceConfig c)
        {
            // Phase 3: DAQNavi (Automation.BDaq) 로드 시도 → 성공 시 AdvantechDaqNaviConnection.
            return new AdvantechMockBoard(c.DeviceId, c.InputChannelCount, c.OutputChannelCount);
        }
    }
}
