# -*- coding: utf-8 -*-
"""관리자 매뉴얼 생성기 — BODA-VMS-User-Manual.html + _admin_weldteach_section.html
→ BODA-VMS-Admin-Manual.html

배경: 관리자판 §1~8 은 사용자 매뉴얼의 복사본이라 수동 복사 시 어긋남이 누적됐다
(docs 중복 정리, 2026-08-14). 이제 이 스크립트가 유일한 생성 경로다.

사용법:
    cd docs/manuals
    python gen_admin_manual.py

수정 규칙:
- §1~8 내용 수정 → BODA-VMS-User-Manual.html (진실 원본) → 본 스크립트 재실행
- §9 WeldTeach 수정 → _admin_weldteach_section.html → 본 스크립트 재실행
- BODA-VMS-Admin-Manual.html 직접 편집 금지 (재생성 시 유실)
"""
import io
import re
import sys
import datetime

USER = "BODA-VMS-User-Manual.html"
FRAG = "_admin_weldteach_section.html"
OUT = "BODA-VMS-Admin-Manual.html"


def fail(msg):
    print(f"[gen_admin_manual] 실패: {msg}", file=sys.stderr)
    sys.exit(1)


def main():
    html = io.open(USER, encoding="utf-8").read()
    frag = io.open(FRAG, encoding="utf-8").read()

    # 조각 분해 — TOC 항목 / 본문 섹션
    try:
        toc_entry = frag.split("<!-- TOC-ENTRY -->", 1)[1].split("<!-- SECTION -->", 1)[0].strip("\n")
        section = frag.split("<!-- SECTION -->", 1)[1].strip("\n")
    except IndexError:
        fail(f"{FRAG} 에 TOC-ENTRY / SECTION 마커가 없음")

    # 사용자 매뉴얼 버전·일자 파싱 (header-strip .sub)
    m = re.search(r'<div class="sub">버전 ([\d.]+) · (\d{4}-\d{2}-\d{2})', html)
    if not m:
        fail("사용자 매뉴얼 header-strip 에서 '버전 X · YYYY-MM-DD' 를 찾지 못함")
    user_ver, user_date = m.group(1), m.group(2)
    today = datetime.date.today().isoformat()

    # 1) <title>
    html, n = re.subn(r"<title>.*?</title>",
                      "<title>BODA VMS 관리자 매뉴얼 (내부)</title>", html, count=1)
    if n != 1:
        fail("<title> 치환 실패")

    # 2) 헤더 h1 + sub + 내부 전용 안내
    header_re = re.compile(
        r'<div class="header-strip">\s*<h1>[^<]*</h1>\s*<div class="sub">[^<]*</div>\s*</div>')
    admin_header = (
        '<div class="header-strip">\n'
        '  <h1>BODA VMS 관리자 매뉴얼</h1>\n'
        f'  <div class="sub">사용자 매뉴얼 v{user_ver} ({user_date}) 기반 + 내부 도구(WeldTeach) 수록 '
        f'· {today} 자동 생성</div>\n'
        '</div>\n\n'
        '<div class="callout warn">\n'
        '<strong>내부 전용 문서:</strong> 이 문서는 <code>gen_admin_manual.py</code> 가 사용자\n'
        '매뉴얼에서 <strong>자동 생성</strong>한 관리자판입니다. §1~8 은 사용자 매뉴얼과 동일하고,\n'
        '§9 에 GS 인증 범위에서 제외된 내부 도구 <strong>VMS.WeldTeach</strong> 를 추가로 다룹니다.\n'
        'GS 인증 제출물이 아니며 외부 배포하지 않습니다. <strong>이 파일을 직접 편집하지 말 것</strong>\n'
        '— §1~8 은 사용자 매뉴얼을, §9 는 <code>_admin_weldteach_section.html</code> 을 고친 뒤\n'
        '재생성하세요.\n'
        '</div>')
    html, n = header_re.subn(admin_header, html, count=1)
    if n != 1:
        fail("header-strip 치환 실패")

    # 3) TOC 에 §9 항목 삽입 — nav 닫힘 직전
    toc_close = "  </ol>\n</nav>"
    if html.count(toc_close) != 1:
        fail(f"TOC 닫힘 패턴이 1회가 아님 ({html.count(toc_close)}회)")
    html = html.replace(toc_close, toc_entry + "\n  </ol>\n</nav>", 1)

    # 4) 본문 §9 삽입 — </main> 직전
    main_close = "</main>"
    if html.count(main_close) != 1:
        fail(f"</main> 이 1회가 아님 ({html.count(main_close)}회)")
    html = html.replace(main_close, "\n" + section + "\n\n</main>", 1)

    # 검증 — 필수 앵커 존재
    for anchor in ('id="weldteach"', "#wt-limits", "9. VMS.WeldTeach"):
        if anchor not in html:
            fail(f"생성물 검증 실패: {anchor} 누락")

    io.open(OUT, "w", encoding="utf-8", newline="").write(html)
    print(f"[gen_admin_manual] OK → {OUT} (사용자 매뉴얼 v{user_ver} {user_date} 기반, {len(html):,} B)")


if __name__ == "__main__":
    main()
