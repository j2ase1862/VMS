# 부록 HTML 생성 — docs/manuals/src/13_appendix.html
#   A. 단축키  B. 비전 도구 파라미터 전체표 (docs/gs/pipeline/_tool_params.json 에서)  C. 검사 결과 값 이름
import json, io, os, html
HERE = os.path.dirname(os.path.abspath(__file__))
PARAMS = os.path.normpath(os.path.join(HERE, "..", "gs", "pipeline", "_tool_params.json"))
OUT = os.path.join(HERE, "src", "13_appendix.html")
tools = json.load(io.open(PARAMS, encoding="utf-8"))

DESC = {
  "ThresholdValue":"이진화 임계값. 픽셀값이 이 값보다 크면 전경(흰색)으로 처리","MaxValue":"이진화 시 전경에 적용할 값(보통 255)","UseOtsu":"Otsu 자동 임계값 사용(체크 시 Threshold Value 무시)","UseAdaptive":"적응형 이진화(국소 영역별 임계값 — 조명 불균일에 강함)","BlockSize":"적응형 이진화 국소 블록 크기(홀수)","CValue":"적응형 이진화 보정 상수(클수록 전경 감소)",
  "BlurType":"블러 종류(Gaussian · Median · Box 등)","KernelSize":"커널 크기(홀수). 클수록 강한 평활화","SigmaX":"가우시안 X 표준편차(0 이면 커널에서 자동)","SigmaY":"가우시안 Y 표준편차(0 이면 SigmaX 사용)",
  "Operation":"연산 종류","KernelWidth":"구조 요소(커널) 너비","KernelHeight":"구조 요소(커널) 높이","Iterations":"연산 반복 횟수",
  "Method":"에지 검출 방법(Canny · Sobel · Laplacian 등)","CannyThreshold1":"Canny 하위(연결) 임계값","CannyThreshold2":"Canny 상위(강한 에지) 임계값","CannyApertureSize":"Sobel 커널 크기(홀수)","L2Gradient":"정확한 L2 그래디언트 사용(정밀도↑ 속도↓)","ClipLimit":"CLAHE 대비 제한값","TileGridWidth":"CLAHE 타일 격자 가로 분할 수","TileGridHeight":"CLAHE 타일 격자 세로 분할 수",
  "UseInternalThreshold":"도구 안에서 이진화를 수행(외부 전처리 불필요)","SegmentationPolarity":"전경 극성(밝은 블롭 / 어두운 블롭)","MinArea":"검출 최소 면적(px)","MaxArea":"검출 최대 면적(px)","EnableJudgment":"이 도구의 OK/NG 판정 사용","UseAreaJudgment":"면적 기준 판정 사용","ExpectedArea":"기준(목표) 면적","AreaTolerancePlus":"면적 상한 허용오차(+)","AreaToleranceMinus":"면적 하한 허용오차(−)","UseCountJudgment":"블롭 개수 기준 판정 사용","CountMode":"개수 판정 모드(정확히 · 이상 · 범위 · 공차)","ExpectedCount":"기준 개수","ExpectedCountMax":"개수 상한(범위 모드)","DrawContours":"윤곽선 오버레이","DrawBoundingBox":"외접 사각형 표시","DrawCenterPoint":"중심점 표시","DrawLabels":"번호/라벨 표시",
  "SearchWidth":"탐색(투영) 폭","SearchAxis":"탐색 축 방향","Polarity":"에지 극성(밝→어두 · 어두→밝 · 모두)","EdgeThreshold":"에지로 인정할 최소 강도","FilterHalfWidth":"에지 필터 반폭(노이즈 평활)","Mode":"동작 모드","MaxEdges":"검출할 최대 에지 수","ScorerMode":"에지 점수 산정 방식","SelectionMode":"사용할 에지 선택 방식(첫 · 최강 · 마지막 등)","ProjectionMode":"프로파일 투영 방식","UseGaussianFilter":"가우시안 필터 적용","GaussianSigma":"가우시안 시그마","UseNormalizedContrast":"대비 정규화","SubPixelMethod":"서브픽셀 보간 방식","NumCalipers":"캘리퍼 개수(많을수록 정밀 · 느림)","SearchLength":"캘리퍼 탐색 길이","FitMethod":"피팅 방법(최소제곱 · RANSAC)","RansacThreshold":"RANSAC 인라이어 허용오차","MinFoundCalipers":"유효로 인정할 최소 캘리퍼 수","ExpectedRadius":"예상 반지름","StartAngle":"탐색 시작 각도","EndAngle":"탐색 종료 각도","SearchDirection":"에지 탐색 방향(내→외 · 외→내)","CenterPoint.X":"예상 중심 X","CenterPoint.Y":"예상 중심 Y",
  "AngleStep":"각도 탐색 간격","MinScale":"최소 스케일","MaxScale":"최대 스케일","ScaleStep":"스케일 탐색 간격","ScoreThreshold":"매칭 점수 임계값(이상이면 검출)","MaxInstances":"최대 검출 개수","NmsDistanceFactor":"중복 검출 억제 거리 계수","NumPyramidLevels":"이미지 피라미드 레벨 수","TopCandidates":"정밀화할 상위 후보 수","UseSearchRegion":"탐색 영역 제한 사용","SearchRegionX":"탐색 영역 X","SearchRegionY":"탐색 영역 Y","SearchRegionWidth":"탐색 영역 너비","SearchRegionHeight":"탐색 영역 높이","UseContrastInvariant":"명암 반전에도 매칭","IsAutoTuneEnabled":"파라미터 자동 튜닝","AngleStart":"각도 탐색 시작","AngleExtent":"각도 탐색 범위","Greediness":"탐욕도(높을수록 빠르나 정확도↓)","NumLevels":"피라미드 레벨 수","CannyLow":"모델 에지 추출 하위 임계값","CannyHigh":"모델 에지 추출 상위 임계값","MaxModelPoints":"모델 특징점 최대 수","CurvatureWeight":"곡률 가중치","MinCoverage":"특징점 일치 비율 하한(반쪽 매칭 차단, 0 = 끔)",
  "CodeReaderMode":"판독 모드(1D · 2D · 자동)","MaxCodeCount":"한 화면에서 판독할 최대 코드 수","TryHarder":"저품질 코드 정밀 탐색","UseLocalization":"DataMatrix 후보 지역화","EnableVerification":"판독 결과 검증(기대값 비교)","ExpectedText":"기대 문자열","UseRegexMatch":"정규식으로 검증","ParseGs1":"GS1 AI 파싱","EnableQualityGrading":"인쇄 품질 등급화","MinPassGrade":"합격 최소 품질 등급","DrawOverlay":"결과 오버레이 표시",
  "OcrEngine":"OCR 엔진(Tesseract · PaddleOCR)","Language":"인식 언어","PageSegMode":"페이지 분할 모드","EngineMode":"엔진 모드","CharacterWhitelist":"허용 문자 집합","MaxSideLen":"입력 이미지 최대 변 길이","CustomDetModelPath":"커스텀 검출 모델 경로","CustomRecModelPath":"커스텀 인식 모델 경로","CustomDictPath":"커스텀 문자 사전 경로","ConfidenceThreshold":"신뢰도 임계값","AutoPreprocess":"자동 전처리","InvertImage":"명암 반전 후 인식","TargetTextHeight":"목표 글자 높이(0 = 자동)","DenoiseLevel":"노이즈 제거 강도","DotMatrixMode":"도트매트릭스(각인) 문자 모드","TessdataPath":"Tesseract tessdata 경로",
  "ModelPath":"모델 파일(.onnx) 경로 또는 model:// 참조","InputSize":"모델 입력 크기(px)","IouThreshold":"박스 중복 제거 IoU 임계값","UseROI":"ROI 안에서만 실행",
  "MinCircularity":"최소 원형도(4π×면적÷둘레², 원=1.0) — 이보다 낮은 덩어리는 개수·면적에서 제외","MaxCircularity":"최대 원형도 — 이보다 원에 가까운 덩어리를 제외","MinConvexity":"최소 볼록도(면적÷볼록 껍질 면적) — 오목하게 패인 덩어리를 제외","ExpectedValue":"판정 기준값(거리 · 각도 · 높이 등)","ToleranceMinus":"기준값 아래쪽 허용오차","TolerancePlus":"기준값 위쪽 허용오차","MaxFlatness":"합격 최대 평탄도(mm)","MaxStraightness":"합격 최대 직진도(mm)","MinConfidence":"합격 최소 정합 신뢰도(0~1)","UseSizeJudgment":"클러스터 크기(길이 · 폭) 판정 사용","ExpectedLength":"기준 길이(mm)","ExpectedWidth":"기준 폭(mm)","SizeToleranceMinus":"크기 하한 허용오차","SizeTolerancePlus":"크기 상한 허용오차","DistanceThreshold":"직선으로 인정할 점의 최대 거리(mm)","MinInliers":"직선으로 인정할 최소 점 개수",
}
esc = html.escape
# 13.4 기능 리스트 대응표 — GS 신청 기능 리스트(체크리스트 xlsx '기능 리스트' 탭 6~70행)의 각 항목을 설명하는 절
FUNC_MAP = [
  ("검사 운전 (VMS 메인)", "자동 운전", "AUTO RUN 연속 검사", "§4.3 · §4.8"),
  ("검사 운전 (VMS 메인)", "자동 운전", "수동 검사 실행", "§4.5"),
  ("검사 운전 (VMS 메인)", "자동 운전", "외부 트리거 연동", "§3.7 · §5.10"),
  ("검사 운전 (VMS 메인)", "판정·집계", "양불 판정 및 카운트", "§4.4 · §5.6"),
  ("검사 운전 (VMS 메인)", "판정·집계", "NG 이력 조회", "§4.2 · §4.5 · §4.6"),
  ("검사 운전 (VMS 메인)", "판정·집계", "단독 모드 로컬 생산 이력", "§4.9 · §4.10"),
  ("검사 운전 (VMS 메인)", "생산 관리 연동", "작업지시(WO) 운전", "§4.3 · §4.8 · §8.7"),
  ("검사 운전 (VMS 메인)", "생산 관리 연동", "멀티 Lot 관리", "§4.3 · §8.7"),
  ("검사 운전 (VMS 메인)", "생산 관리 연동", "작업자 로그인", "§4.3 · §4.12 · §8.9"),
  ("검사 운전 (VMS 메인)", "생산 관리 연동", "검사 결과 웹 업로드", "§8.6 · §8.26"),
  ("검사 운전 (VMS 메인)", "시퀀스 제어", "IO 시퀀스 운전", "§3.7 · §5.10"),
  ("레시피 편집 (Vision Setup)", "레시피 관리", "레시피 생성/저장/복사/이름 변경", "§5.3 · §8.10"),
  ("레시피 편집 (Vision Setup)", "레시피 관리", "예제 템플릿 갤러리", "§5.8"),
  ("레시피 편집 (Vision Setup)", "카메라·영상 취득", "카메라 연결/촬영/라이브", "§3.4 · §4.5 · §5.4 · §5.12"),
  ("레시피 편집 (Vision Setup)", "카메라·영상 취득", "이미지 파일 로드", "§4.5 · §5.4"),
  ("레시피 편집 (Vision Setup)", "ROI·좌표계", "ROI 설정(사각형/회전 사각형)", "§5.5"),
  ("레시피 편집 (Vision Setup)", "ROI·좌표계", "좌표 보정(Fixture)", "§5.5 · §7.3"),
  ("레시피 편집 (Vision Setup)", "영상 전처리 도구", "Blur / Morphology / Image Enhance / Grayscale / Threshold / Edge Detection / Histogram", "§6.1 · §6.2"),
  ("레시피 편집 (Vision Setup)", "영상 전처리 도구", "Polar Unwrap (원형 → 띠 펼침)", "§6.1"),
  ("레시피 편집 (Vision Setup)", "패턴 매칭 도구", "Feature Match (기하학적 패턴 매칭)", "§6.3 · §7.3"),
  ("레시피 편집 (Vision Setup)", "패턴 매칭 도구", "Shape Match (템플릿 매칭)", "§6.3"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "Blob (입자 분석)", "§6.5 · §7.1"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "Caliper / Line Fit / Circle Fit (치수 측정)", "§6.6 · §7.2"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "Geometry (기하 연산)", "§6.6 · §7.2"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "Result (판정 종합)", "§5.6 · §6.12"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "Code Reader (DataMatrix · GS1)", "§6.8 · §7.5"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "OCR (문자 인식)", "§6.7 · §7.6"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "OCV (문자 검증)", "§6.7 · §7.6"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "Color Extract / Color Match (색상 검사)", "§6.13 · §7.4"),
  ("레시피 편집 (Vision Setup)", "측정·판정 도구", "Photometric Stereo (표면 결함 검사)", "§6.15"),
  ("레시피 편집 (Vision Setup)", "딥러닝 도구", "Detection (객체 검출)", "§6.11 · §7.7"),
  ("레시피 편집 (Vision Setup)", "딥러닝 도구", "RF-DETR-seg (인스턴스 분할)", "§6.11"),
  ("레시피 편집 (Vision Setup)", "딥러닝 도구", "Segmentation (시맨틱 분할)", "§6.11"),
  ("레시피 편집 (Vision Setup)", "딥러닝 도구", "Classify (분류)", "§6.11"),
  ("레시피 편집 (Vision Setup)", "딥러닝 도구", "Anomaly (이상 탐지)", "§6.11 · §7.7"),
  ("레시피 편집 (Vision Setup)", "딥러닝 도구", "Ensemble (앙상블 판정)", "§6.12"),
  ("레시피 편집 (Vision Setup)", "정렬(얼라인)", "Match Align (1점/2점 정렬)", "§6.4 · §7.8"),
  ("레시피 편집 (Vision Setup)", "정렬(얼라인)", "Multi-Step Align (2-스텝 정렬)", "§6.4"),
  ("레시피 편집 (Vision Setup)", "3D 측정 도구", "3D 점군 측정(높이·평면·직선·기하)", "§6.10 · §7.9"),
  ("레시피 편집 (Vision Setup)", "3D 측정 도구", "3D 정렬·편차·객체 검출", "§6.9 · §6.10 · §7.9"),
  ("레시피 편집 (Vision Setup)", "검증 도구", "배치 테스트·CSV 리포트", "§5.11"),
  ("레시피 편집 (Vision Setup)", "캘리브레이션", "카메라 캘리브레이션 (mm 환산)", "§5.9"),
  ("레시피 편집 (Vision Setup)", "캘리브레이션", "Image Rectify (왜곡 보정)", "§6.14"),
  ("시스템 구성 (App Setup)", "장치 구성", "카메라 등록·시스템 구성 마법사", "3 장"),
  ("시스템 구성 (App Setup)", "보안", "사용자 계정·보안 모드", "§3.8 · §4.11 · §4.12"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "대시보드", "공장 전체 현황", "§8.2 · §8.3"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "현장 모니터링", "안돈 보드", "§8.4"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "현장 모니터링", "알람", "§8.5"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "현장 모니터링", "생산 이력", "§8.6"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "생산 관리", "작업 지시", "§8.7"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "생산 관리", "제품 · 작업자", "§8.8 · §8.9"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "품질 관리", "레시피 파라미터 (검사 기준값)", "§5.7 · §8.10"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "품질 관리", "파레토 분석 · SPC 관리도", "§8.11 · §8.12"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "품질 관리", "불량 코드", "§8.13"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "품질 관리", "교대 리포트 · PDF 리포트", "§8.14 · §8.15"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "설비 관리", "비전 클라이언트 (라인)", "§8.16"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "설비 관리", "OEE · 설비 신뢰성", "§8.17 · §8.18"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "설비 관리", "예방 보전", "§8.19"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "설비 관리", "불량률 예측", "§4.3 · §8.4 · §8.20"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "시스템 관리 (Admin)", "사용자 승인 · 권한", "§8.22"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "시스템 관리 (Admin)", "감사 로그", "§8.23"),
  ("생산 관리 웹 (동봉 BODA.VMS.Web)", "시스템 관리 (Admin)", "교대 마스터 · 시스템 설정 · SW 라이선스", "§8.21 · §8.24 · §8.25"),
  ("공통", "계정·권한", "로그인·권한 제한", "§4.12 · §8.1 · §8.2"),
  ("공통", "라이선스", "라이선스 인증", "§2.6 · §3.8 · §8.24 · §11.4"),
  ("공통", "유지 관리", "인앱 업데이트", "§2.7 · §4.6"),
]

out = []
out.append('<section id="appendix">\n<h2>13. 부록</h2>\n<div class="callout"><strong>⚙ 부록은 관리자와 기술 지원 담당자용 참고 자료입니다.</strong></div>\n')
out.append('''<h3 id="appx-keys">13.1 단축키 · 마우스 조작 (VisionSetup)</h3>
<table class="cols-2-3-4">
  <tr><th>키 / 조작</th><th>범위</th><th>동작</th></tr>
  <tr><td>← / →</td><td>메인 창 (폴더 열기 후)</td><td>이전 / 다음 이미지. 툴바의 Navigate · Run All · Run Selected 모드에 따라 이동 시 자동 실행</td></tr>
  <tr><td>Esc / 우클릭</td><td>Tool Workspace</td><td>도구 연결 모드 취소</td></tr>
  <tr><td>마우스 휠</td><td>이미지 뷰</td><td>커서 기준 확대 · 축소 (10%~500%)</td></tr>
  <tr><td>우클릭</td><td>이미지 뷰</td><td>다각형 ROI 완성(점 3 개 이상) / 그 외 선택 해제</td></tr>
  <tr><td>드래그 (흰 원 핸들 / 금색 원 핸들)</td><td>ROI</td><td>크기 조절 / 회전 (회전 사각형 ROI)</td></tr>
  <tr><td>← / → · Esc</td><td>Batch Test 실패 케이스 창</td><td>이전 / 다음 실패 케이스 · 창 닫기</td></tr>
  <tr><td>Ctrl + 휠 · Esc</td><td>학습 마스크 편집기</td><td>확대 · 축소 / 진행 중 다각형 취소</td></tr>
  <tr><td>Enter</td><td>SLM Recipe Bot 입력창 · 작업자 로그인 PIN</td><td>전송 / 로그인</td></tr>
</table>
<p>메뉴에 표시된 Ctrl+O · Ctrl+S · F5 · F6 는 관례 표기이며, 실제 실행은 메뉴 또는 툴바 버튼으로 합니다.</p>
''')
out.append('<h3 id="appx-params">13.2 비전 도구 파라미터 전체표</h3>\n<p>Tool Settings 패널에 표시되는 파라미터를 도구별로 정리했습니다. 라벨은 화면 표기와 같으며, 각 도구의 용도와 사용 요령은 6장을 참고하세요.</p>\n')
cats = {}
for t in tools: cats.setdefault(t["category"], []).append(t)
for cat, ts in cats.items():
    out.append(f'<h4>{esc(cat)}</h4>\n')
    for t in ts:
        out.append(f'<h5>{esc(t["tool"])}</h5>\n')
        if not t.get("params"):
            out.append('<p>이 도구는 별도 수치 파라미터 없이 ROI · 입력 연결만으로 동작합니다.</p>\n'); continue
        out.append('<table class="cols-3-3-4">\n<tr><th>파라미터 (UI 라벨)</th><th>컨트롤 / 범위</th><th>설명</th></tr>\n')
        for p in t["params"]:
            ctrl = p["ctrl"]
            if p.get("enum"): ctrl += f' ({p["enum"]})'
            elif p.get("min") or p.get("max"): ctrl += f' [{p.get("min","")}~{p.get("max","")}]'
            d = DESC.get(p["param"], "")
            out.append(f'<tr><td>{esc(p["label"])}</td><td>{esc(ctrl)}</td><td>{esc(d)}</td></tr>\n')
        out.append('</table>\n')
out.append('''<h3 id="appx-results">13.3 검사 결과 값 이름 (PLC 매핑 · Web 이력)</h3>
<p>도구 설정의 PLC Output 에서 고르는 결과 키(Key)는 도구가 내보내는 값 이름입니다. 자주 쓰는 이름은 다음과 같습니다.</p>
<table class="cols-3-6">
  <tr><th>도구</th><th>대표 결과 값</th></tr>
  <tr><td>Blob</td><td>BlobCount · TotalArea · Blob{n}_Area · Blob{n}_CenterX/Y · JudgmentPass</td></tr>
  <tr><td>Caliper · Line Fit · Circle Fit</td><td>EdgeX/Y · Angle · CenterX/Y · Radius · Score</td></tr>
  <tr><td>Geometry</td><td>Distance · DistanceMm · Angle · IntersectionX/Y · JudgmentValue · JudgmentPass</td></tr>
  <tr><td>Feature Match · Shape Match</td><td>MatchCount · Score · CenterX/Y · Angle · Scale · Match{n}_X/Y/Angle/Score</td></tr>
  <tr><td>Match Align · Multi-Step Align</td><td>DeltaX · DeltaY · DeltaTheta · RobotDX/DY/DTheta · JudgmentPass</td></tr>
  <tr><td>Code Reader · OCR · OCV</td><td>Text · Count · Verified · Grade · Confidence</td></tr>
  <tr><td>Detection · Segmentation 계열</td><td>Count · Class{n} · Score{n} · Box{n}_X/Y/W/H · MaskPixels</td></tr>
  <tr><td>PointCloud Cluster · 3D Geometry</td><td>ClusterCount · Cluster{i}_SizeX/Y/Z · Cluster{i}_Length/Width · Distance3D · AngleDeg</td></tr>
  <tr><td>Result</td><td>PassCount · FailCount · TotalCount</td></tr>
</table>
<h3 id="appx-funcmap">13.4 기능 목록 — 설명 위치</h3>
<p>제품 기능 목록의 항목마다 이 매뉴얼에서 화면과 사용법을 설명하는 절입니다.</p>
<table class="cols-2-2-3-2">
  <tr><th>대분류</th><th>중분류</th><th>기능</th><th>설명 절</th></tr>
''')
for big, mid, small, sec in FUNC_MAP:
    out.append(f'  <tr><td>{esc(big)}</td><td>{esc(mid)}</td><td>{esc(small)}</td><td>{sec}</td></tr>\n')
out.append('''</table>
<h3 id="appx-version">13.5 문서 정보</h3>
<table class="cols-3-6">
  <tr><th>항목</th><th>값</th></tr>
  <tr><td>문서 버전</td><td>3.0 (2026-10-25)</td></tr>
  <tr><td>대상 소프트웨어</td><td>VMS 1.47.2 · BODA.VMS.Web 1.10.4 · BODA.VMS.MLOps 0.1.5</td></tr>
  <tr><td>구성</td><td>VMS · VMS.VisionSetup · VMS.AppSetup (검사 런타임) / BODA.VMS.Web (생산 관리) / BODA.VMS.MLOps (선택 구성 요소, 인증 범위 외)</td></tr>
  <tr><td>발행</td><td>BODA Vision AI</td></tr>
</table>
</section>
''')
io.open(OUT, "w", encoding="utf-8", newline="\n").write("".join(out))
print("WROTE", OUT, "tools:", len(tools))
