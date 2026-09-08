# VMS 릴리즈 발행 절차 (수동 발행 표준)

문서 버전: v1.0 (2026-07-27, v1.5.3 발행 기준)

> 관련 문서:
> - 인앱 업데이트 알림·업그레이드 정책: [update_notifier_and_upgrade_policy.md](update_notifier_and_upgrade_policy.md)
> - MSI 빌드 상세: [msi_build_guide.md](msi_build_guide.md)
>
> 이 문서는 dev PC 운영자가 매 릴리즈마다 따라가는 **실행 절차서**다.
> 사본이 dev PC `D:\VMS-Releases\릴리즈_발행_절차.md` 에도 있음 — 갱신 시 양쪽 동기화.

## 개요

| 항목 | 값 |
|---|---|
| 소스 저장소 | `j2ase1862/VMS` (private 전환 예정) — 코드 · bump PR · 태그 |
| 배포 저장소 | `j2ase1862/VMS-Releases` (**public**) — 릴리즈 발행 · MSI 다운로드 · 인앱 업데이트 알림 조회 |
| 버전 소스 | `Directory.Build.props` 단 한 곳 (Assembly + MSI ProductVersion 자동 동기화) |
| 버전 규칙 | feat 포함 = MINOR (1.5.x → 1.6.0) / fix만 = PATCH / MAJOR는 수동 판단 |
| 빌드 PC | **반드시 dev PC** — CI 빌드에는 Mech-Eye SDK가 없어 Mech-Mind 카메라 미지원 |
| 로컬 보관 | dev PC `D:\VMS-Releases\VMS-<버전>\` (bin\Release는 clean 시 소실되므로 필수) |

모든 명령은 리포지토리 루트에서 실행. 아래 예시는 v1.5.4 발행 기준 — 버전만 바꿔서 사용.

---

## ⓪ 원커맨드 발행 — scripts/release.ps1 (2026-08-29 도입)

②~⑥단계는 스크립트 하나로 대체할 수 있다 (① bump+CI+머지와 ③ 노트 작성은 종전대로):

```powershell
.\scripts\release.ps1 -Version 1.18.4 -NotesFile 릴리즈노트_v1.18.4.md
.\scripts\release.ps1 -Version 1.18.4 -NotesFile 노트.md -SkipBuild   # 프리빌드 재사용
```

- 단계 사이 사람 개입(확인·다음 명령 입력) 대기를 제거한 것 — 각 단계의 검증
  (버전 일치·MSI 크기 하한·자산 크기 일치·태그 중복)은 스크립트가 수행하고,
  검증 실패 시 publish 전에 중단한다.
- `-SkipBuild`: CI 대기 중 릴리즈 브랜치에서 MSI 를 미리 빌드해 두고 머지 후
  재사용 (브랜치 내용 = 머지 후 master 일 때만).
- 네이티브 명령(git/gh/dotnet)은 스크립트 내부의 `Invoke-Native` 래퍼가 **종료 코드로만**
  성공을 판정한다. PowerShell 5.1 은 stderr 가 리다이렉트된 상태(`*>&1`, 백그라운드 잡,
  다른 프로세스에서의 캡처)에서 네이티브 stderr 를 오류 레코드로 승격시키는데, git push 는
  성공해도 "To github.com:…" 를 stderr 로 내므로 래퍼 없이는 태그 push 직후 중단된다
  (v1.27.0 발행 사고 — publish·태그는 성공, 로컬 보관·latest 확인만 수동 마무리했다).
  래퍼 덕분에 백그라운드·리다이렉트 실행도 안전하지만, 그래도 출력은 그대로 두는 편이 읽기 쉽다.
- 태그 push 트리거 자동 빌드는 의도적으로 도입하지 않았다 (하단 v1.4.10 사고 규칙).
- Claude Code 세션에서는 `/release <버전>` 슬래시 커맨드가 이 흐름 전체(프리빌드
  병렬화 포함)를 안내한다 (.claude/commands/release.md).

수동 단계별 실행이 필요할 때(부분 재시도·검증 실습)는 아래 ②~⑥을 따른다.

---

## ① 버전 bump — 릴리즈에 포함될 마지막 수정 PR 에 함께 태운다 (2026-08-28 개정)

> 과거에는 bump 전용 PR(chore/release-vX.Y.Z)을 따로 만들어 CI 를 릴리즈당 2회
> 기다렸다. bump 는 `Directory.Build.props` 세 줄이라 CI 가 따로 검증할 것이 없으므로,
> **릴리즈에 들어갈 마지막 수정 PR 의 브랜치에 bump 커밋을 추가**해 CI 1회로 끝낸다.
> (수정 없이 버전만 올리는 릴리즈라면 종전처럼 bump 전용 PR 을 만든다)

릴리즈 대상 수정 PR 브랜치에서 `Directory.Build.props` 세 곳 수정:

```xml
<Version>1.5.4</Version>
<AssemblyVersion>1.5.4.0</AssemblyVersion>
<FileVersion>1.5.4.0</FileVersion>
```

```powershell
git add Directory.Build.props
git commit -m "chore(release): bump version to 1.5.4"
git push
```

CI(Build & Test) 통과 확인 후 머지 — CI 러너에는 카메라 SDK 가 없어 dev PC 가
검증 못 하는 **SDK 미탑재 컴파일 경로**를 CI 만 확인하므로 그린 확인은 생략하지 않는다:

```powershell
gh pr checks <PR번호> --watch     # pass 될 때까지 대기 (약 10분)
gh pr merge <PR번호> --merge
git checkout master
git pull --ff-only origin master
```

## ② MSI 빌드 + 체크섬 (dev PC)

```powershell
.\tools\stage-web-payload.ps1        # Web 서버 동봉 payload 스테이징 (동봉 Web 버전 출력 — ③에서 병기)
dotnet build VMS.sln -c Release
dotnet build VMS.MasterSetup\VMS.MasterSetup.wixproj -c Release
```

산출물: `VMS.MasterSetup\bin\Release\VMS-<버전>.msi` (**v1.31.0 기준 약 340MB** — self-contained 런타임 + Web 서버 동봉 + Mech-Mind 포함 정상.
`release.ps1` 의 하한 `-MinMsiMB 300` 미만이면 Web payload 누락(스테이징 미실행 또는 증분 빌드 — `-t:Rebuild` 사용) 또는 런타임 누락 의심.
※ v1.25.0~v1.30.0 의 "정상 846MB" 는 개발 PC 빌드 출력에 남아 있던 stale `win-x64\` 폴더(1.3GB, 2026-06-01 잔재)가 함께 수확된
결과였다 — Package.wxs 의 `<Exclude>` 로 차단(v1.31.0). Web 동봉 상세: [msi_build_guide.md §13](msi_build_guide.md),
AI 학습 도구 Feature·인증 빌드(`-p:ExcludeAiTools=true` → `VMS-<버전>-cert.msi`): [msi_build_guide.md §14](msi_build_guide.md))

```powershell
$h = (Get-FileHash VMS.MasterSetup\bin\Release\VMS-1.5.4.msi -Algorithm SHA256).Hash.ToLower()
"$h  VMS-1.5.4.msi" | Out-File VMS.MasterSetup\bin\Release\VMS-1.5.4.msi.sha256 -Encoding ascii
```

## ③ 릴리즈 노트 작성

이전 버전 노트(dev PC `D:\VMS-Releases\VMS-<이전버전>\릴리즈노트_*.md`) 형식 참고:

- 제목: `# BODA VMS v1.5.4`
- 한 줄 요약 → `## 새 기능`/`## 수정` (PR 번호 병기) → `## 설치 안내` (기존 v1.4.10 이상은 MSI 실행만으로 업그레이드, 레시피 호환)
- 동봉 Web 서버 버전 병기 (② 스테이징 스크립트 출력값. 예: "동봉 BODA.VMS.Web v1.1.0")

포함할 PR 목록 확인:

```powershell
git log v1.5.3..origin/master --oneline
```

## ④ VMS-Releases에 draft 생성 → 자산 확인 → publish

⚠️ `--repo j2ase1862/VMS-Releases` 필수 — 소스 repo에 만들면 안 됨.

```powershell
gh release create v1.5.4 --repo j2ase1862/VMS-Releases --draft `
  --title "BODA VMS v1.5.4" --notes-file 릴리즈노트_v1.5.4.md `
  VMS.MasterSetup\bin\Release\VMS-1.5.4.msi `
  VMS.MasterSetup\bin\Release\VMS-1.5.4.msi.sha256
```

(1GB 업로드 — 몇 분 소요)

자산이 온전히 올라갔는지 확인 후 publish:

```powershell
gh release view v1.5.4 --repo j2ase1862/VMS-Releases --json assets
# MSI size가 로컬 파일 크기(바이트)와 일치하는지 확인
gh release edit v1.5.4 --repo j2ase1862/VMS-Releases --draft=false
```

**규칙: 자산 없는 릴리즈를 publish 하지 말 것** — 현장 VMS가 시작 시 latest를 조회하므로, 자산 없이 publish 되면 다운로드 불가한 업데이트 알림이 뜬다.

## ⑤ 소스 repo에 태그 push

**잊기 쉬운 단계.** 릴리즈는 VMS-Releases 쪽에 생기므로 소스 repo 태그는 별도로 만들어야 함 (다음 릴리즈 때 `git log v1.5.4..` 범위 계산과 주간 자동 루틴이 이 태그를 기준으로 함):

```powershell
git tag v1.5.4
git push origin v1.5.4
```

태그 push로 CI가 돌지 않는 것이 정상 (release.yml 폐지됨 — PR #255).

## ⑥ 로컬 보관 + 최종 확인

```powershell
New-Item -ItemType Directory -Force D:\VMS-Releases\VMS-1.5.4
Copy-Item VMS.MasterSetup\bin\Release\VMS-1.5.4.msi* D:\VMS-Releases\VMS-1.5.4\
# 릴리즈노트 md + 현장검증 체크리스트 md 도 같은 폴더에 저장
Get-FileHash D:\VMS-Releases\VMS-1.5.4\VMS-1.5.4.msi -Algorithm SHA256   # 해시 재검증
```

최종 확인 — 인앱 알림이 보는 익명 API가 새 버전을 반환하는지:

```powershell
(Invoke-RestMethod "https://api.github.com/repos/j2ase1862/VMS-Releases/releases/latest").tag_name
# → v1.5.4
```

---

## 실수했을 때 (롤백)

```powershell
gh release delete v1.5.4 --repo j2ase1862/VMS-Releases --yes   # 릴리즈 삭제
git push --delete origin v1.5.4; git tag -d v1.5.4             # 소스 태그 제거
```

인앱 체커는 캐시가 없어 삭제 즉시 반영됨. 버전을 다시 올릴 때는 같은 번호 재사용 가능.

## 과거 사고에서 나온 규칙 (요약)

- **v1.4.10 사고**: 태그 push CI가 수동 발행 MSI를 CI 빌드(Mech-Mind 없음)로 덮어씀 → release.yml 폐지로 근원 제거. 순서는 항상 *빌드 → draft(자산 포함) → publish → 태그*.
- **CI 빌드 MSI는 현장 배포 금지** — Mech-Eye SDK 미포함 (538MB대가 그 증거).
- 현장 배포는 VMS-Releases에서 직접 다운로드 (USB 교체 방식은 v1.5.2부터 폐지).

## 변경 이력

| 버전 | 일자 | 주요 변경 |
|---|---|---|
| v1.1 | 2026-08-29 | ⓪ 원커맨드 발행(scripts/release.ps1) + /release 커맨드 도입 — ②~⑥ 자동화, 검증 후 publish |
| v1.0 | 2026-07-27 | 최초 작성 — v1.5.3 발행 기준 수동 발행 6단계 + 롤백 + 사고 규칙 |
