using System.Collections.Generic;

namespace VMS.PLC.Interfaces
{
    /// <summary>
    /// DeviceId → IIoDevice 매핑 레지스트리. App.xaml.cs 가 부팅 시 모든 IIoDevice
    /// (기본 PLC + IO 보드들) 를 등록하고, SequenceEngine 이 SequenceNodeConfig.DeviceId
    /// 로 lookup 하여 적절한 디바이스로 read/write 분기.
    ///
    /// Thread-safe — read 가 빈번하므로 ConcurrentDictionary 또는 lock 기반.
    /// </summary>
    public interface IIoDeviceRegistry
    {
        /// <summary>디바이스 등록 (중복 시 덮어쓰기).</summary>
        void Register(IIoDevice device);

        /// <summary>DeviceId 로 디바이스 조회. 없으면 null.</summary>
        IIoDevice? Get(string deviceId);

        /// <summary>등록된 모든 디바이스(snapshot).</summary>
        IReadOnlyCollection<IIoDevice> AllDevices { get; }

        /// <summary>등록된 모든 DeviceId(snapshot).</summary>
        IReadOnlyCollection<string> AllDeviceIds { get; }
    }
}
