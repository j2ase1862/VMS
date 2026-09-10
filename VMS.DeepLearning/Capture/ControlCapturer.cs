#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VMS.Core.Models.Annotation;
using VMS.DeepLearning.ViewModels;

namespace VMS.DeepLearning.Capture
{
    /// <summary>
    /// DEBUG 전용 — VMS.DeepLearning(라벨링/학습) 화면의 섹션·컨트롤을 개별 PNG 로 캡처하는
    /// 매뉴얼/문서용 유틸. VMS / VMS.AppSetup / VMS.VisionSetup 의 동명 유틸과 동일 원리
    /// (비주얼 트리 순회 + VisualBrush 렌더, 장면별 ViewModel 데모 상태 주입).
    ///
    /// 전체 #if DEBUG 가드 → Release(배포) 빌드에서는 컴파일되지 않으며, App 이
    /// "--capture-controls" 인자로 실행될 때만 호출된다. → 일반 실행/배포 동작에 일절 영향 없음.
    ///
    /// 문서용 상태 주입은 데모 데이터셋(파일 없는 인메모리 모델)을 CurrentDataset 에 주입하고,
    /// 서비스 호출을 타는 훅(ONNX/SAM 모델 로드)은 backing field + OnPropertyChanged 리플렉션으로
    /// 우회한다 — 캡처가 실제 모델 파일 존재 여부에 좌우되지 않게.
    /// </summary>
    internal static class ControlCapturer
    {
        private const double Scale = 2.0;   // 2x (192 DPI)

        public static async Task RunControlsAsync(Window window, LabelingMainViewModel vm, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string logp = Path.Combine(outputDir, "_controls.log");
            var seen = new HashSet<DependencyObject>();
            var manifest = new List<string>();
            int total = 0;

            Brush backdrop = window.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            if (backdrop.CanFreeze) backdrop.Freeze();

            foreach (var (tag, apply, targets) in BuildScenes(window, vm))
            {
                try { apply(); }
                catch (Exception ex) { File.AppendAllText(logp, tag + " apply: " + ex + "\n"); }

                ExpandAllExpanders(window);
                for (int i = 0; i < 3; i++)
                {
                    window.UpdateLayout();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
                await Task.Delay(150);

                // 장면 전체(풀 윈도우) 샷
                try
                {
                    var root = window.Content as FrameworkElement ?? (FrameworkElement)window;
                    RenderRootToFile(root, backdrop, Path.Combine(outputDir, $"{tag}_full.png"));
                    manifest.Add($"| {tag} | Full | Window | (전체 화면) | {tag}_full.png |");
                }
                catch (Exception ex) { File.AppendAllText(logp, tag + " full: " + ex + "\n"); }

                foreach (var (name, find) in targets)
                {
                    try
                    {
                        var fe = find();
                        if (fe == null || fe.ActualWidth < 1 || fe.ActualHeight < 1)
                        { manifest.Add($"| {tag} | Composite | — | {name} | (not found) |"); continue; }
                        string file = $"{tag}_00_{name}.png";
                        if (TryRenderElement(fe, Path.Combine(outputDir, file), backdrop))
                        { total++; manifest.Add($"| {tag} | Composite | {fe.GetType().Name} | {name} | {file} |"); }
                    }
                    catch (Exception ex) { File.AppendAllText(logp, tag + "/" + name + ": " + ex + "\n"); }
                }

                total += CaptureVisibleControls(window, tag, outputDir, seen, manifest, backdrop);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# VMS.DeepLearning 라벨링/학습 화면 컨트롤 캡처 인덱스");
            sb.AppendLine();
            sb.AppendLine($"총 {total} 개 / 출력 폴더: {outputDir}");
            sb.AppendLine();
            sb.AppendLine("| Scene | Kind | Type | Label | File |");
            sb.AppendLine("|-------|------|------|-------|------|");
            foreach (var line in manifest) sb.AppendLine(line);
            File.WriteAllText(Path.Combine(outputDir, "INDEX.md"), sb.ToString(), System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// 장면 목록 — Detection(라벨링/추론/Active Learning/학습) / Segmentation(SAM) / Anomaly(GOOD·DEFECT).
        /// </summary>
        private static IEnumerable<(string tag, Action apply, (string name, Func<FrameworkElement?> find)[] targets)>
            BuildScenes(Window window, LabelingMainViewModel vm)
        {
            // 같은 텍스트가 버튼 등에도 있을 수 있어(예: 툴바 [Export] vs Export 섹션),
            // 모든 매칭을 순회해 Expander 조상이 있는 첫 항목을 취한다.
            // 섹션 헤더에 ⓘ 도움말 Run 이 붙으면 Text 프로퍼티가 비므로(Inlines 에만 존재)
            // InlineText 연결 문자열로 "헤더"/"헤더 ⓘ" 정확일치 매칭.
            FrameworkElement? SectionExpander(string header)
            {
                string withIcon = header + " ⓘ";
                foreach (var d in EnumerateVisibleTree(window))
                    if (d is TextBlock tb)
                    {
                        string t = InlineText(tb);
                        if ((t == header || t == withIcon) && FindAncestor<Expander>(tb) is Expander ex)
                            return ex;
                    }
                return null;
            }
            FrameworkElement? SectionBorder(string text)
            {
                string withIcon = text + " ⓘ";
                foreach (var d in EnumerateVisibleTree(window))
                    if (d is TextBlock tb)
                    {
                        string t = InlineText(tb);
                        if (t == text || t == withIcon)
                            return FindAncestor<Border>(tb);
                    }
                return null;
            }

            yield return ("S1_detection", () =>
            {
                var ds = MakeDemoDataset(DatasetTaskType.Detection, "gear_defects",
                    new[] { "scratch", "dent", "burr" });
                vm.Datasets.Clear();
                vm.Datasets.Add(ds);
                vm.CurrentDataset = ds;
                vm.CurrentImage = ds.Images[0];
                SetField(vm, "_currentImageIndex", 1);
                Notify(vm, nameof(LabelingMainViewModel.CurrentImageIndex));
                vm.SelectedLabel = ds.Images[0].Labels[0];
                vm.NewDatasetName = "new_dataset";

                // Inference Mode — 모델 로드 훅(파일 접근)을 타지 않게 backing field 로 주입.
                SetField(vm, "_isInferenceModeEnabled", true);
                Notify(vm, nameof(LabelingMainViewModel.IsInferenceModeEnabled));
                SetField(vm, "_inferenceModelPath", @"runs\train\gear_defects\best.onnx");
                Notify(vm, nameof(LabelingMainViewModel.InferenceModelPath));
                SetField(vm, "_inferenceStatus", "모델 로드됨 — 이미지 이동마다 자동 검증");
                Notify(vm, nameof(LabelingMainViewModel.InferenceStatus));

                // Active Learning — 문서용 대표 값
                SetField(vm, "_testFolderPath", @"D:\Datasets\lot42_test");
                Notify(vm, nameof(LabelingMainViewModel.TestFolderPath));

                // Training 경로 — 개발 PC 로컬 경로가 스샷에 노출되지 않게 문서용 대표 값으로.
                // (CurrentDataset 주입 뒤에 설정 — AutoMatchTrainingScript 가 덮어쓴 값을 교체)
                vm.TrainingConfig.PythonPath = @"C:\Python312\python.exe";
                vm.TrainingConfig.TrainingScriptPath = @"scripts\train_dfine.py";
                vm.TrainingConfig.OutputDir = @"D:\Models\gear_defects";

                vm.StatusMessage = "데이터셋 'gear_defects' 로드됨 — 이미지 1/3";
            }, new (string, Func<FrameworkElement?>)[]
            {
                ("bar_toolbar", () => SectionBorder("VMS Labeling")),
                ("sec_dataset", () => SectionExpander("Dataset")),
                ("sec_images", () => SectionBorder("Images")),
                ("sec_inference", () => FindAncestor<Border>(FindByContentText<CheckBox>(window, "Inference Mode"))),
                ("sec_active_learning", () => SectionExpander("Active Learning")
                    ?? FindAncestor<Expander>(FindByHeaderText(window, "Active Learning"))),
                ("sec_classes", () => SectionExpander("Classes")),
                ("sec_labels", () => SectionExpander("Labels")),
                ("sec_label_editor", () => SectionBorder("Label Editor")),
                ("sec_export", () => SectionExpander("Export")),
                ("sec_training", () => SectionExpander("Training")),
            });

            yield return ("S2_segmentation", () =>
            {
                var ds = MakeDemoDataset(DatasetTaskType.Segmentation, "connector_masks",
                    new[] { "housing", "pin" });
                vm.Datasets.Clear();
                vm.Datasets.Add(ds);
                vm.CurrentDataset = ds;
                vm.CurrentImage = ds.Images[0];
                SetField(vm, "_currentImageIndex", 1);
                Notify(vm, nameof(LabelingMainViewModel.CurrentImageIndex));

                // SAM 모델 — 로드 훅 없이 문서용 상태만 주입
                SetField(vm, "_samEncoderPath", @"models\sam\mobile_sam_encoder.onnx");
                Notify(vm, nameof(LabelingMainViewModel.SamEncoderPath));
                SetField(vm, "_samDecoderPath", @"models\sam\mobile_sam_decoder.onnx");
                Notify(vm, nameof(LabelingMainViewModel.SamDecoderPath));
                SetField(vm, "_isSamModelLoaded", true);
                Notify(vm, nameof(LabelingMainViewModel.IsSamModelLoaded));
                SetField(vm, "_samStatusMessage", "전경 1점");
                Notify(vm, nameof(LabelingMainViewModel.SamStatusMessage));

                vm.StatusMessage = "SAM 모델 로드됨 — 이미지 위를 클릭해 마스크를 만드세요";
            }, new (string, Func<FrameworkElement?>)[]
            {
                ("sec_sam_model", () => SectionExpander("SAM Model")),
                ("bar_sam_toolbar", () => FindAncestor<Border>(
                    FindAncestor<Border>(FindByExactText(window, "SAM Segment")))),
            });

            yield return ("S3_anomaly", () =>
            {
                var ds = MakeDemoDataset(DatasetTaskType.AnomalyDetection, "casting_anomaly",
                    new[] { "good", "defect" });
                vm.Datasets.Clear();
                vm.Datasets.Add(ds);
                vm.CurrentDataset = ds;
                vm.CurrentImage = ds.Images[0];
                SetField(vm, "_currentImageIndex", 1);
                Notify(vm, nameof(LabelingMainViewModel.CurrentImageIndex));
                vm.StatusMessage = "이미지 1/3 — GOOD/DEFECT 버튼으로 분류하세요";
            }, new (string, Func<FrameworkElement?>)[]
            {
                ("sec_classes_anomaly", () => SectionExpander("Classes")),
                ("sec_current_class", () => SectionBorder("Current Image Class")),
            });
        }

        /// <summary>파일 없는 인메모리 데모 데이터셋 (이미지 경로는 표시용 문자열).</summary>
        private static AnnotationDataset MakeDemoDataset(DatasetTaskType task, string name, string[] classes)
        {
            var ds = new AnnotationDataset { Name = name, DatasetTaskType = task };
            foreach (var c in classes) ds.Classes.Add(c);

            for (int i = 1; i <= 3; i++)
            {
                var img = new AnnotationImage
                {
                    ImagePath = $"cam1_{i:D4}.png",
                    ImageWidth = 1920,
                    ImageHeight = 1080,
                    IsLabeled = i < 3,
                    Split = i == 3 ? DataSplit.Validation : DataSplit.Train,
                };
                if (i < 3 && (task == DatasetTaskType.Detection || task == DatasetTaskType.OCR))
                {
                    img.Labels.Add(new LabelInfo
                    {
                        ClassName = classes[0],
                        LabelType = LabelType.BoundingBox,
                        BoundingBox = new OpenCvSharp.Rect(120 + 40 * i, 80, 220, 140),
                        IsVerified = true,
                    });
                }
                ds.Images.Add(img);
            }
            ds.RefreshStatistics();
            return ds;
        }

        private static void ExpandAllExpanders(DependencyObject root)
        {
            foreach (var d in EnumerateVisibleTree(root))
                if (d is Expander ex) ex.IsExpanded = true;
        }

        private static int CaptureVisibleControls(
            Window window, string tag, string outputDir,
            HashSet<DependencyObject> seen, List<string> manifest, Brush backdrop)
        {
            int count = 0, seq = 0;
            foreach (var element in EnumerateVisibleTree(window))
            {
                if (element is not FrameworkElement fe) continue;
                if (fe.ActualWidth < 1 || fe.ActualHeight < 1) continue;

                string? type = fe switch
                {
                    RadioButton => "RadioButton",
                    CheckBox => "CheckBox",
                    Button => "Button",
                    ComboBox => "ComboBox",
                    Slider => "Slider",
                    TextBox => "TextBox",
                    _ => null
                };
                if (type == null) continue;
                if (!seen.Add(fe)) continue;

                seq++;
                string label = GetControlLabel(fe);
                string file = $"{tag}_{seq:D2}_{type}_{SanitizeName(label)}.png";
                if (TryRenderElement(fe, Path.Combine(outputDir, file), backdrop))
                {
                    count++;
                    manifest.Add($"| {tag} | Control | {type} | {label.Replace("|", "\\|").Replace("\n", " ").Trim()} | {file} |");
                }
            }
            return count;
        }

        private static string GetControlLabel(FrameworkElement fe)
        {
            string auto = System.Windows.Automation.AutomationProperties.GetName(fe);
            if (!string.IsNullOrWhiteSpace(auto)) return auto;
            switch (fe)
            {
                case ContentControl cc when cc.Content is string s && !string.IsNullOrWhiteSpace(s): return s;
                case ContentControl cc when cc.Content is TextBlock tb: return tb.Text;
                case TextBox t: return !string.IsNullOrEmpty(t.Name) ? t.Name : (!string.IsNullOrEmpty(t.Text) ? t.Text : "TextBox");
                case ComboBox cb: return cb.SelectedItem?.ToString() ?? (!string.IsNullOrEmpty(cb.Name) ? cb.Name : "ComboBox");
                default: return string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.Name;
            }
        }

        private static IEnumerable<DependencyObject> EnumerateVisibleTree(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is UIElement ui && ui.Visibility != Visibility.Visible) continue;
                yield return child;
                foreach (var d in EnumerateVisibleTree(child)) yield return d;
            }
        }

        /// <summary>Run 인라인 구성 TextBlock 은 Text 가 비어 있어 Inlines 를 이어붙여 읽는다.</summary>
        private static string InlineText(TextBlock tb)
        {
            if (!string.IsNullOrEmpty(tb.Text)) return tb.Text;
            var sb = new System.Text.StringBuilder();
            foreach (var inline in tb.Inlines)
                if (inline is System.Windows.Documents.Run r) sb.Append(r.Text);
            return sb.ToString();
        }

        private static TextBlock? FindByExactText(DependencyObject root, string text)
        {
            foreach (var d in EnumerateVisibleTree(root))
                if (d is TextBlock tb && InlineText(tb) == text) return tb;
            return null;
        }

        /// <summary>Expander Header 가 문자열인 경우 — ContentPresenter 가 만든 TextBlock 을 찾는다.</summary>
        private static TextBlock? FindByHeaderText(DependencyObject root, string text)
        {
            foreach (var d in EnumerateVisibleTree(root))
                if (d is TextBlock tb && tb.Text.Trim() == text) return tb;
            return null;
        }

        private static T? FindByContentText<T>(DependencyObject root, string text) where T : ContentControl
        {
            foreach (var d in EnumerateVisibleTree(root))
                if (d is T cc && cc.Content is string s && s == text) return cc;
            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
        {
            while (node != null)
            {
                node = VisualTreeHelper.GetParent(node);
                if (node is T hit) return hit;
            }
            return null;
        }

        private static bool TryRenderElement(FrameworkElement fe, string path, Brush backdrop)
        {
            try
            {
                var bounds = new Rect(new Point(0, 0), new Size(fe.ActualWidth, fe.ActualHeight));
                var dv = new DrawingVisual();
                using (var ctx = dv.RenderOpen())
                {
                    ctx.DrawRectangle(backdrop, null, bounds);
                    ctx.DrawRectangle(new VisualBrush(fe) { Stretch = Stretch.None }, null, bounds);
                }
                var rtb = new RenderTargetBitmap(
                    (int)(bounds.Width * Scale), (int)(bounds.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(path);
                enc.Save(fs);
                return true;
            }
            catch { return false; }
        }

        // ───────────────────── 레지스트리 창 캡처 (--capture-dialogs) ─────────────────────

        /// <summary>
        /// MLOps 레지스트리 연동 창 2종을 문서용 상태로 띄워 전체 렌더한다 (VMS 의 RunWindowsFullAsync 와 같은 원리).
        /// 서버 없이 캡처한다 — 로그인·목록·판은 VM 의 backing field 에 직접 넣고, 네트워크를 타는 훅
        /// (DatasetDownload 의 SelectedDataset → 판 목록 조회)은 SetField + Notify 로 우회한다.
        /// 장면: ModelUpload_login(로그인 전) · ModelUpload_ready(계열 선택·올리기 직전) · DatasetDownload_ready(판 선택·받기 직전).
        /// </summary>
        public static async Task RunDialogsAsync(Window owner, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            string logp = Path.Combine(outputDir, "_dialogs.log");

            foreach (var (name, make) in BuildDialogScenes())
            {
                Window? win = null;
                try
                {
                    win = make();
                    win.Owner = owner;
                    win.ShowInTaskbar = false;
                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    win.Left = -32000; win.Top = -32000;   // 사용자 화면에 깜빡이지 않게 오프스크린

                    double w = double.IsNaN(win.Width) || win.Width < 1 ? 560 : win.Width;
                    double h = double.IsNaN(win.Height) || win.Height < 1 ? 600 : win.Height;
                    win.SizeToContent = SizeToContent.Height;   // 표준 창 — 콘텐츠 높이에 맞춘다
                    win.Show();

                    for (int i = 0; i < 3; i++)
                    {
                        win.Measure(new Size(w, double.PositiveInfinity));
                        win.Arrange(new Rect(new Point(0, 0), new Size(w, win.DesiredSize.Height)));
                        win.UpdateLayout();
                        await win.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }
                    await Task.Delay(200);

                    var root = win.Content as FrameworkElement ?? (FrameworkElement)win;
                    Brush backdrop = win.Background ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
                    if (backdrop.CanFreeze) backdrop.Freeze();
                    double rw = root.ActualWidth > 1 ? root.ActualWidth : w;
                    double rh = root.ActualHeight > 1 ? root.ActualHeight : h;
                    RenderRootToFile(root, backdrop, rw, rh, Path.Combine(outputDir, name + ".png"));
                    File.AppendAllText(logp, $"{name}: {rw:0}x{rh:0}\n");
                }
                catch (Exception ex)
                {
                    File.AppendAllText(logp, name + ": " + ex + "\n");
                }
                finally { try { win?.Close(); } catch { } }
            }
        }

        private static IEnumerable<(string name, Func<Window> make)> BuildDialogScenes()
        {
            const string registry = "http://mlops-server:5310";
            const string web = "http://localhost:5292";

            // [레지스트리에 등록] — 로그인 전. 파일 정보(이름·작업 유형·클래스)만 채워진 첫 화면.
            yield return ("ModelUpload_login", () =>
            {
                var vm = new VMS.Core.ViewModels.ModelUploadViewModel(
                    registry, web, @"D:\Models\gear_defects\best.onnx", DatasetTaskType.Detection,
                    new[] { "scratch", "dent", "burr" }, "gear_defects");
                SetField(vm, "<FileSizeText>k__BackingField", "12.4 MB");   // 파일 없이 캡처 — 문서용 대표 크기
                vm.Username = "admin";
                return new Views.ModelUploadWindow(vm);
            });

            // [레지스트리에 등록] — 로그인 뒤 기존 계열을 골라 올리기 직전.
            yield return ("ModelUpload_ready", () =>
            {
                var vm = new VMS.Core.ViewModels.ModelUploadViewModel(
                    registry, web, @"D:\Models\gear_defects\best.onnx", DatasetTaskType.Detection,
                    new[] { "scratch", "dent", "burr" }, "gear_defects");
                SetField(vm, "<FileSizeText>k__BackingField", "12.4 MB");
                vm.Models.Add(new VMS.Core.ViewModels.ModelUploadViewModel.ModelChoice(
                    Guid.NewGuid(), "gear_defects", "Detection · 3 클래스 · 버전 4개 · 운영 v3"));
                vm.Models.Add(new VMS.Core.ViewModels.ModelUploadViewModel.ModelChoice(
                    Guid.NewGuid(), "gear_defects_line2", "Detection · 3 클래스 · 버전 1개"));
                SetField(vm, "_signedIn", true);
                SetField(vm, "_signedInAs", "admin (Admin)");
                Notify(vm, nameof(VMS.Core.ViewModels.ModelUploadViewModel.SignedIn));
                Notify(vm, nameof(VMS.Core.ViewModels.ModelUploadViewModel.SignedInAs));
                Notify(vm, nameof(VMS.Core.ViewModels.ModelUploadViewModel.ShowSignIn));
                vm.CreateNew = false;
                vm.SelectedModel = vm.Models[0];
                vm.License = "Apache-2.0";
                vm.Notes = "lot42 불량 120장 추가 학습, mAP50 0.91";
                vm.Status = "레지스트리 연결됨 — 계열 2개";
                return new Views.ModelUploadWindow(vm);
            });

            // [웹 데이터셋 내려받기] — 로그인 뒤 데이터셋·판을 골라 받기 직전.
            yield return ("DatasetDownload_ready", () =>
            {
                var vm = new VMS.Core.ViewModels.DatasetDownloadViewModel(
                    registry, web, DatasetTaskType.Detection, @"D:\Datasets\export");
                var dsId = Guid.NewGuid();
                vm.Datasets.Add(new VMS.Core.ViewModels.DatasetDownloadViewModel.DatasetChoice(
                    dsId, "gear_defects", "이미지 1,240장 · 라벨 3,812개 · 판 2개", "Detection"));
                vm.Datasets.Add(new VMS.Core.ViewModels.DatasetDownloadViewModel.DatasetChoice(
                    Guid.NewGuid(), "connector_pins", "이미지 380장 · 라벨 1,102개 · 판 1개", "Detection"));
                SetField(vm, "_signedIn", true);
                SetField(vm, "_signedInAs", "admin (Admin)");
                Notify(vm, nameof(VMS.Core.ViewModels.DatasetDownloadViewModel.SignedIn));
                Notify(vm, nameof(VMS.Core.ViewModels.DatasetDownloadViewModel.SignedInAs));
                Notify(vm, nameof(VMS.Core.ViewModels.DatasetDownloadViewModel.ShowSignIn));
                // SelectedDataset 은 setter 훅이 판 목록을 서버에서 조회하므로 field 로 넣는다.
                SetField(vm, "_selectedDataset", vm.Datasets[0]);
                Notify(vm, nameof(VMS.Core.ViewModels.DatasetDownloadViewModel.SelectedDataset));
                var v2 = new VMS.Core.Services.RegistryDatasetVersion
                {
                    Id = Guid.NewGuid(), Name = "v2-lot42", TaskType = "Detection", ExportFormat = "yolo",
                    SizeBytes = 812L * 1024 * 1024, ImageCount = 1240, AnnotationCount = 3812,
                    Classes = new[] { "scratch", "dent", "burr" }
                };
                var v1 = new VMS.Core.Services.RegistryDatasetVersion
                {
                    Id = Guid.NewGuid(), Name = "v1-initial", TaskType = "Detection", ExportFormat = "yolo",
                    SizeBytes = 610L * 1024 * 1024, ImageCount = 960, AnnotationCount = 2901,
                    Classes = new[] { "scratch", "dent", "burr" }
                };
                vm.Versions.Add(new VMS.Core.ViewModels.DatasetDownloadViewModel.VersionChoice(
                    v2, "v2-lot42 (2026-09-09)", "이미지 1,240장 · 라벨 3,812개 · 812 MB"));
                vm.Versions.Add(new VMS.Core.ViewModels.DatasetDownloadViewModel.VersionChoice(
                    v1, "v1-initial (2026-08-20)", "이미지 960장 · 라벨 2,901개 · 610 MB"));
                vm.SelectedVersion = vm.Versions[0];
                vm.ReviewedOnly = true;
                vm.Status = "판 2개 — 최신 판을 선택했습니다";
                return new Views.DatasetDownloadWindow(vm);
            });
        }

        private static void RenderRootToFile(FrameworkElement root, Brush backdrop, double rw, double rh, string path)
        {
            // 표준 창은 OS 제목표시줄이 비클라이언트라 콘텐츠만 렌더된다 — 문서 그림이 답답하지 않게 창 여백만큼 테두리를 둔다.
            const double pad = 12;
            var outer = new Rect(new Point(0, 0), new Size(rw + pad * 2, rh + pad * 2));
            var inner = new Rect(new Point(pad, pad), new Size(rw, rh));
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                ctx.DrawRectangle(backdrop, null, outer);
                ctx.DrawRectangle(new VisualBrush(root)
                { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, inner);
            }
            var rtb = new RenderTargetBitmap(
                (int)(outer.Width * Scale), (int)(outer.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        private static void RenderRootToFile(FrameworkElement root, Brush backdrop, string path)
        {
            double rw = root.ActualWidth > 1 ? root.ActualWidth : 1400;
            double rh = root.ActualHeight > 1 ? root.ActualHeight : 900;
            var bounds = new Rect(new Point(0, 0), new Size(rw, rh));
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                ctx.DrawRectangle(backdrop, null, bounds);
                ctx.DrawRectangle(new VisualBrush(root)
                { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, bounds);
            }
            var rtb = new RenderTargetBitmap(
                (int)(rw * Scale), (int)(rh * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            enc.Save(fs);
        }

        private static string SanitizeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unnamed";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('_');
            }
            string r = sb.ToString();
            if (r.Length > 40) r = r.Substring(0, 40);
            return r.Length == 0 ? "unnamed" : r;
        }

        private static void SetField(object target, string field, object? value)
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) { f.SetValue(target, value); return; }
            }
            throw new MissingFieldException(target.GetType().Name, field);
        }

        private static void Notify(object vm, string prop)
        {
            for (var t = vm.GetType(); t != null; t = t.BaseType)
            {
                var m = t.GetMethod("OnPropertyChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null, new[] { typeof(string) }, null);
                if (m != null) { m.Invoke(vm, new object[] { prop }); return; }
            }
        }
    }
}
#endif
