# VMS.Core.Contracts NuGet 패키지 생성 → 로컬 피드 (기본 D:\Repo\nuget-local)
#
# 사용:
#   .\tools\pack-contracts.ps1                       # Directory.Build.props 의 버전으로 pack
#   .\tools\pack-contracts.ps1 -Feed D:\nuget        # 다른 피드 폴더
#   .\tools\pack-contracts.ps1 -Suffix dev           # 1.31.0-dev.20260908T1530 같은 프리릴리즈 (개발 중 반복 pack)
#
# MLOps 솔루션(BODA.VMS.MLOps 등)은 nuget.config 에 이 피드를 추가하고
#   <PackageReference Include="VMS.Core.Contracts" Version="1.31.*" />
# 로 참조한다. 상세: docs\mlops_contracts_package.md
param(
    [string]$Feed = "D:\Repo\nuget-local",
    [string]$Suffix = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "VMS.Core.Contracts\VMS.Core.Contracts.csproj"

New-Item -ItemType Directory -Force $Feed | Out-Null

$args = @("pack", $proj, "-c", $Configuration, "-p:PackageOutputPath=$Feed", "--nologo")
if ($Suffix) {
    $stamp = Get-Date -Format "yyyyMMddTHHmm"
    $args += "-p:VersionSuffix=$Suffix.$stamp"
}

Write-Host "==> dotnet $($args -join ' ')"
& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "pack 실패 (exit $LASTEXITCODE)" }

$latest = Get-ChildItem (Join-Path $Feed "VMS.Core.Contracts.*.nupkg") | Where-Object { $_.Name -notmatch '\.snupkg$' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "==> 생성: $($latest.FullName) ($([math]::Round($latest.Length / 1KB)) KB)"
Write-Host "    소비 측: dotnet nuget add source `"$Feed`" -n vms-local  (1회) → PackageReference VMS.Core.Contracts"
