# VMS 홍보 데모 자동 녹화 (v1)
# VMS.VisionSetup 실행 → 창의 실제 물리 사각형(GetWindowRect, DPI-aware)을 구해
# 데스크톱 합성 캡처(gdigrab)에서 그 영역만 crop → F8(1회 재생) 자동 입력 →
# 데모 완료 마커 감지 시 ffmpeg 정상 종료. 멀티모니터/DPI/창위치/3D(DirectX) 모두 안전.
param(
    [string]$OutDir = "D:\Repo\VMS\docs\promo",
    [int]$TimeoutSec = 150
)
$ErrorActionPreference = "Stop"
trap { Write-Host "ERROR: $($_.Exception.Message)"; Write-Host $_.ScriptStackTrace; try { Get-Process VMS.VisionSetup,ffmpeg -ErrorAction SilentlyContinue | Stop-Process -Force } catch {}; exit 9 }

$sig = @'
using System;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int v);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
Add-Type $sig
try { [W]::SetProcessDpiAwareness(2) | Out-Null } catch {}  # 2 = Per-Monitor DPI Aware (물리픽셀)

# 1) ffmpeg 위치
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) {
    $cand = @(
        "D:\Repo\VMS\tools\ffmpeg\ffmpeg-*essentials*\bin\ffmpeg.exe",
        "$env:LOCALAPPDATA\Microsoft\WinGet\Links\ffmpeg.exe"
    )
    foreach ($c in $cand) {
        $f = Get-Item $c -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($f) { $ffmpeg = $f.FullName; break }
    }
}
if (-not $ffmpeg) { throw "ffmpeg 를 찾을 수 없습니다." }
Write-Host "ffmpeg: $ffmpeg"

# 2) 출력/마커
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$out = Join-Path $OutDir "vms_demo_v1.mp4"
if (Test-Path $out) { Remove-Item $out -Force }
$marker = Join-Path $env:TEMP "vms_demo_done.txt"
if (Test-Path $marker) { Remove-Item $marker -Force }

# 3) VMS 실행
$exe = "D:\Repo\VMS\VMS.VisionSetup\bin\Debug\net8.0-windows7.0\VMS.VisionSetup.exe"
if (-not (Test-Path $exe)) { throw "빌드 산출물이 없습니다: $exe" }
$app = Start-Process $exe -PassThru
Write-Host "VMS pid=$($app.Id) — 로딩 대기..."
Start-Sleep -Seconds 11

# 4) VMS 창 물리 사각형 → crop 영역 계산
$app.Refresh()
$hwnd = $app.MainWindowHandle
if ($hwnd -eq [IntPtr]::Zero) { $hwnd = [W]::FindWindow($null, "BODA VISION AI - OpenCvSharp Vision System") }
if ($hwnd -eq [IntPtr]::Zero) { throw "VMS 창 핸들을 찾지 못했습니다." }
$r = New-Object 'W+RECT'
[W]::GetWindowRect($hwnd, [ref]$r) | Out-Null
$vx = [W]::GetSystemMetrics(76)   # SM_XVIRTUALSCREEN
$vy = [W]::GetSystemMetrics(77)   # SM_YVIRTUALSCREEN
$cw = $r.Right  - $r.Left; $cw -= $cw % 2
$ch = $r.Bottom - $r.Top;  $ch -= $ch % 2
$cx = $r.Left - $vx
$cy = $r.Top  - $vy
Write-Host "window rect: L=$($r.Left) T=$($r.Top) R=$($r.Right) B=$($r.Bottom)  virtorigin=($vx,$vy)"
Write-Host "crop: ${cw}x${ch} @ ($cx,$cy)"

# 5) 창 포커스 (캡처 시작 전 전면화)
$wsh = New-Object -ComObject WScript.Shell
for ($i=0; $i -lt 6; $i++) { if ($wsh.AppActivate($app.Id)) { break }; Start-Sleep -Milliseconds 300 }
Start-Sleep -Milliseconds 500

# 6) ffmpeg 녹화 시작 (데스크톱 합성 캡처 → 창 영역 crop)
$ffArgs = @(
    "-y","-f","gdigrab","-framerate","30","-draw_mouse","0","-i","desktop",
    "-vf","crop=${cw}:${ch}:${cx}:${cy}",
    "-c:v","libx264","-preset","veryfast","-pix_fmt","yuv420p","-r","30","`"$out`""
)
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $ffmpeg
$psi.Arguments = ($ffArgs -join " ")
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
$psi.CreateNoWindow = $true
$ff = [System.Diagnostics.Process]::Start($psi)
Write-Host "ffmpeg 녹화 시작 pid=$($ff.Id)"
Start-Sleep -Milliseconds 800

# 7) F8 (1회 재생)
$wsh.AppActivate($app.Id) | Out-Null
Start-Sleep -Milliseconds 300
$wsh.SendKeys("{F8}")
Write-Host "데모 시작(F8). 완료 마커까지 녹화..."

# 8) 완료 마커 대기
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while (-not (Test-Path $marker) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
if (Test-Path $marker) { Write-Host "데모 완료 감지." } else { Write-Host "타임아웃 — 종료 진행." }
Start-Sleep -Milliseconds 900

# 9) ffmpeg 정상 종료
try { $ff.StandardInput.Write("q"); $ff.StandardInput.Flush() } catch {}
if (-not $ff.WaitForExit(10000)) { try { $ff.Kill() } catch {} }
Write-Host "녹화 종료."

# 10) 앱 종료
try { $app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 600; if (-not $app.HasExited) { $app.Kill() } } catch {}

# 11) 요약
Write-Host "================="
Write-Host "OUTPUT: $out"
if (Test-Path $out) {
    $size = [math]::Round((Get-Item $out).Length/1MB,1)
    $ffprobe = Join-Path (Split-Path $ffmpeg) "ffprobe.exe"
    $dur = if (Test-Path $ffprobe) { & $ffprobe -v error -show_entries format=duration -of default=nw=1:nk=1 $out } else { "?" }
    Write-Host "size: ${size} MB   duration: ${dur} sec"
}
exit 0
