using System.Runtime.InteropServices;
using System.Text;

namespace VMS.WeldTeach.Services;

/// <summary>
/// Occt.NET 래퍼(北京腾雪科技)가 STEP 리더/라이터 생성 시 띄우는 홍보 팝업
/// (윈도우 클래스 "Comet_PopupWindow") 을 화면에서 제거한다.
///
/// 팝업 소유 스레드는 메시지를 펌프하지 않아 WM_CLOSE/ShowWindow/SetWindowPos 등
/// 메시지 기반 접근이 전부 무효다 (실측). 유일하게 통하는 방법은 소유 스레드와 무관하게
/// 합성기가 처리하는 DWM 클로킹(DwmSetWindowAttribute + DWMWA_CLOAK) — 같은 프로세스의
/// 창에는 권한이 허용된다. 배경 감시 스레드가 0.1s 간격으로 클로킹한다.
/// 근본 해결은 커널 교체(README 라이선스 리스크 참조).
/// </summary>
internal static class VendorPopupSuppressor
{
    private const int DWMWA_CLOAK = 13;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lparam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static Thread? _watcher;

    public static void Install()
    {
        if (_watcher != null) return;
        _watcher = new Thread(Watch) { IsBackground = true, Name = "VendorPopupCloak" };
        _watcher.Start();
    }

    private static void Watch()
    {
        uint myPid = (uint)Environment.ProcessId;
        var cloaked = new HashSet<IntPtr>();
        var sb = new StringBuilder(64);
        while (true)
        {
            EnumWindows((hwnd, _) =>
            {
                if (cloaked.Contains(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != myPid) return true;
                sb.Clear();
                if (GetClassName(hwnd, sb, sb.Capacity) > 0 && sb.ToString() == "Comet_PopupWindow")
                {
                    int one = 1;
                    if (DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref one, sizeof(int)) == 0)
                        cloaked.Add(hwnd);
                }
                return true;
            }, IntPtr.Zero);
            Thread.Sleep(100);
        }
    }
}
