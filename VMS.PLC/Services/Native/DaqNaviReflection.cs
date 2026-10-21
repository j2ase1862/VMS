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

        /// <summary>
        /// (int port, int bitOrCount, data) 3인자 오버로드를 고른다. GetMethod(name) 은 오버로드가 있으면
        /// AmbiguousMatchException 을 던져 로드 전체가 실패(→ 조용히 Mock 폴백)하므로 쓰지 않는다.
        /// </summary>
        internal static MethodInfo? FindMethod(Type? t, string name)
        {
            if (t is null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != name) continue;
                var ps = m.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int))
                    return m;
            }
            return null;
        }

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

        /// <summary>
        /// 비트 · 포트 I/O 에 쓰는 메서드 4개 중 SDK 에서 찾지 못한 이름 (전부 있으면 빈 문자열).
        /// 하나라도 없으면 그 I/O 는 동작할 수 없으므로 연결 단계에서 실패시킨다.
        /// </summary>
        public static string MissingIoMethods()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (_diReadBitMethod is null) missing.Add("InstantDiCtrl.ReadBit");
            if (_diReadAnyMethod is null) missing.Add("InstantDiCtrl.ReadAny");
            if (_doWriteBitMethod is null) missing.Add("InstantDoCtrl.WriteBit");
            if (_doWriteAnyMethod is null) missing.Add("InstantDoCtrl.WriteAny");
            return string.Join(", ", missing);
        }

        /// <summary>
        /// InstantDi/DoCtrl.Initialized — SelectedDevice 로 보드가 실제로 열렸는지. 속성이 없는 SDK 판이면 null.
        /// </summary>
        public static bool? IsInitialized(object instantCtrl)
        {
            var p = instantCtrl.GetType().GetProperty("Initialized", BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(instantCtrl) is bool b ? b : null;
        }

        // ─── 호출 + 반환 코드 검사 ───

        /// <summary>
        /// DAQNavi ErrorCode 가 오류인지 — Advantech 규약: Success = 0, 경고 0xA0000000 대역, 오류 0xE0000000 대역
        /// (enum 의 바탕은 int 라 오류 값은 음수). 경고는 동작은 했다는 뜻이라 성공으로 본다.
        /// 반환값이 없으면(void) 성공.
        /// </summary>
        internal static bool IsErrorCode(object? ret, out uint code)
        {
            code = 0;
            if (ret is null) return false;
            code = unchecked((uint)Convert.ToInt64(ret));
            return (code & 0xF0000000u) == 0xE0000000u;
        }

        /// <summary>
        /// 메서드를 호출하고 반환 ErrorCode 를 검사한다. 메서드가 없거나 오류 코드면 예외 —
        /// 종전처럼 조용히 넘기면 출력 신호가 안 나가도(입력을 못 읽어도) 호출자는 성공으로 안다.
        /// </summary>
        internal static void InvokeChecked(MethodInfo? method, object target, object?[] args, string operation)
        {
            if (method is null)
                throw new InvalidOperationException(
                    $"DAQNavi {operation} 메서드를 찾을 수 없습니다 — Automation.BDaq4.dll 판이 맞는지 확인하세요.");

            object? ret;
            try
            {
                ret = method.Invoke(target, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                throw new InvalidOperationException($"DAQNavi {operation} 실패: {tie.InnerException.Message}", tie.InnerException);
            }

            if (IsErrorCode(ret, out var code))
                throw new InvalidOperationException($"DAQNavi {operation} 실패 (ErrorCode {ret} = 0x{code:X8})");
        }

        // ─── Bit I/O ───

        /// <summary>InstantDiCtrl.ReadBit(int port, int bit, out byte data) reflection invoke.</summary>
        public static bool ReadBit(object instantDi, int port, int bit)
        {
            var args = new object?[] { port, bit, null };
            InvokeChecked(_diReadBitMethod, instantDi, args, $"ReadBit(port={port}, bit={bit})");
            // SDK 판에 따라 out 인자가 byte 또는 bool
            return args[2] switch
            {
                bool b => b,
                byte v => v != 0,
                _ => false,
            };
        }

        public static void WriteBit(object instantDo, int port, int bit, bool value)
        {
            // WriteBit(int port, int bit, byte data) — 인자 형식은 SDK 판의 시그니처를 따른다
            object data = _doWriteBitMethod?.GetParameters() is { Length: 3 } ps && ps[2].ParameterType == typeof(byte)
                ? (byte)(value ? 1 : 0)
                : value;
            InvokeChecked(_doWriteBitMethod, instantDo, new object?[] { port, bit, data }, $"WriteBit(port={port}, bit={bit}, {(value ? 1 : 0)})");
        }

        // ─── Port I/O (byte array) ───

        /// <summary>
        /// InstantDiCtrl.ReadAny(int startPort, int portCount, byte[] data) — 포트 단위 read.
        /// DAQNavi 의 포트는 8-bit. 32-bit 묶음이 필요하면 4개 포트 read.
        /// </summary>
        public static byte[] ReadPorts(object instantDi, int startPort, int portCount)
        {
            var data = new byte[portCount];
            InvokeChecked(_diReadAnyMethod, instantDi, new object?[] { startPort, portCount, data },
                $"ReadAny(port={startPort}, count={portCount})");
            return data;
        }

        public static void WritePorts(object instantDo, int startPort, int portCount, byte[] data)
        {
            InvokeChecked(_doWriteAnyMethod, instantDo, new object?[] { startPort, portCount, data },
                $"WriteAny(port={startPort}, count={portCount})");
        }
    }
}
