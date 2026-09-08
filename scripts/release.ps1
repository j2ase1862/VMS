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
    [int]$MinMsiMB = 300    # v1.31.0 부터 정상 ~340MB — 개발 PC 빌드 출력의 stale win-x64\ 폴더(1.3GB, 2026-06-01 잔재)가 v1.25.0~v1.30.0 MSI 에 함께 수확돼 846MB 로 부풀어 있었음 (Package.wxs Exclude 로 차단). 300 미만이면 런타임(self-contained) 또는 Web payload 누락 의심
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

# 네이티브 실행 파일(git/gh/dotnet) 호출 래퍼 — 성공 여부는 종료 코드로만 판정한다.
#
# PowerShell 5.1 은 호출자가 stderr 를 리다이렉트(`2>&1`, `*>&1`, 백그라운드 잡 캡처 등)하면
# 네이티브 stderr 한 줄마다 ErrorRecord(NativeCommandError) 를 만들고, 스크립트의
# $ErrorActionPreference='Stop' 아래에서는 그것이 종료 예외가 된다. git push 는 성공해도
# "To github.com:…" 진행 메시지를 stderr 로 내므로 (v1.27.0 발행 시 태그 push 직후 중단 사고)
# 호출 구간만 EAP 를 Continue 로 낮춰 stderr 를 출력으로만 흘리고, 종료 코드로 실패를 판정한다.
# 기본은 stdout/stderr 를 그대로 화면에 흘리고(빌드 로그 실시간), -Capture 면 stdout 줄을
# 모아 반환한다 (JSON 파싱 등) — 이때도 stderr 는 화면으로만 보낸다.
function Invoke-Native([string]$failMessage, [scriptblock]$body, [switch]$Capture) {
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = 0
    $captured = New-Object System.Collections.Generic.List[string]
    try {
        & $body 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) { Write-Host $_.ToString() }
            elseif ($Capture) { $captured.Add([string]$_) }
            else { Write-Host ([string]$_) }
        }
    }
    finally {
        $ErrorActionPreference = $prevEAP
    }
    if ($LASTEXITCODE -ne 0) { throw "$failMessage (exit $LASTEXITCODE)" }
    if ($Capture) { return ($captured -join "`n") }
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
    Step '솔루션 빌드' { Invoke-Native '솔루션 빌드 실패' { dotnet build VMS.sln -c Release } }
    Step 'MSI 빌드 (Rebuild)' {
        Invoke-Native 'MSI 빌드 실패' { dotnet build VMS.MasterSetup\VMS.MasterSetup.wixproj -c Release -t:Rebuild }
    }
}

# ── 2. MSI 크기 검증 + 체크섬 ──
$msiBytes = 0
Step 'MSI 검증 + SHA256' {
    if (-not (Test-Path $msiPath)) { throw "MSI 없음: $msiPath (-SkipBuild 인데 프리빌드가 없거나 버전 불일치)" }
    $script:msiBytes = (Get-Item $msiPath).Length
    $mb = [math]::Round($msiBytes / 1MB)
    if ($mb -lt $MinMsiMB) {
        throw "MSI ${mb}MB < 하한 ${MinMsiMB}MB — Web payload 누락(-t:Rebuild 필요) 또는 self-contained 런타임 누락 의심 (v1.31.0 기준 정상 ~340MB)"
    }
    Write-Host "    VMS-$Version.msi = ${mb}MB"
    $h = (Get-FileHash $msiPath -Algorithm SHA256).Hash.ToLower()
    "$h  VMS-$Version.msi" | Out-File "$msiPath.sha256" -Encoding ascii
}

# ── 3. draft 생성 + 자산 업로드 (1GB — 몇 분 소요) ──
Step "draft 생성 + 자산 업로드 ($repo)" {
    Invoke-Native 'draft 생성/업로드 실패' {
        gh release create $tag --repo $repo --draft `
            --title "BODA VMS v$Version" --notes-file $NotesFile `
            $msiPath "$msiPath.sha256"
    }
}

# ── 4. 자산 크기 일치 확인 → publish ──
Step '자산 검증 + publish' {
    $assets = Invoke-Native '릴리즈 자산 조회 실패' { gh release view $tag --repo $repo --json assets } -Capture | ConvertFrom-Json
    $remote = $assets.assets | Where-Object { $_.name -eq "VMS-$Version.msi" }
    if ($null -eq $remote) { throw '업로드된 MSI 자산이 없음 — publish 중단' }
    if ($remote.size -ne $script:msiBytes) {
        throw "자산 크기 불일치 (원격 $($remote.size) ≠ 로컬 $script:msiBytes) — 업로드 불완전, publish 중단"
    }
    Invoke-Native 'publish 실패' { gh release edit $tag --repo $repo --draft=false }
}

# ── 5. 소스 repo 태그 ──
Step '소스 repo 태그 push' {
    Invoke-Native '태그 생성 실패' { git tag $tag }
    # git push 는 성공 시에도 진행 메시지를 stderr 로 낸다 — Invoke-Native 가 종료 코드로만 판정
    Invoke-Native '태그 push 실패' { git push origin $tag }
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
