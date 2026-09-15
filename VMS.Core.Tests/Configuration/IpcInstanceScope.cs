using System;
using System.Threading;
using VMS.Camera.Configuration;

namespace VMS.Core.Tests.Configuration
{
    /// <summary>
    /// 시험이 도는 동안만 <b>이 실행에만 있는 인스턴스 이름</b>을 씌워, 커널 객체 이름(MMF·Mutex·Event)을
    /// 다른 모든 것과 떼어 놓는다. <see cref="IDisposable.Dispose"/> 에서 원래 상태로 되돌린다.
    ///
    /// <para><b>왜 필요한가.</b> IPC 이름은 <c>AppDataPaths.QualifyIpcName</c> 에서 나오고, 기본 인스턴스는
    /// 접미사 없는 <c>Local\VMS_SharedFrame_*</c> 를 그대로 쓴다 — 즉 <b>이 PC 에서 전역</b>이다. 그래서
    /// 시험이 기본 인스턴스로 돌면 같은 이름을 이렇게 나눠 쓰게 된다.</para>
    /// <list type="bullet">
    ///   <item>개발 PC 에서 <b>실제 VMS 가 떠 있으면</b> 그쪽 Writer 와 이름이 겹친다 — 시험의 Reader 가
    ///   남의 프레임을 읽거나, 시험의 Writer 가 "Reader 가 있다" 고 잘못 판단한다.</item>
    ///   <item><c>dotnet test</c> 를 두 번 겹쳐 돌리면 서로의 MMF 를 밟는다.</item>
    ///   <item>앞선 실행이 죽어 남은 커널 객체가 다음 실행에 딸려 온다.</item>
    /// </list>
    /// <para>셋 다 "다시 돌리면 통과" 하는 간헐 실패로 나타나, 원인을 좇기가 특히 나쁘다.
    /// 컬렉션으로 직렬화해도 <b>프로세스 밖</b>의 충돌은 막지 못한다 — 이름 자체를 갈라야 한다.</para>
    ///
    /// <para>인스턴스 이름은 <c>^[A-Za-z0-9_-]{1,32}$</c> 만 받으므로 짧은 16진수로 만든다.</para>
    /// </summary>
    public sealed class IpcInstanceScope : IDisposable
    {
        private static int _counter;
        private readonly string? _originalEnvValue;

        public IpcInstanceScope()
        {
            _originalEnvValue = Environment.GetEnvironmentVariable(AppDataPaths.EnvVarName);

            // 프로세스 ID + 증가 번호 → 같은 PC 의 다른 실행과도, 한 실행 안의 다른 범위와도 겹치지 않는다
            InstanceName = $"t{Environment.ProcessId:x}{Interlocked.Increment(ref _counter):x}";
            AppDataPaths.ResetForTests();
            AppDataPaths.Initialize(["--instance", InstanceName]);
        }

        /// <summary>이 범위가 쓰는 인스턴스 이름 — IPC 이름의 접미사가 된다.</summary>
        public string InstanceName { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataPaths.EnvVarName, _originalEnvValue);
            AppDataPaths.ResetForTests();
        }
    }
}
