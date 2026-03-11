using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VMS.Core.Interfaces;
using VMS.Core.Models.Annotation;

namespace VMS.Core.Services
{
    /// <summary>
    /// 딥러닝 라벨링 데이터셋 관리 서비스.
    /// JSON 기반 파일 저장 방식으로 데이터셋을 관리합니다.
    /// </summary>
    public class AnnotationService : IAnnotationService
    {
        private readonly string _datasetFolderPath;

        private const string DatasetFileName = "dataset.json";
        private const string ImagesFolderName = "images";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public AnnotationService(string datasetFolderPath)
        {
            _datasetFolderPath = datasetFolderPath;

            if (!Directory.Exists(_datasetFolderPath))
                Directory.CreateDirectory(_datasetFolderPath);
        }

        public string DatasetFolderPath => _datasetFolderPath;

        #region Dataset CRUD

        public AnnotationDataset CreateDataset(string name, LabelType taskType)
        {
            var dataset = new AnnotationDataset
            {
                Name = name,
                TaskType = taskType,
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now
            };

            string datasetDir = GetDatasetDirectory(dataset);
            Directory.CreateDirectory(datasetDir);
            Directory.CreateDirectory(Path.Combine(datasetDir, ImagesFolderName));

            SaveDataset(dataset);
            return dataset;
        }

        public AnnotationDataset? LoadDataset(string datasetPath)
        {
            try
            {
                string jsonPath = Path.Combine(datasetPath, DatasetFileName);
                if (!File.Exists(jsonPath))
                    return null;

                string json = File.ReadAllText(jsonPath);
                return JsonSerializer.Deserialize<AnnotationDataset>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"데이터셋 로드 실패: {ex.Message}");
                return null;
            }
        }

        public bool SaveDataset(AnnotationDataset dataset)
        {
            try
            {
                dataset.ModifiedAt = DateTime.Now;

                string datasetDir = GetDatasetDirectory(dataset);
                if (!Directory.Exists(datasetDir))
                    Directory.CreateDirectory(datasetDir);

                string jsonPath = Path.Combine(datasetDir, DatasetFileName);
                string json = JsonSerializer.Serialize(dataset, JsonOptions);
                File.WriteAllText(jsonPath, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"데이터셋 저장 실패: {ex.Message}");
                return false;
            }
        }

        public bool DeleteDataset(string datasetId)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(_datasetFolderPath))
                {
                    var ds = LoadDataset(dir);
                    if (ds?.Id == datasetId)
                    {
                        Directory.Delete(dir, recursive: true);
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"데이터셋 삭제 실패: {ex.Message}");
                return false;
            }
        }

        public List<AnnotationDataset> GetDatasetList()
        {
            var list = new List<AnnotationDataset>();

            if (!Directory.Exists(_datasetFolderPath))
                return list;

            foreach (var dir in Directory.GetDirectories(_datasetFolderPath))
            {
                var ds = LoadDataset(dir);
                if (ds != null)
                    list.Add(ds);
            }

            return list.OrderByDescending(d => d.ModifiedAt).ToList();
        }

        #endregion

        #region Image Management

        public AnnotationImage? AddImage(AnnotationDataset dataset, string sourceImagePath)
        {
            try
            {
                if (!File.Exists(sourceImagePath))
                    return null;

                string datasetDir = GetDatasetDirectory(dataset);
                string imagesDir = Path.Combine(datasetDir, ImagesFolderName);
                if (!Directory.Exists(imagesDir))
                    Directory.CreateDirectory(imagesDir);

                string fileName = Path.GetFileName(sourceImagePath);
                string destPath = GetUniqueFilePath(imagesDir, fileName);
                File.Copy(sourceImagePath, destPath, overwrite: false);

                int width = 0, height = 0;
                using (var mat = Cv2.ImRead(destPath, ImreadModes.Unchanged))
                {
                    if (!mat.Empty())
                    {
                        width = mat.Width;
                        height = mat.Height;
                    }
                }

                var annotationImage = new AnnotationImage
                {
                    ImagePath = Path.GetFileName(destPath),
                    ImageWidth = width,
                    ImageHeight = height,
                    CreatedAt = DateTime.Now,
                    ModifiedAt = DateTime.Now
                };

                dataset.Images.Add(annotationImage);
                dataset.RefreshStatistics();
                return annotationImage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"이미지 추가 실패: {ex.Message}");
                return null;
            }
        }

        public List<AnnotationImage> AddImages(AnnotationDataset dataset, IEnumerable<string> sourceImagePaths)
        {
            var results = new List<AnnotationImage>();
            foreach (var path in sourceImagePaths)
            {
                var img = AddImage(dataset, path);
                if (img != null)
                    results.Add(img);
            }
            return results;
        }

        public bool RemoveImage(AnnotationDataset dataset, string imageId)
        {
            var image = dataset.Images.FirstOrDefault(i => i.Id == imageId);
            if (image == null)
                return false;

            try
            {
                string filePath = GetImageFullPath(dataset, image);
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"이미지 파일 삭제 실패: {ex.Message}");
            }

            dataset.Images.Remove(image);
            dataset.RefreshStatistics();
            return true;
        }

        #endregion

        #region Label Management

        public LabelInfo AddLabel(AnnotationImage image, LabelInfo label)
        {
            image.Labels.Add(label);
            image.IsLabeled = image.Labels.Count > 0;
            image.ModifiedAt = DateTime.Now;
            return label;
        }

        public bool UpdateLabel(AnnotationImage image, LabelInfo label)
        {
            var existing = image.Labels.FirstOrDefault(l => l.Id == label.Id);
            if (existing == null)
                return false;

            int idx = image.Labels.IndexOf(existing);
            image.Labels[idx] = label;
            image.ModifiedAt = DateTime.Now;
            return true;
        }

        public bool RemoveLabel(AnnotationImage image, string labelId)
        {
            var label = image.Labels.FirstOrDefault(l => l.Id == labelId);
            if (label == null)
                return false;

            image.Labels.Remove(label);
            image.IsLabeled = image.Labels.Count > 0;
            image.ModifiedAt = DateTime.Now;
            return true;
        }

        #endregion

        #region Class Management

        public bool AddClass(AnnotationDataset dataset, string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return false;
            if (dataset.Classes.Contains(className))
                return false;

            dataset.Classes.Add(className);
            return true;
        }

        public bool RenameClass(AnnotationDataset dataset, string oldName, string newName)
        {
            if (!dataset.Classes.Contains(oldName) || string.IsNullOrWhiteSpace(newName))
                return false;

            int idx = dataset.Classes.IndexOf(oldName);
            dataset.Classes[idx] = newName;

            foreach (var image in dataset.Images)
                foreach (var label in image.Labels)
                    if (label.ClassName == oldName)
                        label.ClassName = newName;

            return true;
        }

        public bool RemoveClass(AnnotationDataset dataset, string className)
        {
            if (!dataset.Classes.Remove(className))
                return false;

            foreach (var image in dataset.Images)
            {
                var toRemove = image.Labels.Where(l => l.ClassName == className).ToList();
                foreach (var label in toRemove)
                    image.Labels.Remove(label);
                image.IsLabeled = image.Labels.Count > 0;
            }

            dataset.RefreshStatistics();
            return true;
        }

        #endregion

        #region Data Split

        public void AutoSplitDataset(AnnotationDataset dataset)
        {
            var labeled = dataset.Images.Where(i => i.IsLabeled).ToList();
            if (labeled.Count == 0) return;

            var rng = new Random();
            var shuffled = labeled.OrderBy(_ => rng.Next()).ToList();

            int trainCount = (int)(shuffled.Count * dataset.TrainRatio);

            for (int i = 0; i < shuffled.Count; i++)
                shuffled[i].Split = i < trainCount ? DataSplit.Train : DataSplit.Validation;

            foreach (var img in dataset.Images.Where(i => !i.IsLabeled))
                img.Split = DataSplit.Unassigned;

            dataset.RefreshStatistics();
        }

        #endregion

        #region Export — YOLO

        public bool ExportYolo(AnnotationDataset dataset, string outputPath)
        {
            try
            {
                string trainImgDir = Path.Combine(outputPath, "images", "train");
                string valImgDir = Path.Combine(outputPath, "images", "val");
                string trainLblDir = Path.Combine(outputPath, "labels", "train");
                string valLblDir = Path.Combine(outputPath, "labels", "val");

                Directory.CreateDirectory(trainImgDir);
                Directory.CreateDirectory(valImgDir);
                Directory.CreateDirectory(trainLblDir);
                Directory.CreateDirectory(valLblDir);

                var yamlSb = new StringBuilder();
                yamlSb.AppendLine($"train: images/train");
                yamlSb.AppendLine($"val: images/val");
                yamlSb.AppendLine($"nc: {dataset.Classes.Count}");
                yamlSb.Append("names: [");
                yamlSb.Append(string.Join(", ", dataset.Classes.Select(c => $"'{c}'")));
                yamlSb.AppendLine("]");
                File.WriteAllText(Path.Combine(outputPath, "data.yaml"), yamlSb.ToString());

                foreach (var image in dataset.Images.Where(i => i.IsLabeled))
                {
                    bool isTrain = image.Split == DataSplit.Train;
                    string imgDir = isTrain ? trainImgDir : valImgDir;
                    string lblDir = isTrain ? trainLblDir : valLblDir;

                    string srcPath = GetImageFullPath(dataset, image);
                    if (!File.Exists(srcPath)) continue;

                    string destImgPath = Path.Combine(imgDir, Path.GetFileName(image.ImagePath));
                    File.Copy(srcPath, destImgPath, overwrite: true);

                    string lblFileName = Path.GetFileNameWithoutExtension(image.ImagePath) + ".txt";
                    var lblSb = new StringBuilder();

                    foreach (var label in image.Labels)
                    {
                        int classIdx = dataset.Classes.IndexOf(label.ClassName);
                        if (classIdx < 0) continue;

                        double cx = (label.BoundingBox.X + label.BoundingBox.Width / 2.0) / image.ImageWidth;
                        double cy = (label.BoundingBox.Y + label.BoundingBox.Height / 2.0) / image.ImageHeight;
                        double w = (double)label.BoundingBox.Width / image.ImageWidth;
                        double h = (double)label.BoundingBox.Height / image.ImageHeight;

                        lblSb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0} {1:F6} {2:F6} {3:F6} {4:F6}", classIdx, cx, cy, w, h));
                    }

                    File.WriteAllText(Path.Combine(lblDir, lblFileName), lblSb.ToString());
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"YOLO Export 실패: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Export — PaddleOCR Detection

        public bool ExportPaddleOcrDet(AnnotationDataset dataset, string outputPath)
        {
            try
            {
                Directory.CreateDirectory(outputPath);

                string trainImgDir = Path.Combine(outputPath, "train_images");
                string valImgDir = Path.Combine(outputPath, "val_images");
                Directory.CreateDirectory(trainImgDir);
                Directory.CreateDirectory(valImgDir);

                var trainLines = new List<string>();
                var valLines = new List<string>();

                foreach (var image in dataset.Images.Where(i => i.IsLabeled))
                {
                    bool isTrain = image.Split == DataSplit.Train;
                    string imgDir = isTrain ? trainImgDir : valImgDir;

                    string srcPath = GetImageFullPath(dataset, image);
                    if (!File.Exists(srcPath)) continue;

                    string destPath = Path.Combine(imgDir, Path.GetFileName(image.ImagePath));
                    File.Copy(srcPath, destPath, overwrite: true);

                    var annotations = new List<string>();
                    foreach (var label in image.Labels)
                    {
                        var pts = GetLabelPoints(label);
                        string ptsStr = string.Join(",",
                            pts.Select(p => $"[{(int)p.X},{(int)p.Y}]"));
                        string transcription = string.IsNullOrEmpty(label.Transcription)
                            ? label.ClassName : label.Transcription;
                        annotations.Add(
                            $"{{\"transcription\":\"{EscapeJson(transcription)}\",\"points\":[{ptsStr}]}}");
                    }

                    string relPath = Path.GetFileName(image.ImagePath);
                    string line = $"{relPath}\t[{string.Join(",", annotations)}]";

                    if (isTrain)
                        trainLines.Add(line);
                    else
                        valLines.Add(line);
                }

                File.WriteAllLines(Path.Combine(outputPath, "train_det.txt"), trainLines);
                File.WriteAllLines(Path.Combine(outputPath, "val_det.txt"), valLines);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PaddleOCR Det Export 실패: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Export — PaddleOCR Recognition

        public bool ExportPaddleOcrRec(AnnotationDataset dataset, string outputPath)
        {
            try
            {
                Directory.CreateDirectory(outputPath);

                string trainCropDir = Path.Combine(outputPath, "train_crops");
                string valCropDir = Path.Combine(outputPath, "val_crops");
                Directory.CreateDirectory(trainCropDir);
                Directory.CreateDirectory(valCropDir);

                var trainLines = new List<string>();
                var valLines = new List<string>();
                int cropIdx = 0;

                foreach (var image in dataset.Images.Where(i => i.IsLabeled))
                {
                    bool isTrain = image.Split == DataSplit.Train;
                    string cropDir = isTrain ? trainCropDir : valCropDir;

                    string srcPath = GetImageFullPath(dataset, image);
                    if (!File.Exists(srcPath)) continue;

                    using var mat = Cv2.ImRead(srcPath, ImreadModes.Color);
                    if (mat.Empty()) continue;

                    foreach (var label in image.Labels.Where(l => !string.IsNullOrEmpty(l.Transcription)))
                    {
                        var box = label.BoundingBox;
                        var roi = new Rect(
                            Math.Max(0, box.X),
                            Math.Max(0, box.Y),
                            Math.Min(box.Width, mat.Width - Math.Max(0, box.X)),
                            Math.Min(box.Height, mat.Height - Math.Max(0, box.Y)));

                        if (roi.Width < 2 || roi.Height < 2)
                            continue;

                        using var crop = new Mat(mat, roi);
                        string cropName = $"crop_{cropIdx++:D6}.jpg";
                        string cropPath = Path.Combine(cropDir, cropName);
                        Cv2.ImWrite(cropPath, crop);

                        string line = $"{cropName}\t{label.Transcription}";
                        if (isTrain)
                            trainLines.Add(line);
                        else
                            valLines.Add(line);
                    }
                }

                File.WriteAllLines(Path.Combine(outputPath, "train_rec.txt"), trainLines);
                File.WriteAllLines(Path.Combine(outputPath, "val_rec.txt"), valLines);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PaddleOCR Rec Export 실패: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Helpers

        public string GetDatasetDirectory(AnnotationDataset dataset)
        {
            string safeName = string.Join("_",
                dataset.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrEmpty(safeName))
                safeName = dataset.Id;
            return Path.Combine(_datasetFolderPath, safeName);
        }

        public string GetImageFullPath(AnnotationDataset dataset, AnnotationImage image)
        {
            return Path.Combine(GetDatasetDirectory(dataset), ImagesFolderName, image.ImagePath);
        }

        private static string GetUniqueFilePath(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            if (!File.Exists(path))
                return path;

            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            int counter = 1;

            while (File.Exists(path))
            {
                path = Path.Combine(directory, $"{name}_{counter++}{ext}");
            }

            return path;
        }

        private static List<Point2d> GetLabelPoints(LabelInfo label)
        {
            if (label.Points.Count >= 4)
                return label.Points;

            var b = label.BoundingBox;
            return new List<Point2d>
            {
                new(b.X, b.Y),
                new(b.X + b.Width, b.Y),
                new(b.X + b.Width, b.Y + b.Height),
                new(b.X, b.Y + b.Height)
            };
        }

        private static string EscapeJson(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"")
                       .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        #endregion
    }
}
