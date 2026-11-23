# BODA VMS 사내 매뉴얼 (확장 옵션 포함)

영업 · 엔지니어용 사내 매뉴얼입니다. GS 제출 매뉴얼(`docs/manuals/src` → `docs/gs`)은 **기본 구성만** 설명하고 동결되어 있으므로,
그 사본을 이 폴더로 가져와 확장 옵션(◆)과 사내 전용 내용을 더했습니다. **제출본 파일과 그 생성 스크립트는 건드리지 않습니다.**

## 만들기

```bash
python docs/manuals/internal/gen_tools_chapter.py   # 6장 (tools_prose.json + docs/gs/pipeline/_tool_params.json) — PIL 필요
python docs/manuals/internal/gen_appendix.py        # 13장 부록 A~E
python docs/manuals/internal/build_internal.py      # → BODA-VMS-Internal-Manual.html (그림은 상대 경로)
python docs/manuals/internal/build_internal.py --embed <출력.html>   # 그림을 품은 한 파일 (배포용)
```

`build_internal.py` 는 끝에 없는 그림 · 겹친 id · 찾지 못한 § 참조를 알려 줍니다. 셋 다 비어야 합니다.

## 파일

| 무엇 | 어디 |
|---|---|
| 장 조각 | `src/NN_*.html` (00 머리 · 01~13 장 · 14 부록 추가분) |
| 6장 도구 설명 | `tools_prose.json` → `gen_tools_chapter.py` (긴 패널 그림은 `figs/split/` 에 만듦 — 동결된 `docs/gs/screenshots` 에 쓰지 않음) |
| 부록 | `gen_appendix.py` → `src/13_appendix.html`, 추가분 `src/14_appendix_extra.html` |
| 용어집 | `glossary.html` (13.8 의 `<!--GLOSSARY-->` 자리) |
| 영상 번호 | `videos.json` — {절 id 또는 "h4:소제목 앞부분": 영상 번호}. 6장 도구 소제목은 V-6-n 자동 |
| 사내판에서만 쓰는 그림 | `figs/` (`figs/history/` 는 제출본 정리 때 지운 그림을 git 이력에서 되살린 것) |

## 표기 규칙

- **◆** = 라이선스 확장 옵션이 있어야 보이는 기능(§1.6). 빌드가 설명 풍선을 붙입니다.
- `§3.5` 처럼 쓰면 빌드가 그 절로 가는 링크로 바꿉니다. 절 번호는 제목 글자에서 읽으므로 절을 옮기면 제목 번호와 참조를 함께 고칩니다.
- 8장(Web) · 9장(MLOps)은 요약입니다. 각 절은 같은 폴더에 둔 별책
  `BODA-VMS-Web-User-Manual.html` · `BODA_VMS_MLOps_사용자매뉴얼_v2.0.html` 의 해당 절로 링크합니다 — 배포할 때 세 파일을 한 폴더에 둡니다.

## 출발점

2026-11-11 제출본(v3.1, BODA VMS v1.48.1)에서 시작해, 확장 옵션을 지운 두 커밋을 되살렸습니다.
- `1625f3d` (10-30, MLOps · 로봇 · 3D 촬영 · 멀티뷰 · 핸드아이 삭제) — 3-way 역적용 후 충돌 45곳 손으로 정리. 제품명 · 설치 폴더 같은 이름 바꿈은 제출본 쪽을 따름.
- `61c0a7e` (11-02, PLC · 롤러 검사 삭제) — `extension/plc.html` 과 그 커밋의 삭제분으로 되살림.
- 절 번호는 옛 13장 구성(9장 MLOps · 10장 딥러닝 · 11 유지보수 · 12 문제 조치 · 13 부록)입니다.

제출본에서 이후 문장을 고치면 이 폴더에는 자동으로 들어오지 않습니다 — 필요한 것만 옮깁니다.
2026-11-23 에 제출본 v3.2(BODA VMS v1.48.1 build 7)의 개정을 옮겼습니다(REVIEW_NOTES 0-1). 6장 · 부록 표는 제출본과 같은 `(Expert)` 표기를 씁니다.
