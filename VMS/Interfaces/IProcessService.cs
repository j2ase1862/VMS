namespace VMS.Interfaces
{
    public interface IProcessService
    {
        void LaunchProcess(string fileName, string? arguments = null);

        /// <summary>
        /// 추적 중인 자식 프로세스 정리 — 창이 있으면 정상 닫기 요청(미저장 작업 보호),
        /// 창 없는 잔존 프로세스만 강제 종료. VMS 종료 경로에서 호출.
        /// </summary>
        void ShutdownLaunchedProcesses(int gracefulTimeoutMs = 2000);
    }
}
