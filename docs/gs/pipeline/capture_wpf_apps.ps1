# WPF 앱 매뉴얼용 캡처 일괄 실행 (DEBUG 빌드, --instance mancap 격리)
$ErrorActionPreference = 'Continue'
$root = 'D:\Temp\mancap'
New-Item -ItemType Directory -Force $root | Out-Null
$log = "$root\_run.log"
"START $(Get-Date -Format s)" | Out-File $log -Encoding utf8

# 격리 인스턴스 준비: 기본 인스턴스의 Recipes 를 복사 (읽기 전용 캡처용)
$la = $env:LOCALAPPDATA + '\BODA VISION AI'
$inst = "$la\instances\mancap"
if (-not (Test-Path "$inst\Recipes")) {
  New-Item -ItemType Directory -Force "$inst\Recipes" | Out-Null
  Copy-Item "$la\Recipes\*" "$inst\Recipes\" -Recurse -Force
  "copied recipes" | Out-File $log -Append -Encoding utf8
}

function Run-Capture($exe, $argList, $timeoutSec) {
  "RUN $exe $argList" | Out-File $log -Append -Encoding utf8
  $p = Start-Process -FilePath $exe -ArgumentList $argList -PassThru
  if (-not $p.WaitForExit($timeoutSec * 1000)) {
    "TIMEOUT -> kill" | Out-File $log -Append -Encoding utf8
    try { $p.Kill() } catch {}
  }
  "EXIT $($p.ExitCode) $(Get-Date -Format s)" | Out-File $log -Append -Encoding utf8
  Start-Sleep -Seconds 2
}

$vs  = 'D:\Repo\VMS\VMS.VisionSetup\bin\Debug\net8.0-windows7.0\VMS.VisionSetup.exe'
$vms = 'D:\Repo\VMS\VMS\bin\Debug\net8.0-windows7.0\VMS.exe'
$aps = 'D:\Repo\VMS\VMS.AppSetup\bin\Debug\net8.0-windows\VMS.AppSetup.exe'
$dl  = 'D:\Repo\VMS\VMS.DeepLearning\bin\Debug\net8.0-windows7.0\VMS.DeepLearning.exe'

Run-Capture $vs  "--instance mancap --capture-fullpage `"$root\visionsetup_full`"" 300
Run-Capture $vs  "--instance mancap --capture-toolpanels `"$root\toolpanels`"" 600
Run-Capture $vs  "--instance mancap --capture-dialogs `"$root\visionsetup_dlg`"" 300
Run-Capture $vs  "--instance mancap --capture-controls `"$root\visionsetup_ctl`"" 300
Run-Capture $vms "--instance mancap --capture-controls `"$root\vms_ctl`"" 300
Run-Capture $vms "--instance mancap --capture-dialogs `"$root\vms_dlg`"" 300
Run-Capture $aps "--capture-controls `"$root\appsetup_ctl`"" 300
Run-Capture $aps "--capture-fullpage `"$root\appsetup_full`"" 300
Run-Capture $dl  "--capture-controls `"$root\dl_ctl`"" 300
Run-Capture $dl  "--capture-dialogs `"$root\dl_dlg`"" 300

"DONE $(Get-Date -Format s)" | Out-File $log -Append -Encoding utf8
Get-ChildItem $root -Directory | ForEach-Object { "$($_.Name): $((Get-ChildItem $_.FullName -File).Count) files" } | Out-File $log -Append -Encoding utf8
