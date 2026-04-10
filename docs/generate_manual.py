"""VMS 딥러닝 라벨링 & 학습 매뉴얼 Word 문서 생성 스크립트"""

from docx import Document
from docx.shared import Pt, Inches, Cm, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.oxml.ns import qn
import os

FONT_NAME = '맑은 고딕'

def set_run_font(run, font_name=FONT_NAME):
    """run에 한글 폰트를 명시적으로 설정 (eastAsia + ascii)"""
    run.font.name = font_name
    r = run._element
    rPr = r.get_or_add_rPr()
    rFonts = rPr.find(qn('w:rFonts'))
    if rFonts is None:
        rFonts = rPr.makeelement(qn('w:rFonts'), {})
        rPr.insert(0, rFonts)
    rFonts.set(qn('w:eastAsia'), font_name)
    rFonts.set(qn('w:ascii'), font_name)
    rFonts.set(qn('w:hAnsi'), font_name)

def set_cell_shading(cell, color):
    """셀 배경색 설정"""
    shading = cell._element.get_or_add_tcPr()
    shading_elm = shading.makeelement(qn('w:shd'), {
        qn('w:fill'): color,
        qn('w:val'): 'clear'
    })
    shading.append(shading_elm)

def add_styled_table(doc, headers, rows, col_widths=None):
    """스타일이 적용된 테이블 추가"""
    table = doc.add_table(rows=1 + len(rows), cols=len(headers))
    table.alignment = WD_TABLE_ALIGNMENT.CENTER
    table.style = 'Table Grid'

    # 헤더
    for i, header in enumerate(headers):
        cell = table.rows[0].cells[i]
        cell.text = header
        for p in cell.paragraphs:
            for run in p.runs:
                run.bold = True
                run.font.size = Pt(9)
                run.font.color.rgb = RGBColor(0xFF, 0xFF, 0xFF)
                set_run_font(run)
            p.alignment = WD_ALIGN_PARAGRAPH.CENTER
        set_cell_shading(cell, '2B579A')

    # 데이터
    for r_idx, row in enumerate(rows):
        for c_idx, val in enumerate(row):
            cell = table.rows[r_idx + 1].cells[c_idx]
            cell.text = val
            for p in cell.paragraphs:
                for run in p.runs:
                    run.font.size = Pt(9)
                    set_run_font(run)
            if r_idx % 2 == 1:
                set_cell_shading(cell, 'F2F2F2')

    # 열 너비
    if col_widths:
        for i, w in enumerate(col_widths):
            for row in table.rows:
                row.cells[i].width = Cm(w)

    return table

def add_note(doc, text, note_type="참고"):
    """노트/팁/주의 박스 추가"""
    p = doc.add_paragraph()
    prefix_map = {"참고": "[참고] ", "팁": "[팁] ", "주의": "[주의] ", "중요": "[중요] "}
    prefix = prefix_map.get(note_type, "[참고] ")
    run = p.add_run(prefix)
    run.bold = True
    run.font.size = Pt(9)
    run.font.color.rgb = RGBColor(0x2B, 0x57, 0x9A)
    set_run_font(run)
    run = p.add_run(text)
    run.font.size = Pt(9)
    run.font.color.rgb = RGBColor(0x44, 0x44, 0x44)
    set_run_font(run)
    p.paragraph_format.left_indent = Cm(0.5)
    p.paragraph_format.space_after = Pt(6)

def add_code_block(doc, text):
    """코드 블록 추가"""
    p = doc.add_paragraph()
    p.paragraph_format.left_indent = Cm(1)
    p.paragraph_format.space_before = Pt(4)
    p.paragraph_format.space_after = Pt(4)
    run = p.add_run(text)
    run.font.name = 'Consolas'
    run.font.size = Pt(8.5)
    run.font.color.rgb = RGBColor(0x33, 0x33, 0x33)
    # 코드 블록은 eastAsia만 맑은고딕으로
    r = run._element
    rPr = r.get_or_add_rPr()
    rFonts = rPr.find(qn('w:rFonts'))
    if rFonts is None:
        rFonts = rPr.makeelement(qn('w:rFonts'), {})
        rPr.insert(0, rFonts)
    rFonts.set(qn('w:eastAsia'), FONT_NAME)

def add_bullet(doc, text, level=0):
    """불릿 포인트 추가"""
    p = doc.add_paragraph(text, style='List Bullet')
    p.paragraph_format.left_indent = Cm(1.5 + level * 1.0)
    for run in p.runs:
        run.font.size = Pt(10)
        set_run_font(run)

def add_numbered(doc, text, level=0):
    """번호 목록 추가"""
    p = doc.add_paragraph(text, style='List Number')
    p.paragraph_format.left_indent = Cm(1.5 + level * 1.0)
    for run in p.runs:
        run.font.size = Pt(10)
        set_run_font(run)

def main():
    doc = Document()

    # 기본 스타일 설정
    style = doc.styles['Normal']
    style.font.name = FONT_NAME
    style.font.size = Pt(10)
    style.paragraph_format.space_after = Pt(6)
    style.paragraph_format.line_spacing = 1.15
    # Normal 스타일에 eastAsia 폰트 설정
    rPr = style.element.get_or_add_rPr()
    rFonts = rPr.find(qn('w:rFonts'))
    if rFonts is None:
        rFonts = rPr.makeelement(qn('w:rFonts'), {})
        rPr.insert(0, rFonts)
    rFonts.set(qn('w:eastAsia'), FONT_NAME)
    rFonts.set(qn('w:ascii'), FONT_NAME)
    rFonts.set(qn('w:hAnsi'), FONT_NAME)

    for level in range(1, 4):
        hs = doc.styles[f'Heading {level}']
        hs.font.name = FONT_NAME
        hs.font.color.rgb = RGBColor(0x2B, 0x57, 0x9A)
        hPr = hs.element.get_or_add_rPr()
        hFonts = hPr.find(qn('w:rFonts'))
        if hFonts is None:
            hFonts = hPr.makeelement(qn('w:rFonts'), {})
            hPr.insert(0, hFonts)
        hFonts.set(qn('w:eastAsia'), FONT_NAME)
        hFonts.set(qn('w:ascii'), FONT_NAME)
        hFonts.set(qn('w:hAnsi'), FONT_NAME)

    # ═══════════════════════════════════════════
    # 표지
    # ═══════════════════════════════════════════
    for _ in range(6):
        doc.add_paragraph()

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = p.add_run('VMS 딥러닝\n라벨링 & 학습\n오퍼레이터 매뉴얼')
    run.font.size = Pt(28)
    run.bold = True
    run.font.color.rgb = RGBColor(0x2B, 0x57, 0x9A)

    doc.add_paragraph()

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = p.add_run('Version 1.0')
    run.font.size = Pt(14)
    run.font.color.rgb = RGBColor(0x66, 0x66, 0x66)

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = p.add_run('2026-04-03')
    run.font.size = Pt(12)
    run.font.color.rgb = RGBColor(0x99, 0x99, 0x99)

    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = p.add_run('대상: VMS VisionSetup 오퍼레이터')
    run.font.size = Pt(11)
    run.font.color.rgb = RGBColor(0x99, 0x99, 0x99)

    doc.add_page_break()

    # ═══════════════════════════════════════════
    # 목차
    # ═══════════════════════════════════════════
    doc.add_heading('목차', level=1)
    toc_items = [
        '1. 개요',
        '2. 사전 준비',
        '3. 라벨링 화면 구성',
        '4. 데이터셋 관리',
        '5. 이미지 추가 및 탐색',
        '6. 라벨링 작업',
        '7. 데이터 내보내기 (Export)',
        '8. 모델 학습 (Training)',
        '9. 커스텀 모델 적용',
        '10. 전체 워크플로우 요약',
        '11. 문제 해결 (FAQ)',
    ]
    for item in toc_items:
        p = doc.add_paragraph(item)
        p.paragraph_format.space_after = Pt(2)
        p.paragraph_format.left_indent = Cm(1)

    doc.add_page_break()

    # ═══════════════════════════════════════════
    # 1. 개요
    # ═══════════════════════════════════════════
    doc.add_heading('1. 개요', level=1)
    doc.add_paragraph(
        'VMS Deep Learning Labeling 기능은 검사 품질 향상을 위해 자체 학습 데이터를 구축하고, '
        'ONNX 모델을 학습하여 현장 환경에 최적화된 커스텀 모델을 만드는 도구입니다.'
    )

    doc.add_heading('전체 흐름', level=2)
    add_code_block(doc, '이미지 수집 → 데이터셋 생성 → 라벨링 → 내보내기 → 학습 → 커스텀 모델 적용')

    add_styled_table(doc,
        ['단계', '설명'],
        [
            ['라벨링', '이미지에서 대상 영역을 박스로 지정하고 클래스/텍스트를 입력'],
            ['내보내기', '학습 포맷(YOLO, PaddleOCR, Classification, Anomaly)으로 데이터를 변환'],
            ['학습', 'Python 스크립트를 통해 모델을 학습 (Fine-tuning)'],
            ['적용', '학습된 ONNX 모델을 Vision Tool에 즉시 적용'],
        ],
        col_widths=[4, 13]
    )

    # ═══════════════════════════════════════════
    # 2. 사전 준비
    # ═══════════════════════════════════════════
    doc.add_heading('2. 사전 준비', level=1)

    doc.add_heading('2.1 Python 환경 설치', level=2)
    doc.add_paragraph('학습 기능을 사용하려면 Python 환경이 필요합니다. (라벨링만 사용할 경우 불필요)')
    add_code_block(doc, '# Python 3.8 ~ 3.10 권장\npip install paddlepaddle paddleocr paddle2onnx onnx')
    add_note(doc, 'GPU 학습을 위해서는 CUDA 지원 PaddlePaddle을 설치하세요: pip install paddlepaddle-gpu')

    doc.add_heading('2.2 학습 스크립트 위치', level=2)
    doc.add_paragraph('학습 스크립트는 VMS 설치 경로에 포함되어 있습니다:')
    add_code_block(doc, 'VMS.VisionSetup/scripts/train_ppocr.py')

    # ═══════════════════════════════════════════
    # 3. 라벨링 화면 구성
    # ═══════════════════════════════════════════
    doc.add_heading('3. 라벨링 화면 구성', level=1)
    doc.add_paragraph(
        'VMS.VisionSetup 메인 화면 상단의 "Labeling" 버튼을 클릭하면 VMS.DeepLearning 애플리케이션이 실행됩니다.'
    )

    add_styled_table(doc,
        ['영역', '기능'],
        [
            ['좌측 패널', '데이터셋 관리, 이미지 목록 및 탐색'],
            ['중앙 캔버스', '이미지 표시, 바운딩 박스 생성/편집/삭제'],
            ['우측 패널', '클래스 관리, 라벨 목록, 라벨 편집, 내보내기 & 학습'],
        ],
        col_widths=[4, 13]
    )

    doc.add_paragraph()
    add_code_block(doc,
        '┌─────────────┬──────────────────────────┬──────────────┐\n'
        '│  좌측 패널   │       중앙 캔버스         │  우측 패널    │\n'
        '│             │                          │              │\n'
        '│ [Dataset]   │                          │ [Classes]    │\n'
        '│  - 목록     │     이미지 표시 영역       │  - 클래스 목록│\n'
        '│  - 생성/삭제 │     + 바운딩 박스 편집     │  - 추가      │\n'
        '│             │                          │              │\n'
        '│ [Images]    │                          │ [Labels]     │\n'
        '│  - 이미지   │                          │  - 라벨 목록  │\n'
        '│    목록     │                          │  - 편집기     │\n'
        '│  - 추가/삭제 │                          │              │\n'
        '│  - ◀ ▶ 탐색 │                          │ [Export &    │\n'
        '│             │                          │  Training]   │\n'
        '└─────────────┴──────────────────────────┴──────────────┘'
    )

    # ═══════════════════════════════════════════
    # 4. 데이터셋 관리
    # ═══════════════════════════════════════════
    doc.add_heading('4. 데이터셋 관리', level=1)

    doc.add_heading('4.1 데이터셋 생성', level=2)
    add_numbered(doc, '좌측 패널의 Dataset 섹션에서 텍스트 입력란에 데이터셋 이름을 입력합니다.')
    add_numbered(doc, '"+" 버튼을 클릭하면 데이터셋이 생성됩니다.')
    add_note(doc, '데이터셋은 %APPDATA%/VMS/Datasets/{데이터셋명}/ 폴더에 저장됩니다.')

    doc.add_heading('4.2 데이터셋 불러오기', level=2)
    add_bullet(doc, '목록에서 데이터셋을 선택한 후 "Load" 버튼을 클릭합니다.')
    add_bullet(doc, '저장된 이미지와 라벨 정보가 모두 로드됩니다.')

    doc.add_heading('4.3 데이터셋 저장', level=2)
    add_bullet(doc, '"Save" 버튼을 클릭하면 현재 작업 내용이 JSON 파일로 저장됩니다.')
    add_bullet(doc, '이미지 전환 시 자동 저장되지만, 작업 중간에 수동 저장을 권장합니다.')

    doc.add_heading('4.4 데이터셋 삭제', level=2)
    add_bullet(doc, '"Delete" 버튼 클릭 → 확인 대화 상자에서 승인하면 모든 이미지와 라벨이 영구 삭제됩니다.')
    add_note(doc, '삭제된 데이터셋은 복구할 수 없습니다.', '주의')

    # ═══════════════════════════════════════════
    # 5. 이미지 추가 및 탐색
    # ═══════════════════════════════════════════
    doc.add_heading('5. 이미지 추가 및 탐색', level=1)

    doc.add_heading('5.1 이미지 추가', level=2)
    add_numbered(doc, '좌측 패널의 Images 영역에서 "+ Add" 버튼을 클릭합니다.')
    add_numbered(doc, '파일 선택 대화 상자에서 이미지를 선택합니다. (지원: BMP, JPG, JPEG, PNG, TIF, TIFF)')
    add_numbered(doc, '이미지가 데이터셋 폴더 안의 images/ 하위 폴더에 복사됩니다.')
    add_note(doc, '학습 효과를 높이려면 실제 검사 환경에서 촬영한 이미지를 충분히 수집하세요. 최소 50장 이상 권장합니다.', '팁')

    doc.add_heading('5.2 이미지 탐색', level=2)
    add_styled_table(doc,
        ['조작', '설명'],
        [
            ['◀ / ▶ 버튼', '이전/다음 이미지로 이동'],
            ['이미지 목록 클릭', '해당 이미지로 바로 이동'],
            ['더블 클릭', '이미지 선택 및 표시'],
        ],
        col_widths=[5, 12]
    )
    doc.add_paragraph()
    add_bullet(doc, '현재 위치는 [현재번호 / 전체] 형식으로 표시됩니다.')
    add_bullet(doc, '이미지 목록에서 녹색 원(●) = 라벨링 완료, 회색 원(●) = 미라벨링')

    doc.add_heading('5.3 이미지 삭제', level=2)
    add_bullet(doc, '"- Del" 버튼으로 현재 이미지를 삭제할 수 있습니다.')

    # ═══════════════════════════════════════════
    # 6. 라벨링 작업
    # ═══════════════════════════════════════════
    doc.add_heading('6. 라벨링 작업', level=1)

    doc.add_heading('6.1 클래스 등록', level=2)
    doc.add_paragraph('라벨링을 시작하기 전에 인식할 대상 종류(클래스)를 등록합니다.')
    add_numbered(doc, '우측 패널의 Classes 섹션에서 클래스 이름을 입력합니다. (예: text, serial_number, date)')
    add_numbered(doc, '"+" 버튼을 클릭하여 추가합니다.')
    add_numbered(doc, '목록에서 현재 사용할 클래스를 선택(클릭)합니다.')
    add_note(doc, '클래스마다 자동으로 고유 색상이 배정되어, 캔버스에서 클래스별 바운딩 박스 색상이 다르게 표시됩니다.')

    doc.add_heading('6.2 바운딩 박스 생성', level=2)
    add_numbered(doc, '우측 패널에서 사용할 클래스를 선택합니다.')
    add_numbered(doc, '중앙 캔버스의 도구 모음(Toolbar)에서 사각형 ROI 도구를 선택합니다.')
    add_numbered(doc, '이미지 위에서 대상 영역을 드래그하여 박스를 그립니다.')
    add_numbered(doc, '박스가 생성되면 자동으로 선택한 클래스의 라벨이 생성됩니다.')

    doc.add_heading('6.3 텍스트 입력 (Transcription)', level=2)
    doc.add_paragraph('Recognition 학습에 필수적인 단계입니다.')
    add_numbered(doc, '캔버스에서 바운딩 박스를 클릭하여 선택합니다.')
    add_numbered(doc, '우측 패널 하단의 Selected Label 편집기가 활성화됩니다.')
    add_numbered(doc, 'Transcription 입력란에 해당 박스 안의 실제 텍스트를 정확히 입력합니다.')
    add_note(doc, 'Transcription은 대소문자, 공백, 특수문자를 포함하여 정확하게 입력해야 합니다. 이 텍스트가 모델 학습의 정답(Ground Truth)이 됩니다.', '중요')

    doc.add_heading('6.4 라벨 수정', level=2)
    add_styled_table(doc,
        ['작업', '방법'],
        [
            ['박스 이동', '박스를 드래그하여 위치 변경'],
            ['박스 크기 조절', '모서리/변 핸들을 드래그하여 크기 변경'],
            ['클래스 변경', '라벨 선택 후 편집기의 Class 드롭다운에서 변경'],
            ['텍스트 수정', '라벨 선택 후 Transcription 입력란 수정'],
            ['라벨 삭제', '라벨 선택 후 Labels 영역의 "Delete" 버튼 클릭'],
        ],
        col_widths=[5, 12]
    )

    doc.add_heading('6.5 라벨링 품질 가이드라인', level=2)
    doc.add_paragraph('학습 효과를 극대화하기 위한 라벨링 규칙:')
    add_styled_table(doc,
        ['항목', '권장 사항'],
        [
            ['박스 범위', '대상에 딱 맞게 — 너무 크거나 작으면 학습 품질 저하'],
            ['텍스트 단위', '한 줄에 하나의 박스 (여러 줄을 하나의 박스로 묶지 마세요)'],
            ['일관성', '같은 형식의 대상은 동일한 클래스로 분류'],
            ['Transcription', '이미지에 보이는 그대로 정확히 입력'],
            ['최소 수량', 'Detection: 100장+ / Recognition: 500장+ 권장'],
        ],
        col_widths=[4, 13]
    )

    # ═══════════════════════════════════════════
    # 7. 데이터 내보내기
    # ═══════════════════════════════════════════
    doc.add_heading('7. 데이터 내보내기 (Export)', level=1)
    doc.add_paragraph('라벨링이 완료되면 학습 데이터 형식으로 내보냅니다.')

    doc.add_heading('7.1 자동 분할 (Train/Validation)', level=2)
    add_numbered(doc, '우측 패널 하단의 Export & Training 섹션을 펼칩니다.')
    add_numbered(doc, '"Auto Split (Train/Val)" 버튼을 클릭합니다.')
    add_numbered(doc, '전체 이미지가 80:20 비율로 Train/Validation에 자동 분배됩니다.')

    doc.add_heading('7.2 내보내기 포맷', level=2)
    doc.add_paragraph('데이터셋의 Task Type에 따라 적절한 Export 포맷을 선택합니다:')
    add_styled_table(doc,
        ['Task Type', 'Export 포맷', '용도'],
        [
            ['Detection', 'YOLO format', '객체 검출 (YOLOv8/v11)'],
            ['Classification', 'ImageFolder format', '이미지 분류 (ResNet, MobileNet)'],
            ['Anomaly', 'MVTec format', '이상 탐지 (PatchCore, FastFlow)'],
            ['OCR', 'PaddleOCR format', 'OCR 텍스트 인식 (PaddleOCR)'],
        ],
        col_widths=[4, 5, 8]
    )

    doc.add_heading('7.3 PaddleOCR 형식 출력 구조', level=2)
    add_code_block(doc,
        '출력폴더/\n'
        '├── train_det.txt        # Detection 학습용 라벨\n'
        '├── val_det.txt          # Detection 검증용 라벨\n'
        '├── train_rec.txt        # Recognition 학습용 라벨\n'
        '├── val_rec.txt          # Recognition 검증용 라벨\n'
        '├── train_crops/         # Recognition용 크롭 이미지\n'
        '└── images/              # Detection용 원본 이미지\n'
        '    ├── train/\n'
        '    └── val/'
    )

    # ═══════════════════════════════════════════
    # 8. 모델 학습
    # ═══════════════════════════════════════════
    doc.add_heading('8. 모델 학습 (Training)', level=1)

    doc.add_heading('8.1 학습 설정', level=2)
    doc.add_paragraph('우측 패널의 Export & Training 섹션을 펼치면 Training 설정이 나타납니다.')
    add_styled_table(doc,
        ['항목', '설명', '기본값'],
        [
            ['Python', 'Python 실행 경로', 'python'],
            ['Script', '학습 스크립트 파일 경로', '(수동 지정)'],
            ['Epochs', '학습 반복 횟수', '100'],
            ['Batch', '배치 크기 (GPU 메모리에 맞게 조절)', '8'],
        ],
        col_widths=[3, 10, 4]
    )

    doc.add_heading('8.2 학습 스크립트 설정', level=2)
    add_numbered(doc, 'Script 항목 옆의 "..." 버튼을 클릭합니다.')
    add_numbered(doc, '파일 선택 대화 상자에서 학습 스크립트를 선택합니다.')
    add_code_block(doc, 'VMS.VisionSetup/scripts/train_ppocr.py')
    add_note(doc, 'Python 경로가 시스템 PATH에 없는 경우, 전체 경로를 입력합니다. 예: C:\\Python310\\python.exe')

    doc.add_heading('8.3 학습 실행', level=2)
    add_numbered(doc, '데이터셋을 선택하고, 적절한 형식으로 Export를 먼저 완료합니다.')
    add_numbered(doc, '학습 설정을 확인합니다.')
    add_numbered(doc, '"Start Training" 버튼을 클릭합니다.')
    doc.add_paragraph('학습이 시작되면:')
    add_bullet(doc, '프로그레스 바에 진행률이 표시됩니다.')
    add_bullet(doc, 'Loss: 손실값 (낮을수록 좋음)')
    add_bullet(doc, 'Acc: 정확도 (높을수록 좋음)')
    add_bullet(doc, 'Log 영역에 학습 로그가 실시간으로 표시됩니다.')

    doc.add_heading('8.4 학습 중지', level=2)
    add_bullet(doc, '"Stop" 버튼을 클릭하면 학습이 중단됩니다.')
    add_bullet(doc, '중단 시점까지의 체크포인트가 저장됩니다.')

    doc.add_heading('8.5 학습 완료', level=2)
    doc.add_paragraph('학습이 정상 완료되면:')
    add_numbered(doc, '자동으로 ONNX 포맷으로 변환됩니다.')
    add_numbered(doc, '완료 대화 상자에 ONNX 모델 파일 경로가 표시됩니다.')
    add_numbered(doc, '이 경로를 메모해 두세요 — 다음 단계에서 사용합니다.')

    doc.add_heading('8.6 학습 파라미터 조정 가이드', level=2)
    add_styled_table(doc,
        ['상황', '조치'],
        [
            ['Loss가 줄지 않음', 'Learning Rate 낮추기 (0.0001), 데이터 더 수집'],
            ['Acc가 낮음', 'Epochs 늘리기, Transcription 오류 점검'],
            ['GPU 메모리 부족', 'Batch Size 줄이기 (4 또는 2)'],
            ['과적합', '데이터 더 수집, Epochs 줄이기'],
        ],
        col_widths=[5, 12]
    )

    # ═══════════════════════════════════════════
    # 9. 커스텀 모델 적용
    # ═══════════════════════════════════════════
    doc.add_heading('9. 커스텀 모델 적용', level=1)
    doc.add_paragraph('학습이 완료된 ONNX 모델을 Vision Tool에 적용합니다.')

    doc.add_heading('9.1 모델 경로 설정', level=2)
    add_numbered(doc, 'VMS VisionSetup에서 해당 Deep Learning Tool을 선택합니다.')
    add_numbered(doc, '우측의 Tool Settings에서 Model Path에 학습된 ONNX 파일 경로를 입력합니다.')
    add_numbered(doc, '필요한 경우 Class Names, Threshold 등을 조정합니다.')

    add_styled_table(doc,
        ['Tool', 'Model Path 항목', '예시'],
        [
            ['Detection (YOLO)', 'ModelPath', 'C:\\output\\best.onnx'],
            ['Classify', 'ModelPath', 'C:\\output\\classifier.onnx'],
            ['Anomaly', 'ModelPath', 'C:\\output\\anomaly_model.onnx'],
            ['OCR (Custom Det)', 'Custom Det Model', 'C:\\output\\det_model.onnx'],
            ['OCR (Custom Rec)', 'Custom Rec Model', 'C:\\output\\rec_model.onnx'],
        ],
        col_widths=[4, 5, 8]
    )

    doc.add_heading('9.2 적용 확인', level=2)
    add_bullet(doc, '이미지를 실행하여 추론 결과를 확인합니다.')
    add_bullet(doc, '결과가 만족스럽지 않으면 라벨링 데이터를 보완하고 재학습합니다.')

    doc.add_heading('9.3 기본 모델로 복원', level=2)
    add_bullet(doc, 'Model Path의 내용을 지우면 기본 모델로 자동 복원됩니다.')

    # ═══════════════════════════════════════════
    # 10. 전체 워크플로우 요약
    # ═══════════════════════════════════════════
    doc.add_heading('10. 전체 워크플로우 요약', level=1)

    add_styled_table(doc,
        ['Step', '작업', '상세'],
        [
            ['1', '데이터셋 생성', 'Labeling 앱 → Dataset → 이름 입력 → "+" 클릭'],
            ['2', '이미지 추가', '"+ Add" → 검사 환경 이미지 선택 (50장 이상 권장)'],
            ['3', '클래스 등록', 'Classes → 대상 종류별 클래스명 입력 → "+" 클릭'],
            ['4', '라벨링', '클래스 선택 → 바운딩 박스 그리기 → Transcription 입력'],
            ['5', '내보내기', '"Auto Split" → 적절한 Export 포맷 선택'],
            ['6', '학습', 'Script 경로 지정 → Epochs/Batch 설정 → "Start Training"'],
            ['7', '모델 적용', 'Vision Tool Settings → Model Path에 ONNX 경로 입력'],
            ['8', '검증', '검사 이미지 실행 → 결과 확인 → 필요 시 반복'],
        ],
        col_widths=[2, 4, 11]
    )

    # ═══════════════════════════════════════════
    # 11. 문제 해결 (FAQ)
    # ═══════════════════════════════════════════
    doc.add_heading('11. 문제 해결 (FAQ)', level=1)

    faqs = [
        ('Q: "학습 스크립트 경로를 지정하세요" 오류가 나옵니다.',
         'Script 항목에 train_ppocr.py 파일 경로를 지정해야 합니다. "..." 버튼으로 VMS.VisionSetup/scripts/train_ppocr.py를 선택하세요.'),
        ('Q: 학습 시작 시 Python을 찾을 수 없다는 오류가 나옵니다.',
         'Python 항목에 전체 경로를 입력하세요. 예: C:\\Python310\\python.exe. 터미널에서 python --version으로 설치 여부를 확인하세요.'),
        ('Q: paddlepaddle 모듈을 찾을 수 없다는 오류가 나옵니다.',
         'Python 환경에 필요 패키지를 설치하세요: pip install paddlepaddle paddleocr paddle2onnx onnx'),
        ('Q: GPU 메모리 부족 (CUDA Out of Memory) 오류가 발생합니다.',
         'Batch Size를 줄여보세요 (8 → 4 → 2 → 1). GPU 메모리가 4GB 미만이면 CPU 학습을 권장합니다.'),
        ('Q: 학습은 완료했는데 인식률이 개선되지 않습니다.',
         '라벨링 데이터가 충분한지 확인하세요 (Recognition: 최소 500장). Transcription 정확도, Train/Val 분할 여부를 점검하고, Epochs를 200~500으로 늘려보세요.'),
        ('Q: 기본 모델로 돌아가고 싶습니다.',
         'Tool Settings에서 Model Path를 비우면 기본 모델이 사용됩니다.'),
        ('Q: 이미지 목록에서 녹색/회색 원은 무엇인가요?',
         '녹색(●) = 라벨이 1개 이상 있는 이미지, 회색(●) = 아직 라벨이 없는 이미지입니다.'),
        ('Q: 데이터셋은 어디에 저장되나요?',
         '%APPDATA%/VMS/Datasets/ 폴더에 데이터셋별 하위 폴더로 저장됩니다.'),
    ]

    for question, answer in faqs:
        p = doc.add_paragraph()
        run = p.add_run(question)
        run.bold = True
        run.font.size = Pt(10)

        p = doc.add_paragraph()
        run = p.add_run(answer)
        run.font.size = Pt(10)
        run.font.color.rgb = RGBColor(0x44, 0x44, 0x44)
        p.paragraph_format.left_indent = Cm(0.5)
        p.paragraph_format.space_after = Pt(12)

    # ═══════════════════════════════════════════
    # 푸터
    # ═══════════════════════════════════════════
    doc.add_paragraph()
    p = doc.add_paragraph()
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = p.add_run('VMS VisionSetup — Deep Learning Labeling & Training Module')
    run.font.size = Pt(9)
    run.font.color.rgb = RGBColor(0x99, 0x99, 0x99)
    run.italic = True

    # 모든 paragraph의 run에 한글 폰트 일괄 적용
    for para in doc.paragraphs:
        for run in para.runs:
            if run.font.name != 'Consolas':  # 코드 블록은 제외
                set_run_font(run)
    for table in doc.tables:
        for row in table.rows:
            for cell in row.cells:
                for para in cell.paragraphs:
                    for run in para.runs:
                        set_run_font(run)

    # 저장
    output_path = os.path.join(os.path.dirname(__file__), 'VMS_DeepLearning_Manual.docx')
    doc.save(output_path)
    print(f'매뉴얼 생성 완료: {output_path}')

if __name__ == '__main__':
    main()
