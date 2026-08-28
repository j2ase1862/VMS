# VMS 릴리즈 원커맨드 발행 스크립트
#
# docs/release_manual_procedure.md ②~⑥단계(빌드→체크섬→draft→자산검증→publish→태그→보관→API확인)를
# 하나로 묶는다. ①(bump+CI+머지)은 PR 흐름이므로 이 스크립트 범위 밖.
#
# 사용 (리포지토리 루트, dev PC 전용 — CI 빌드 MSI는 현장 배포 금지):
#   .\scripts\release.ps1 -Version 1.18.4 -NotesFile 릴리즈노트_v1.18.4.md
#   .\scripts\release.ps1 -Version 1.18.4 -NotesFile 노트.md -SkipBuild   # CI 대기 중 프리빌드 재사용
#
# 안전장치:
#   - Directory.Build.props 버전 일치 확인 (bump 누락 방지)
#   - MSI 크기 하한 검증 (1,055MB대=Web payload 누락, 538MB대=SDK 누락 — 절차서 ② 참조)
#   - 자산 크기 일치 확인 전에는 publish 하지 않음 (자산 없는 릴리즈 publish 금지 규칙)
#   - 태그 트리거 자동 빌드는 의도적으로 없음 (v1.4.10 사고 — release.yml 폐지, PR #255)

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$NotesFile,
    [switch]$SkipBuild,
    [int]$MinMsiMB = 1100
)

$ErrorActionPreference = 'Stop'
$repo = 'j2ase1862/VMS-Releases'
$tag = "v$Version"
$msiPath = "VMS.MasterSetup\bin\Release\VMS-$Version.msi"
$total = [System.Diagnostics.Stopwatch]::StartNew()

function Step([string]$name, [scriptblock]$body) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Write-Host "==> $name" -ForegroundColor Cyan
    & $body
    Write-Host "    완료 ($([math]::Round($sw.Elapsed.TotalSeconds, 1))s)" -ForegroundColor DarkGray
}

# ── 0. 사전 검증 ──
Step '사전 검증' {
    if (-not (Test-Path $NotesFile)) { throw "릴리즈 노트 없음: $NotesFile" }

    $props = Get-Content Directory.Build.props -Raw
    if ($props -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
        throw "Directory.Build.props 의 <Version>이 $Version 이 아님 — bump 누락 또는 버전 인자 오타"
    }

    $branch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'master') { Write-Warning "현재 브랜치가 master 가 아님: $branch" }

    # 존재-확인 프로브 — EAP Stop 상태에서 네이티브 stderr 를 리다이렉트하면
    # PS 5.1 이 ErrorRecord 로 감싸 예외를 던지므로 프로브 동안만 완화한다
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    git rev-parse -q --verify "refs/tags/$tag" *> $null
    $tagExists = ($LASTEXITCODE -eq 0)
    gh release view $tag --repo $repo *> $null
    $releaseExists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $prevEAP

    if ($tagExists) { throw "태그 $tag 가 이미 존재 — 롤백 절차 확인 (절차서 하단)" }
    if ($releaseExists) { throw "$repo 에 $tag 릴리즈가 이미 존재" }
}

# ── 1. 빌드 (-SkipBuild 시 생략: CI 대기 중 프리빌드 재사용) ──
if (-not $SkipBuild) {
    Step 'Web payload 스테이징' { .\tools\stage-web-payload.ps1 }
    Step '솔루션 빌드' { dotnet build VMS.sln -c Release; if ($LASTEXITCODE -ne 0) { throw '솔루션 빌드 실패' } }
    Step 'MSI 빌드 (Rebuild)' {
        dotnet build VMS.MasterSetup\VMS.MasterSetup.wixproj -c Release -t:Rebuild
        if ($LASTEXITCODE -ne 0) { throw 'MSI 빌드 실패' }
    }
}

# ── 2. MSI 크기 검증 + 체크섬 ──
$msiBytes = 0
Step 'MSI 검증 + SHA256' {
    if (-not (Test-Path $msiPath)) { throw "MSI 없음: $msiPath (-SkipBuild 인데 프리빌드가 없거나 버전 불일치)" }
    $script:msiBytes = (Get-Item $msiPath).Length
    $mb = [math]::Round($msiBytes / 1MB)
    if ($mb -lt $MinMsiMB) {
        throw "MSI ${mb}MB < 하한 ${MinMsiMB}MB — 1,055MB대=Web payload 누락(-t:Rebuild 필요), 538MB대=SDK 누락 의심"
    }
    Write-Host "    VMS-$Version.msi = ${mb}MB"
    $h = (Get-FileHash $msiPath -Algorithm SHA256).Hash.ToLower()
    "$h  VMS-$Version.msi" | Out-File "$msiPath.sha256" -Encoding ascii
}

# ── 3. draft 생성 + 자산 업로드 (1GB — 몇 분 소요) ──
Step "draft 생성 + 자산 업로드 ($repo)" {
    gh release create $tag --repo $repo --draft `
        --title "BODA VMS v$Version" --notes-file $NotesFile `
        $msiPath "$msiPath.sha256"
    if ($LASTEXITCODE -ne 0) { throw 'draft 생성/업로드 실패' }
}

# ── 4. 자산 크기 일치 확인 → publish ──
Step '자산 검증 + publish' {
    $assets = gh release view $tag --repo $repo --json assets | ConvertFrom-Json
    $remote = $assets.assets | Where-Object { $_.name -eq "VMS-$Version.msi" }
    if ($null -eq $remote) { throw '업로드된 MSI 자산이 없음 — publish 중단' }
    if ($remote.size -ne $script:msiBytes) {
        throw "자산 크기 불일치 (원격 $($remote.size) ≠ 로컬 $script:msiBytes) — 업로드 불완전, publish 중단"
    }
    gh release edit $tag --repo $repo --draft=false
    if ($LASTEXITCODE -ne 0) { throw 'publish 실패' }
}

# ── 5. 소스 repo 태그 ──
Step '소스 repo 태그 push' {
    git tag $tag
    git push origin $tag
    if ($LASTEXITCODE -ne 0) { throw '태그 push 실패' }
}

# ── 6. 로컬 보관 + 최종 확인 ──
Step '로컬 보관 (D:\VMS-Releases)' {
    $dir = "D:\VMS-Releases\VMS-$Version"
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item "$msiPath*" $dir\ -Force
    # 노트를 보관 폴더에서 직접 작성한 경우 자기 복사가 IOException — 스킵
    $notesResolved = (Resolve-Path $NotesFile).Path
    if ((Split-Path $notesResolved -Parent) -ne $dir) {
        Copy-Item $notesResolved $dir\ -Force
    }
    $archived = (Get-FileHash "$dir\VMS-$Version.msi" -Algorithm SHA256).Hash.ToLower()
    $expected = ((Get-Content "$msiPath.sha256") -split '\s+')[0]
    if ($archived -ne $expected) { throw '보관본 해시 불일치' }
}

Step '인앱 업데이트 API 확인' {
    $latest = (Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest").tag_name
    if ($latest -ne $tag) { throw "latest API 가 $latest 반환 (기대: $tag)" }
}

Write-Host ""
Write-Host "✅ v$Version 발행 완료 — 총 $([math]::Round($total.Elapsed.TotalMinutes, 1))분" -ForegroundColor Green
Write-Host "   https://github.com/$repo/releases/tag/$tag"
