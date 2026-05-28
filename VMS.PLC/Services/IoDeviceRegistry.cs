using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using VMS.PLC.Interfaces;

namespace VMS.PLC.Services
{
    /// <summary>
    /// IIoDeviceRegistry 의 ConcurrentDictionary 기반 구현. SequenceEngine 의 read-heavy
    /// 패턴(매 노드 실행마다 lookup) 에 lock 경합 최소화.
    ///
    /// 디바이스 인스턴스 lifecycle 은 외부(App.xaml.cs) 관리 — Registry 는 reference 만 보유,
    /// Dispose 책임 없음.
    /// </summary>
    public class IoDeviceRegistry : IIoDeviceRegistry
    {
        private readonly ConcurrentDictionary<string, IIoDevice> _devices = new();

        public void Register(IIoDevice device)
        {
            if (device is null) return;
            if (string.IsNullOrWhiteSpace(device.DeviceId)) return;
            _devices[device.DeviceId] = device;
        }

        public IIoDevice? Get(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return null;
            return _devices.TryGetValue(deviceId, out var d) ? d : null;
        }

        public IReadOnlyCollection<IIoDevice> AllDevices => _devices.Values.ToList();

        public IReadOnlyCollection<string> AllDeviceIds => _devices.Keys.ToList();
    }
}
