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
                vm.TrainingConfig.TrainingScriptPath = @"scripts\train_yolo.py";
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
