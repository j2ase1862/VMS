#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VMS.AppSetup.Models;
using VMS.AppSetup.ViewModels;
using VMS.Camera.Models;
using VMS.PLC.Models;

namespace VMS.AppSetup.Capture
{
    /// <summary>
    /// DEBUG 전용 — WPF 컨트롤을 개별 PNG 로 코드 캡처하는 매뉴얼/문서용 유틸리티.
    ///
    /// PicPick 등의 "윈도우 컨트롤 캡처"는 자식 Win32 HWND 를 열거하는데,
    /// WPF 컨트롤은 최상위 Window 1개만 HWND 를 가지고 내부는 전부 drawn 요소라
    /// 개별 컨트롤로 잡히지 않는다. 그래서 비주얼 트리를 직접 순회하여
    /// RenderTargetBitmap 으로 컨트롤별 PNG 를 떨어뜨린다.
    ///
    /// 전체 파일이 #if DEBUG 로 감싸여 Release(배포) 빌드에서는 컴파일되지 않으며,
    /// Debug 빌드에서도 App 이 "--capture-controls" 인자로 실행될 때만 호출된다.
    /// → 일반 실행/배포 동작에 일절 영향 없음.
    /// </summary>
    internal static class ControlCapturer
    {
        private const double Scale = 2.0;       // 2x (192 DPI) — 매뉴얼용 선명도
        private const int TotalPages = 6;

        /// <summary>
        /// 한 페이지 안에서 캡처할 "장면" — 기본 상태 외에 조건부 패널을 켠 변형도 포함.
        /// Apply 로 ViewModel 상태를 바꾼 뒤, 그 상태에서 새로 보이는 컨트롤만 캡처한다.
        /// </summary>
        private sealed record Scene(int Page, string Tag, Action<SetupViewModel> Apply);

        /// <summary>
        /// 6 개 페이지를 차례로 전환하며 보이는 모든 대상 컨트롤을 PNG 로 저장한다.
        /// 조건부로 숨겨지는 컨트롤(Serial/Modbus/로봇 활성/카메라 타입별 패널 등)은
        /// 변형 장면(Scene)에서 상태를 켜 노출시킨 뒤 캡처한다.
        /// </summary>
        public static async Task RunAsync(Window window, SetupViewModel vm, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            var seen = new HashSet<DependencyObject>();
            var manifest = new List<string>();
            int total = 0;

            var size = new Size(
                double.IsNaN(window.Width) ? 960 : window.Width,
                double.IsNaN(window.Height) ? 820 : window.Height);

            // 다크 테마 컨트롤이 흰 문서 배경에서 안 보이는 문제 방지 — 앱의 윈도우 배경을 깔고 렌더.
            Brush backdrop = window.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            if (backdrop.CanFreeze) backdrop.Freeze();

            foreach (var scene in BuildScenes())
            {
                vm.CurrentPage = scene.Page;
                scene.Apply(vm);

                // headless(무 데스크톱) 환경에서도 동작하도록 레이아웃을 코드로 강제.
                // Measure/Arrange 두 번 — 상태 변경으로 새로 생성된 ItemsControl 컨테이너까지 배치.
                for (int pass = 0; pass < 2; pass++)
                {
                    window.Measure(size);
                    window.Arrange(new Rect(new Point(0, 0), size));
                    window.UpdateLayout();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                }

                total += CaptureVisible(window, scene.Page, scene.Tag, outputDir, seen, manifest, backdrop);
            }

            // 인덱스(매니페스트) 기록 — 어떤 파일이 무슨 컨트롤인지 한눈에.
            var sb = new StringBuilder();
            sb.AppendLine("# VMS.AppSetup 컨트롤 캡처 인덱스");
            sb.AppendLine();
            sb.AppendLine($"총 {total} 개 컨트롤 / 출력 폴더: {outputDir}");
            sb.AppendLine();
            sb.AppendLine("| Page | Scene | Type | Label | File |");
            sb.AppendLine("|------|-------|------|-------|------|");
            foreach (var line in manifest)
                sb.AppendLine(line);
            File.WriteAllText(Path.Combine(outputDir, "INDEX.md"), sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// 페이지별 캡처 장면 목록. 각 페이지는 "default" 장면 + 조건부 패널을 켠 변형 장면을 갖는다.
        /// 같은 컨트롤은 seen 집합으로 1회만 캡처되므로, 변형 장면에서는 새로 노출된 것만 잡힌다.
        /// </summary>
        private static IEnumerable<Scene> BuildScenes()
        {
            // ── Page 1~2: 조건부 패널 없음 ──
            yield return new Scene(1, "default", _ => { });
            yield return new Scene(2, "default", _ => { });

            // ── Page 3 (Camera): 카메라 타입별 파라미터 패널(AreaScan/LineScan/3D/FrameGrabber) ──
            yield return new Scene(3, "default", _ => { });
            yield return new Scene(3, "camtypes", vm =>
            {
                vm.CameraMode = CameraMode.Virtual;
                vm.Cameras.Clear();
                // Area Scan 2D — Exposure/Gain 패널
                vm.Cameras.Add(new CameraConfiguration
                {
                    Name = "AreaCam", IpAddress = "192.168.0.101",
                    Manufacturer = CameraManufacturer.HIK, CameraType = CameraType.AreaScan2D
                });
                // Line Scan 2D + Encoder — Trigger/LineRate/ScanLength + Encoder Res 패널
                vm.Cameras.Add(new CameraConfiguration
                {
                    Name = "LineCam", IpAddress = "192.168.0.102",
                    Manufacturer = CameraManufacturer.Basler, CameraType = CameraType.LineScan2D,
                    TriggerSource = TriggerSource.Encoder
                });
                // Area Scan 3D — 3D(Capture Mode/Filter/Z-range) 패널
                vm.Cameras.Add(new CameraConfiguration
                {
                    Name = "Cam3D", IpAddress = "192.168.0.103",
                    Manufacturer = CameraManufacturer.Mech_Mind, CameraType = CameraType.AreaScan3D
                });
                // Matrox — Frame Grabber(MIL: Board Type/#/Digitizer/DCF) 패널
                vm.Cameras.Add(new CameraConfiguration
                {
                    Name = "GrabberCam", IpAddress = "192.168.0.104",
                    Manufacturer = CameraManufacturer.Matrox, CameraType = CameraType.AreaScan2D
                });
            });

            // ── Page 4 (PLC): Ethernet(기본) / Modbus Unit ID / Serial 분기 ──
            yield return new Scene(4, "ethernet", vm =>
            {
                vm.SelectedPlcVendor = PlcVendor.Mitsubishi;
                vm.SelectedCommunicationType = PlcCommunicationType.Ethernet;
                vm.UseHeartbeat = true;   // Heartbeat Address 입력칸 활성
            });
            yield return new Scene(4, "modbus", vm => vm.SelectedPlcVendor = PlcVendor.Modbus);
            yield return new Scene(4, "serial", vm =>
            {
                vm.SelectedCommunicationType = PlcCommunicationType.Serial;
            });

            // ── Page 5 (Robot): 비활성(기본) / 활성 + Doosan + Modbus-TCP ──
            yield return new Scene(5, "disabled", vm => vm.IsRobotEnabled = false);
            yield return new Scene(5, "enabled", vm =>
            {
                vm.IsRobotEnabled = true;
                vm.SelectedRobotVendor = RobotVendor.Doosan;          // ShowRobotModbusOption
                vm.SelectedRobotProtocolMode = RobotProtocolMode.ModbusTcp; // IsRobotModbusMode
            });

            // ── Page 6 (IO Board): 보드 추가(폼에 값 채워진 활성 상태) 먼저 캡처 ──
            // IO 보드 폼 컨트롤은 항상 보이고 IsEnabled 만 토글되므로, 값이 채워진
            // 상태를 먼저 캡처해야 매뉴얼에 빈 폼 대신 실제 입력 예시가 남는다.
            yield return new Scene(6, "withboard", vm =>
            {
                if (vm.SelectedIoBoard is null && vm.AddIoBoardCommand.CanExecute(null))
                    vm.AddIoBoardCommand.Execute(null);
            });
        }

        private static int CaptureVisible(
            Window window, int page, string sceneTag, string outputDir,
            HashSet<DependencyObject> seen, List<string> manifest, Brush backdrop)
        {
            int count = 0;
            int seq = 0;

            foreach (var element in EnumerateVisualTree(window))
            {
                if (element is not FrameworkElement fe) continue;
                // IsVisible 는 presentation source(렌더) 가 있어야 true 라 headless 에선 못 씀.
                // 대신 트리 순회에서 Collapsed 서브트리를 가지치기 + 실제 배치 크기로 판정.
                if (fe.ActualWidth < 1 || fe.ActualHeight < 1) continue;

                string? type = Classify(fe);
                if (type == null) continue;

                // 라벨(TextBlock)은 Button/RadioButton 등의 내부 콘텐츠면 중복이라 제외.
                if (fe is TextBlock && IsInsideCapturedControl(fe)) continue;

                if (!seen.Add(fe)) continue;   // 헤더/푸터 등 페이지 공통 컨트롤 중복 방지

                seq++;
                string label = GetLabel(fe);
                string fileName = $"P{page}_{sceneTag}_{seq:D2}_{type}_{Sanitize(label)}.png";
                string fullPath = Path.Combine(outputDir, fileName);

                if (TryRender(fe, fullPath, backdrop))
                {
                    count++;
                    manifest.Add($"| {page} | {sceneTag} | {type} | {Escape(label)} | {fileName} |");
                }
            }

            return count;
        }

        /// <summary>대상 컨트롤 분류 — 해당 없으면 null.</summary>
        private static string? Classify(FrameworkElement fe) => fe switch
        {
            RadioButton => "RadioButton",
            CheckBox => "CheckBox",
            Button => "Button",
            ComboBox => "ComboBox",
            ListBox => "ListBox",
            PasswordBox => "PasswordBox",
            TextBox => "TextBox",
            TextBlock => "Label",
            _ => null
        };

        private static string GetLabel(FrameworkElement fe)
        {
            switch (fe)
            {
                case TextBlock tb:
                    return tb.Text;
                case ContentControl cc:
                    if (cc.Content is string s) return s;
                    if (cc.Content is TextBlock inner) return inner.Text;
                    return cc.Name;
                case TextBox txt:
                    return !string.IsNullOrEmpty(txt.Name) ? txt.Name
                         : !string.IsNullOrEmpty(txt.Text) ? txt.Text : "TextBox";
                case PasswordBox pb:
                    return !string.IsNullOrEmpty(pb.Name) ? pb.Name : "PasswordBox";
                case ComboBox cb:
                    return cb.SelectedItem?.ToString() ?? (!string.IsNullOrEmpty(cb.Name) ? cb.Name : "ComboBox");
                default:
                    return string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.Name;
            }
        }

        /// <summary>TextBlock 이 Button/RadioButton/CheckBox/Combo 등의 내부 콘텐츠인지.</summary>
        private static bool IsInsideCapturedControl(DependencyObject node)
        {
            var parent = VisualTreeHelper.GetParent(node);
            while (parent != null)
            {
                if (parent is ButtonBase || parent is Selector || parent is PasswordBox)
                    return true;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return false;
        }

        /// <summary>
        /// 컨트롤을 화면상의 위치/오프셋과 무관하게 원점에 렌더링하여 PNG 저장.
        /// VisualBrush 로 그려 트리 깊숙한 요소의 좌표 오프셋 문제를 회피한다.
        /// </summary>
        private static bool TryRender(FrameworkElement fe, string path, Brush backdrop)
        {
            try
            {
                double w = fe.ActualWidth;
                double h = fe.ActualHeight;
                var bounds = new Rect(new Point(0, 0), new Size(w, h));

                var dv = new DrawingVisual();
                using (var ctx = dv.RenderOpen())
                {
                    ctx.DrawRectangle(backdrop, null, bounds);   // 앱 다크 배경 먼저
                    var brush = new VisualBrush(fe) { Stretch = Stretch.None };
                    ctx.DrawRectangle(brush, null, bounds);      // 그 위에 컨트롤
                }

                var rtb = new RenderTargetBitmap(
                    (int)(w * Scale), (int)(h * Scale),
                    96 * Scale, 96 * Scale,
                    PixelFormats.Pbgra32);
                rtb.Render(dv);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(path);
                encoder.Save(fs);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateVisualTree(DependencyObject root)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                // Collapsed/Hidden 서브트리(비활성 페이지·조건부 패널)는 통째로 건너뛴다.
                if (child is UIElement ui && ui.Visibility != Visibility.Visible)
                    continue;

                yield return child;
                foreach (var descendant in EnumerateVisualTree(child))
                    yield return descendant;
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unnamed";
            var sb = new StringBuilder();
            foreach (char c in s.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(c);
                else if (char.IsWhiteSpace(c))
                    sb.Append('_');
                // 그 외 특수문자는 버림
            }
            string result = sb.ToString();
            if (result.Length > 40) result = result.Substring(0, 40);
            return result.Length == 0 ? "unnamed" : result;
        }

        private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Trim();
    }
}
#endif
