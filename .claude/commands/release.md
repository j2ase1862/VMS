# /release — VMS 릴리즈 발행

인자: 버전 (예: `/release 1.18.4`). 절차 상세는 docs/release_manual_procedure.md.

## 흐름

1. **bump 확인**: `Directory.Build.props` 세 곳이 대상 버전인지 확인. bump 는 릴리즈에
   들어갈 마지막 수정 PR 에 동승시킨다 (bump 전용 PR 은 수정 없는 릴리즈만).
2. **CI 그린 + 머지**: `gh pr checks <PR> --watch`(백그라운드) → 그린 확인 후
   `gh pr merge <PR> --merge` (`--auto` 금지 — 프라이빗+무료라 필수 체크 403).
   성공한 실행의 로그는 읽지 않는다. 실패 시에만 `gh run view --log-failed`.
3. **CI 대기 중 프리빌드(병렬)**: 브랜치 내용이 머지 후 master 와 동일하므로
   `tools\stage-web-payload.ps1` + 솔루션 빌드 + `VMS.MasterSetup.wixproj -t:Rebuild` 를
   백그라운드로 미리 돌린다 → 이후 `-SkipBuild` 로 재사용.
4. **릴리즈 노트 작성**: 이전 노트(`D:\VMS-Releases\VMS-<이전버전>\릴리즈노트_*.md`) 형식.
   포함 PR 은 `git log v<이전>..origin/master --oneline`. 동봉 Web 버전 병기(스테이징 출력).
5. **머지 후 master 에서 원커맨드 발행(백그라운드)**:
   ```powershell
   .\scripts\release.ps1 -Version <버전> -NotesFile <노트.md> -SkipBuild
   ```
   프리빌드가 없거나 머지 내용이 브랜치와 다르면 `-SkipBuild` 를 빼고 전체 빌드.
   스크립트가 MSI 크기 하한·자산 크기 일치·태그 중복을 검증하고, 검증 통과 전에는
   publish 하지 않는다. 실패 시 롤백은 절차서 하단.

## 금지 사항

- 태그 push 트리거 자동 릴리즈 재도입 금지 (v1.4.10 사고 — CI 에 Mech-Eye SDK 없음)
- CI 빌드 MSI 현장 배포 금지 (538MB 대가 그 증거)
- 자산 없는/불완전한 릴리즈 publish 금지 (인앱 업데이트 알림이 latest 를 조회)
