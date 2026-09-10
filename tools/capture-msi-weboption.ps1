# MSI 마법사 "설치 구성 선택"(WebOptionDlg) 화면 캡처 — 설치하지 않고 UI 만 띄워 PrintWindow 후 종료
param(
    [string]$Msi = "D:\Repo\VMS\VMS.MasterSetup\bin\Release\VMS-1.33.0.msi",
    [string]$Out = "D:\Temp\msi-cap\71_msi_weboption.png"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
}
"@
[W]::SetProcessDPIAware() | Out-Null
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null

$title = "BODA Vision System 설치"
$p = Start-Process msiexec.exe -ArgumentList @("/i", "`"$Msi`"") -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 500; $ui = Get-Process msiexec -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -eq $title } | Select-Object -First 1; if ($ui) { $h = [IntPtr]$ui.MainWindowHandle; $p = $ui; break } }
if ($h -eq [IntPtr]::Zero) { throw "마법사 창을 찾지 못했습니다: $title" }
"window found after $($i*0.5)s"

function CurrentHwnd {
    $ui = Get-Process msiexec -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle -eq $script:title } | Select-Object -First 1
    if ($ui) { $script:h = [IntPtr]$ui.MainWindowHandle; $script:p = $ui }
    return $script:h
}
function Page($hwnd) {
    # 준비 대화상자 → 본 대화상자로 창이 바뀌면 핸들이 무효가 된다 — 매번 다시 얻고, 실패하면 잠깐 뒤 재시도
    $root = $null
    for ($t = 0; $t -lt 20 -and -not $root; $t++) {
        try { $root = [System.Windows.Automation.AutomationElement]::FromHandle((CurrentHwnd)) } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $root) { return @() }
    # MSI 대화상자 컨트롤은 UIA 에 Pane 으로 노출된다 — 이름만 모은다
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $names = @(); foreach ($b in $all) { if ($b.Current.Name) { $names += $b.Current.Name } }
    return $names
}
function ClickButton($hwnd, $namePrefix) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($b.Current.Name -like "$namePrefix*") { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $b.Current.Name }
    }
    throw "버튼 없음: $namePrefix"
}

# Welcome → InstallDir → WebOption (설치 구성 선택). 준비("잠시 기다려")가 끝난 뒤에만 Next 를 누르고,
# 페이지가 바뀐 것을 확인한 뒤 다음 단계로 간다. VerifyReady 로 넘어가지 않게 최대 2번만 Next.
function WaitReady($hwnd) {
    for ($k = 0; $k -lt 60; $k++) {
        $n = Page $hwnd
        if (($n | Where-Object { $_ -like "*잠시 기다려*" -or $_ -like "*확인하는 중*" -or $_ -like "*마이그레이션하는 중*" }).Count -eq 0) { return $n }
        Start-Sleep -Milliseconds 500
    }
    return (Page $hwnd)
}
$found = $false
$names = WaitReady $h
for ($step = 0; $step -lt 3; $step++) {
    if (($names | Where-Object { $_ -like "*AI 학습 도구*" }).Count -gt 0) { $found = $true; break }
    if ($step -eq 2) { break }
    "page ${step}: [$($names -join ' | ')] -> Next"
    $before = ($names -join '|')
    # "다음(N)" 컨트롤(Win32 Button)에 BM_CLICK — 포커스·포그라운드와 무관
    $root = [System.Windows.Automation.AutomationElement]::FromHandle((CurrentHwnd))
    $btn = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "다음(N)")))
    if (-not $btn) { throw "다음(N) 컨트롤 없음" }
    $bh = [IntPtr]$btn.Current.NativeWindowHandle
    "  click Next hwnd=$bh"
    [W]::SendMessage($bh, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    for ($k = 0; $k -lt 40; $k++) { Start-Sleep -Milliseconds 500; $names = WaitReady $h; if (($names -join '|') -ne $before) { break } }
}
if (-not $found) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; throw "설치 구성 선택 화면에 도달하지 못했습니다" }
"reached WebOptionDlg: [$((Page $h) -join ' | ')]"

$h = CurrentHwnd
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
$r = New-Object W+R; [W]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Rt - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[W]::PrintWindow($h, $hdc, 2) | Out-Null   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"saved $Out ${w}x${hh}"

# 설치는 하지 않는다 — UI 프로세스만 종료 (VerifyReady 이전이라 시스템 변경 없음)
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Get-Process msiexec -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -eq $title } | Stop-Process -Force -ErrorAction SilentlyContinue
"done"
