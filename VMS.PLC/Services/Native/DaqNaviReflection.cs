using System;
using System.IO;
using System.Reflection;

namespace VMS.PLC.Services.Native
{
    /// <summary>
    /// Advantech DAQNavi (Automation.BDaq4.dll) 동적 로드 + reflection helper.
    /// CSProj 의존성 회피 — vendor DLL 이 PATH 또는 설치 경로에 있으면 자동 로드.
    /// 미설치 환경에서는 EnsureLoaded() 가 false 반환 → Factory 가 Mock 폴백.
    ///
    /// 주의: reflection invoke 는 매 호출마다 비용 — InstantDiCtrl / InstantDoCtrl 의
    /// MethodInfo 는 한 번만 lookup 후 캐시. ReadBit / WriteBit 같은 빈번한 메서드도 캐싱.
    /// </summary>
    internal sealed class DaqNaviReflection
    {
        private const string AssemblyName = "Automation.BDaq4";
        private const string AssemblyFile = "Automation.BDaq4.dll";

        private static Assembly? _assembly;
        private static bool _loadAttempted;
        private static readonly object _loadLock = new();

        // 캐시된 Type / MethodInfo
        private static Type? _instantDiCtrlType;
        private static Type? _instantDoCtrlType;
        private static Type? _deviceInformationType;
        private static MethodInfo? _diReadBitMethod;
        private static MethodInfo? _doWriteBitMethod;
        private static MethodInfo? _diReadAnyMethod;
        private static MethodInfo? _doWriteAnyMethod;
        private static PropertyInfo? _selectedDeviceProperty;

        /// <summary>
        /// 한 번만 시도 — 첫 호출에서 Assembly.Load 시도, 실패 시 _assembly = null 유지.
        /// thread-safe.
        /// </summary>
        public static bool EnsureLoaded()
        {
            if (_assembly is not null) return true;
            if (_loadAttempted) return _assembly is not null;

            lock (_loadLock)
            {
                if (_assembly is not null) return true;
                _loadAttempted = true;

                try
                {
                    // GAC / PATH 에서 로드 시도
                    _assembly = Assembly.Load(AssemblyName);
                }
                catch
                {
                    try
                    {
                        // 실행 디렉토리 옆 lib 폴더 등 fallback
                        var basePath = AppDomain.CurrentDomain.BaseDirectory;
                        var candidate = Path.Combine(basePath, AssemblyFile);
                        if (File.Exists(candidate))
                            _assembly = Assembly.LoadFrom(candidate);
                    }
                    catch
                    {
                        _assembly = null;
                    }
                }

                if (_assembly is null) return false;

                try
                {
                    _instantDiCtrlType = _assembly.GetType("Automation.BDaq.InstantDiCtrl");
                    _instantDoCtrlType = _assembly.GetType("Automation.BDaq.InstantDoCtrl");
                    _deviceInformationType = _assembly.GetType("Automation.BDaq.DeviceInformation");

                    // ReadBit(int port, int bit, out bool data) → 시그니처 정확하지 않으면 null
                    _diReadBitMethod = FindMethod(_instantDiCtrlType, "ReadBit");
                    _doWriteBitMethod = FindMethod(_instantDoCtrlType, "WriteBit");
                    _diReadAnyMethod = FindMethod(_instantDiCtrlType, "ReadAny");
                    _doWriteAnyMethod = FindMethod(_instantDoCtrlType, "WriteAny");
                    _selectedDeviceProperty = _instantDiCtrlType?.GetProperty("SelectedDevice");

                    return _instantDiCtrlType is not null && _instantDoCtrlType is not null;
                }
                catch
                {
                    _assembly = null;
                    return false;
                }
            }
        }

        private static MethodInfo? FindMethod(Type? t, string name) =>
            t?.GetMethod(name, BindingFlags.Public | BindingFlags.Instance);

        // ─── Factory ───

        public static object? CreateInstantDi() =>
            _instantDiCtrlType is null ? null : Activator.CreateInstance(_instantDiCtrlType);

        public static object? CreateInstantDo() =>
            _instantDoCtrlType is null ? null : Activator.CreateInstance(_instantDoCtrlType);

        /// <summary>
        /// DeviceInformation 인스턴스 생성 — Advantech 의 디바이스 식별자 ("BID#0" 등) 또는
        /// (int deviceNumber) 생성자 사용. 사용 가능한 생성자 탐색.
        /// </summary>
        public static object? CreateDeviceInformation(string deviceDescription, int boardId)
        {
            if (_deviceInformationType is null) return null;
            try
            {
                // 우선 (string) 생성자 — DAQNavi 의 표준 "BID#N" 형식 직접 전달
                var ctorString = _deviceInformationType.GetConstructor(new[] { typeof(string) });
                if (ctorString is not null) return ctorString.Invoke(new object?[] { deviceDescription });

                // 폴백: (int) 생성자
                var ctorInt = _deviceInformationType.GetConstructor(new[] { typeof(int) });
                if (ctorInt is not null) return ctorInt.Invoke(new object?[] { boardId });

                // 기본 생성자 + SelectedDevice 직접 set
                return Activator.CreateInstance(_deviceInformationType);
            }
            catch { return null; }
        }

        public static void SetSelectedDevice(object instantCtrl, object deviceInfo)
        {
            _selectedDeviceProperty?.SetValue(instantCtrl, deviceInfo);
        }

        // ─── Bit I/O ───

        /// <summary>InstantDiCtrl.ReadBit(int port, int bit, out bool data) reflection invoke.</summary>
        public static bool ReadBit(object instantDi, int port, int bit)
        {
            if (_diReadBitMethod is null) return false;
            var args = new object?[] { port, bit, false };
            _diReadBitMethod.Invoke(instantDi, args);
            return args[2] is bool b && b;
        }

        public static void WriteBit(object instantDo, int port, int bit, bool value)
        {
            _doWriteBitMethod?.Invoke(instantDo, new object?[] { port, bit, value });
        }

        // ─── Port I/O (byte array) ───

        /// <summary>
        /// InstantDiCtrl.ReadAny(int startPort, int portCount, byte[] data) — 포트 단위 read.
        /// DAQNavi 의 포트는 8-bit. 32-bit 묶음이 필요하면 4개 포트 read.
        /// </summary>
        public static byte[] ReadPorts(object instantDi, int startPort, int portCount)
        {
            if (_diReadAnyMethod is null) return new byte[portCount];
            var data = new byte[portCount];
            _diReadAnyMethod.Invoke(instantDi, new object?[] { startPort, portCount, data });
            return data;
        }

        public static void WritePorts(object instantDo, int startPort, int portCount, byte[] data)
        {
            _doWriteAnyMethod?.Invoke(instantDo, new object?[] { startPort, portCount, data });
        }
    }
}
