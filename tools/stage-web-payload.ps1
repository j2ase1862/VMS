<#
.SYNOPSIS
  BODA.VMS.Web publish 산출물을 VMS.MasterSetup\WebPayload\ 로 스테이징 — MSI 동봉용.

.DESCRIPTION
  릴리즈 MSI 빌드 전(release_manual_procedure ② 앞)에 dev PC 에서 1회 실행한다.
  Web 리포를 self-contained(win-x64) 로 publish 한 뒤 WebPayload/ 에 미러링하면,
  VMS.MasterSetup.wixproj 가 payload 를 감지해 WebServer Feature 를 MSI 에 포함한다.

  payload 가 없으면 MSI 는 Web 미포함으로 빌드된다 (CI 표준 — 경고만 출력).
  WebPayload/ 는 .gitignore 대상 — 커밋 금지.

.PARAMETER WebRepoPath
  BODA.VMS.Web 리포 루트. 기본값 D:\Project\BODA.VMS.Web

.PARAMETER SkipBuild
  publish 를 건너뛰고 기존 산출물(-SourcePath)만 복사. 에어갭 등 특수 상황용.

.PARAMETER SourcePath
  -SkipBuild 시 사용할 기존 publish 폴더.

.EXAMPLE
  .\tools\stage-web-payload.ps1
  dotnet build VMS.MasterSetup/VMS.MasterSetup.wixproj -c Release
#>
[CmdletBinding()]
param(
    [string]$WebRepoPath = "D:\Project\BODA.VMS.Web",
    [switch]$SkipBuild,
    [string]$SourcePath
)

$ErrorActionPreference = "Stop"
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$PayloadDir = Join-Path $RepoRoot "VMS.MasterSetup\WebPayload"
$ExeName    = "BODA.VMS.Web.exe"

if ($SkipBuild) {
    if (-not $SourcePath -or -not (Test-Path (Join-Path $SourcePath $ExeName))) {
        Write-Error "-SkipBuild 사용 시 -SourcePath 에 $ExeName 포함 publish 폴더를 지정하세요."
        exit 1
    }
    $publishDir = $SourcePath
} else {
    $csproj = Join-Path $WebRepoPath "BODA.VMS.Web\BODA.VMS.Web\BODA.VMS.Web.csproj"
    if (-not (Test-Path $csproj)) {
        Write-Error "Web 리포를 찾지 못했습니다: $csproj  (-WebRepoPath 확인)"
        exit 1
    }
    $publishDir = Join-Path $env:TEMP "boda-web-payload-publish"
    Write-Host "[1/3] dotnet publish (self-contained win-x64)..." -ForegroundColor Green
    dotnet publish $csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Error "dotnet publish 실패 (exit=$LASTEXITCODE)"
        exit 1
    }
}

Write-Host "[2/3] WebPayload 미러링..." -ForegroundColor Green
New-Item -ItemType Directory -Force -Path $PayloadDir | Out-Null
# 운영 시크릿이 payload 에 섞이지 않도록 방어적 제외 (publish 산출물에는 원래 없음)
robocopy $publishDir $PayloadDir /MIR /XF "appsettings.Production.json" /NJH /NJS /NDL /NFL /NP | Out-Null
if ($LASTEXITCODE -ge 8) {
    Write-Error "robocopy 실패 (exit=$LASTEXITCODE)"
    exit 1
}

Write-Host "[3/3] 검증..." -ForegroundColor Green
if (-not (Test-Path (Join-Path $PayloadDir $ExeName))) {
    Write-Error "스테이징 실패 — $ExeName 이 WebPayload 에 없습니다."
    exit 1
}

# 동봉 Web 버전 식별 (릴리즈 노트 병기용)
$webVersion = "?"
$buildProps = Join-Path $WebRepoPath "Directory.Build.props"
if (Test-Path $buildProps) {
    $m = Select-String -Path $buildProps -Pattern "<Version>([^<]+)</Version>"
    if ($m) { $webVersion = $m.Matches[0].Groups[1].Value }
}
$files = Get-ChildItem $PayloadDir -Recurse -File
$sizeMB = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)

Write-Host ""
Write-Host "===== Web payload 스테이징 완료 =====" -ForegroundColor Cyan
Write-Host "  대상      : $PayloadDir"
Write-Host "  Web 버전  : $webVersion  (릴리즈 노트에 병기할 것)"
Write-Host "  파일/크기 : $($files.Count)개 / ${sizeMB}MB"
Write-Host "  다음 단계 : dotnet build VMS.MasterSetup/VMS.MasterSetup.wixproj -c Release"
exit 0   # robocopy 성공 코드(1~7)가 종료 코드로 새지 않도록 명시
