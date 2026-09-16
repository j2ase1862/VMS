$ErrorActionPreference = 'Continue'
$root = 'D:\Temp\mancap'
$log = "$root\_run3.log"
"START $(Get-Date -Format s)" | Out-File $log -Encoding utf8
$vs = 'D:\Repo\VMS\VMS.VisionSetup\bin\Debug\net8.0-windows7.0\VMS.VisionSetup.exe'
$inst = "$env:LOCALAPPDATA\BODA VISION AI\instances\mancap"
function Run-Capture($exe, $argList, $timeoutSec) {
  "RUN $exe $argList" | Out-File $log -Append -Encoding utf8
  $p = Start-Process -FilePath $exe -ArgumentList $argList -PassThru
  if (-not $p.WaitForExit($timeoutSec * 1000)) { "TIMEOUT -> kill" | Out-File $log -Append -Encoding utf8; try { $p.Kill() } catch {} }
  "EXIT $($p.ExitCode) $(Get-Date -Format s)" | Out-File $log -Append -Encoding utf8
  Start-Sleep -Seconds 2
}
# 장면 1: 레시피 로드 + 이미지 + 실행 (Feature Match 도구 선택)
$env:VMS_CAPTURE_RECIPE = "$inst\Recipes\recipe_apple_iphone17_pro.json"
$env:VMS_CAPTURE_STEP = '4'
$env:VMS_CAPTURE_IMAGE = "$inst\Recipes\RefImages\featurematch_c0e340cb-da24-453e-8694-c97de9bcdf94.png"
$env:VMS_CAPTURE_TOOL = '1'
Run-Capture $vs "--instance mancap --capture-fullpage `"$root\visionsetup_full_scene`"" 240
# 다이얼로그 (템플릿 갤러리 포함)
Remove-Item Env:VMS_CAPTURE_RECIPE, Env:VMS_CAPTURE_STEP, Env:VMS_CAPTURE_IMAGE, Env:VMS_CAPTURE_TOOL -ErrorAction SilentlyContinue
Run-Capture $vs "--instance mancap --capture-dialogs `"$root\visionsetup_dlg2`"" 240
"DONE $(Get-Date -Format s)" | Out-File $log -Append -Encoding utf8
