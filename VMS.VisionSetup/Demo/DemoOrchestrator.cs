using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.VisionTools.CodeReading;
using VMS.VisionSetup.VisionTools.Identification;
using VMS.VisionSetup.VisionTools.SurfaceAnalysis;

namespace VMS.VisionSetup.Demo
{
    /// <summary>
    /// 전시회 부스용 홍보 데모를 정해진 시나리오대로 자동 재생한다.
    /// MainViewModel 의 public 진입점만 호출하며(MVVM 준수), View 전용 동작
    /// (3D 카메라 회전)은 <see cref="Demo3DOrbitMessage"/> 로 위임한다.
    ///
    /// 시나리오:
    ///   00 인트로 → 01 드래그&드롭 노코드 파이프라인 → 02 3D 측정 →
    ///   03 포토메트릭 스테레오 표면검사 → 아웃트로
    /// </summary>
    public sealed class DemoOrchestrator
    {
        private readonly MainViewModel _vm;
        private CancellationTokenSource? _cts;

        public DemoOrchestrator(MainViewModel vm) => _vm = vm;

        public bool IsRunning => _cts != null;

        /// <summary>데모 시작. 이미 실행 중이면 무시.</summary>
        public async Task RunAsync()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            try { File.Delete(DoneMarkerPath); } catch { /* 이전 마커 제거 */ }

            try
            {
                _vm.IsDemoRunning = true;
                do
                {
                    await SceneIntro(ct);
                    await SceneLabelOcr(ct);
                    await Scene3D(ct);
                    await ScenePhotometric(ct);
                    await SceneOutro(ct);
                }
                while (_vm.DemoLoop && !ct.IsCancellationRequested);
            }
            catch (OperationCanceledException) { /* 정상 중단 */ }
            finally
            {
                WeakReferenceMessenger.Default.Send(new Demo3DOrbitMessage(false));
                _vm.DemoClearWorkspace();
                _vm.DemoSceneTitle = "";
                _vm.DemoCaption = "";
                _vm.DemoSubCaption = "";
                _vm.IsDemoRunning = false;

                // 단일 패스 정상 종료 시 녹화 스크립트용 완료 마커 기록
                try
                {
                    if (!ct.IsCancellationRequested)
                        File.WriteAllText(DoneMarkerPath, "done");
                }
                catch { /* 무시 */ }

                _cts = null;
            }
        }

        /// <summary>녹화 자동화용 데모 완료 신호 파일 경로.</summary>
        public static string DoneMarkerPath =>
            Path.Combine(Path.GetTempPath(), "vms_demo_done.txt");

        /// <summary>데모 중단 요청.</summary>
        public void Stop() => _cts?.Cancel();

        // ─────────────────────────────────────────────────────────────
        // 씬
        // ─────────────────────────────────────────────────────────────

        private async Task SceneIntro(CancellationToken ct)
        {
            _vm.DemoClearWorkspace();
            _vm.CenterTabIndex = 0;
            await Caption("VMS — Vision Management System",
                          "2D · 3D · Deep Learning · 표면검사 통합 비전 플랫폼",
                          "BODA Vision AI", 2600, ct);
        }

        private async Task SceneLabelOcr(CancellationToken ct)
        {
            _vm.DemoClearWorkspace();
            _vm.CenterTabIndex = 0;
            _vm.CurrentImage = Cv2.ImRead(PickOcrImage(), ImreadModes.Color);
            _vm.DemoShowOriginalImage();
            await Caption("01  No-Code Inspection",
                          "제약 라벨을 OCR과 코드리더가 동시에 검사합니다",
                          "Drag & Drop · OCR + Barcode/DataMatrix", 2000, ct);

            // 1) OCR 툴 — LOT/유효기한 판독 (ROI·언어만 지정)
            var ocr = _vm.AddDemoTool("OCRTool", 60, 60);
            _vm.DemoCaption = "OCR 툴 — 검사 영역(ROI)과 언어만 지정";
            await Delay(1000, ct);
            if (ocr?.VisionTool is OCRTool ot)
            {
                ot.Language = OcrLanguage.English;
                ot.UseROI = true;
                ot.ROI = new Rect(205, 630, 645, 290);   // LOT/EXP 박스 (1024x1280 고정 라벨 레이아웃)
                ot.InvertImage = true;                    // 흰 글씨/검은 배경 → 반전
                ot.PageSegMode = OcrPageSegMode.SingleBlock;
                ot.CharacterWhitelist = "0123456789/";    // 숫자·날짜만 허용
                ot.ConfidenceThreshold = 0;               // 데모: 신뢰도 게이트 해제(인식되면 통과)
                ot.EnableVerification = true;             // 로트번호 존재 검증 → PASS(True) 판정
                ot.UseRegexMatch = true;
                ot.ExpectedText = @"\d{6,}";              // 6자리+ 숫자(LOT) 패턴
            }
            await Delay(800, ct);

            // 2) 코드리더 툴 — QR·DataMatrix 자동 인식 (전체 이미지, 연결 없이 병렬 검사)
            var code = _vm.AddDemoTool("CodeReaderTool", 60, 180);
            _vm.DemoCaption = "코드리더 툴 추가 — QR·DataMatrix 자동 인식";
            if (code?.VisionTool is CodeReaderTool cr)
            {
                cr.CodeReaderMode = CodeReaderMode.Auto;
                cr.TryHarder = true;
                cr.UseLocalization = true;
            }
            await Delay(1100, ct);

            // 3) 두 툴이 한 이미지를 동시에 검사 → 오버레이 합성
            _vm.DemoCaption = "한 번의 실행으로 OCR·코드 판독을 동시에";
            _vm.DemoSubCaption = "F5 · Run All";
            await Delay(500, ct);
            await _vm.RunAllToolsAsync();
            await Delay(3400, ct);
        }

        private async Task Scene3D(CancellationToken ct)
        {
            _vm.DemoClearWorkspace();
            await Caption("02  3D Metrology",
                          "구조광 3D 스캔 — 자동차 변속기 클러스터 기어",
                          "Structured-Light Point Cloud", 1200, ct);

            _vm.CurrentPointCloud = LoadVpc("pc_demo/transmission_gear_ng.vpc");
            _vm.CenterTabIndex = 2; // Point Cloud 탭
            await Delay(600, ct);

            // 턴테이블 자동 회전
            WeakReferenceMessenger.Default.Send(new Demo3DOrbitMessage(true));
            _vm.DemoCaption = "회전하는 3D 포인트클라우드 — 백만 점 실시간 렌더링";
            _vm.DemoSubCaption = "Orbit · Zoom · Inspect";
            await Delay(5200, ct);
            WeakReferenceMessenger.Default.Send(new Demo3DOrbitMessage(false));

            // Depth Map 전환
            _vm.CenterTabIndex = 1; // Depth Map 탭
            if (_vm.GenerateHeightMapCommand.CanExecute(null))
                _vm.GenerateHeightMapCommand.Execute(null);
            _vm.DemoCaption = "Depth Map — 높이 분포로 단차·결함을 한눈에";
            _vm.DemoSubCaption = "Height Map · 0.01mm";
            await Delay(3200, ct);

            // 다시 3D 로 돌아가 마무리 회전
            _vm.CenterTabIndex = 2;
            WeakReferenceMessenger.Default.Send(new Demo3DOrbitMessage(true));
            _vm.DemoCaption = "평면 피팅 · 거리/각도 측정까지 하나의 워크플로우";
            await Delay(3000, ct);
            WeakReferenceMessenger.Default.Send(new Demo3DOrbitMessage(false));
        }

        private async Task ScenePhotometric(CancellationToken ct)
        {
            _vm.DemoClearWorkspace();
            _vm.CenterTabIndex = 0;
            await Caption("03  Photometric Stereo",
                          "프레스 가공 자동차 도어 패널을 다중조명으로 재구성합니다",
                          "Automotive Panel · Surface Defect Inspection", 1600, ct);

            _vm.CurrentImage = LoadMat("ps_door/light_0.png");
            _vm.DemoShowOriginalImage();
            await Delay(800, ct);

            var ps = _vm.AddDemoTool("PhotometricStereoTool", 60, 60);
            if (ps?.VisionTool is PhotometricStereoTool tool)
            {
                ConfigureLights(tool);

                tool.OutputType = PsOutputType.NormalMap;
                await RunAndHold("표면 법선맵(Normal Map) — 미세 굴곡·캐릭터 라인까지 복원", "Normal Map", 2600, ct);

                tool.OutputType = PsOutputType.Albedo;
                await RunAndHold("반사율맵(Albedo) — 형상과 도장 무늬를 분리", "Albedo", 2600, ct);

                tool.OutputType = PsOutputType.DefectEnhanced;
                await RunAndHold("결함 강조 — 프레스 흠집·덴트를 육안보다 먼저 검출", "Defect Enhanced", 3600, ct);
            }
        }

        private async Task SceneOutro(CancellationToken ct)
        {
            _vm.DemoClearWorkspace();
            _vm.CenterTabIndex = 0;
            await Caption("VMS — Vision Management System",
                          "하나의 플랫폼으로, 검사의 처음부터 끝까지",
                          "www · BODA Vision AI", 3200, ct);
        }

        // ─────────────────────────────────────────────────────────────
        // 보조
        // ─────────────────────────────────────────────────────────────

        /// <summary>ps_door 합성 데이터 생성 시 사용한 조명 방향(고도 50°, 방위 0/90/180/270° + 정수리).</summary>
        private static void ConfigureLights(PhotometricStereoTool tool)
        {
            const double el = 50.0 * Math.PI / 180.0;
            double c = Math.Cos(el), s = Math.Sin(el);
            tool.Lights.Clear();
            tool.Lights.Add(new LightSample { Lx = c, Ly = 0, Lz = s, ImagePath = ResolveAsset("ps_door/light_0.png") });
            tool.Lights.Add(new LightSample { Lx = 0, Ly = c, Lz = s, ImagePath = ResolveAsset("ps_door/light_1.png") });
            tool.Lights.Add(new LightSample { Lx = -c, Ly = 0, Lz = s, ImagePath = ResolveAsset("ps_door/light_2.png") });
            tool.Lights.Add(new LightSample { Lx = 0, Ly = -c, Lz = s, ImagePath = ResolveAsset("ps_door/light_3.png") });
            tool.Lights.Add(new LightSample { Lx = 0, Ly = 0, Lz = 1, ImagePath = ResolveAsset("ps_door/light_4.png") });
        }

        private async Task RunAndHold(string caption, string sub, int holdMs, CancellationToken ct)
        {
            _vm.DemoCaption = caption;
            _vm.DemoSubCaption = sub;
            await _vm.RunAllToolsAsync();
            await Delay(holdMs, ct);
        }

        private async Task Caption(string title, string cap, string sub, int holdMs, CancellationToken ct)
        {
            _vm.DemoSceneTitle = title;
            _vm.DemoCaption = cap;
            _vm.DemoSubCaption = sub;
            await Delay(holdMs, ct);
        }

        private static Task Delay(int ms, CancellationToken ct) => Task.Delay(ms, ct);

        // 라벨 OCR 데모 이미지 폴더 (1024x1280 동일 규격 라벨). 폴더 내 첫 jpg 사용.
        private const string OcrImageDir = @"D:\참고 이미지\딥러닝 이미지\라벨 ocr";

        private static string PickOcrImage()
        {
            try
            {
                if (Directory.Exists(OcrImageDir))
                {
                    var files = Directory.GetFiles(OcrImageDir, "*.jpg");
                    Array.Sort(files, StringComparer.Ordinal);
                    if (files.Length > 0) return files[0];
                }
            }
            catch { /* 폴더 접근 실패 시 폴백 */ }
            return ResolveAsset("medicine_bottle.png");
        }

        private static Mat LoadMat(string rel) => Cv2.ImRead(ResolveAsset(rel), ImreadModes.Color);

        private static PointCloudData LoadVpc(string rel) => PointCloudData.LoadFromFile(ResolveAsset(rel));

        /// <summary>
        /// 실행 위치(bin/...)에서 상위로 거슬러 올라가며 test_images 하위 자산을 찾는다.
        /// 못 찾으면 개발 머신의 리포 경로로 폴백한다.
        /// </summary>
        private static string ResolveAsset(string rel)
        {
            rel = rel.Replace('/', Path.DirectorySeparatorChar);
            string? dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, "test_images", rel);
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
            return Path.Combine(@"D:\Repo\VMS\test_images", rel);
        }
    }
}
