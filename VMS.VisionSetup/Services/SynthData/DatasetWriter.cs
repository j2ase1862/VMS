using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace VMS.VisionSetup.Services.SynthData
{
    public enum DatasetFormat
    {
        /// <summary>PaddleOCR rec format — train_crops/ + train_rec.txt + val_crops/ + val_rec.txt</summary>
        PaddleOcrRec,
        /// <summary>OCV — per-char crops + char_labels.txt (FontLibrary import 호환)</summary>
        OcvCharPatches
    }

    /// <summary>
    /// 합성 데이터 출력 writer. PaddleOCR rec 학습용 + OCV 학습용 두 포맷 지원.
    /// train/val 분할 자동 처리 (기본 8:2).
    /// </summary>
    public class DatasetWriter
    {
        private readonly string _outputDir;
        private readonly DatasetFormat _format;
        private readonly double _valRatio;
        private readonly List<(string ImagePath, string Label)> _trainEntries = new();
        private readonly List<(string ImagePath, string Label)> _valEntries = new();
        private int _seq;

        public DatasetWriter(string outputDir, DatasetFormat format, double valRatio = 0.2)
        {
            _outputDir = outputDir;
            _format = format;
            _valRatio = Math.Clamp(valRatio, 0.0, 0.5);

            Directory.CreateDirectory(_outputDir);
            if (format == DatasetFormat.PaddleOcrRec)
            {
                Directory.CreateDirectory(Path.Combine(_outputDir, "train_crops"));
                Directory.CreateDirectory(Path.Combine(_outputDir, "val_crops"));
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(_outputDir, "chars"));
            }
        }

        /// <summary>한 라인(또는 문자열) 샘플 저장. 라벨은 OCR ground truth.</summary>
        public void AddSample(Mat image, string label, Random rng)
        {
            bool isVal = rng.NextDouble() < _valRatio;
            string subdir = _format == DatasetFormat.PaddleOcrRec
                ? (isVal ? "val_crops" : "train_crops")
                : "chars";

            _seq++;
            string filename = $"{_seq:D6}.png";
            string relPath = $"{subdir}/{filename}";
            string fullPath = Path.Combine(_outputDir, subdir, filename);
            Cv2.ImWrite(fullPath, image);

            var entry = (relPath, label);
            if (_format == DatasetFormat.OcvCharPatches) _trainEntries.Add(entry);
            else if (isVal) _valEntries.Add(entry);
            else _trainEntries.Add(entry);
        }

        /// <summary>샘플 추가 완료 후 메타 파일 작성.</summary>
        public void FinalizeWriter()
        {
            switch (_format)
            {
                case DatasetFormat.PaddleOcrRec:
                    WriteLabelFile(Path.Combine(_outputDir, "train_rec.txt"), _trainEntries);
                    WriteLabelFile(Path.Combine(_outputDir, "val_rec.txt"), _valEntries);
                    WriteDictFile(Path.Combine(_outputDir, "dict.txt"));
                    // train_ppocr.py가 ppocr_keys_v1.txt 이름을 기대 — 동일 파일 추가 출력
                    WriteDictFile(Path.Combine(_outputDir, "ppocr_keys_v1.txt"));
                    break;
                case DatasetFormat.OcvCharPatches:
                    WriteCharLabelFile(Path.Combine(_outputDir, "char_labels.txt"), _trainEntries);
                    break;
            }
        }

        public (int Train, int Val) Counts => (_trainEntries.Count, _valEntries.Count);

        private static void WriteLabelFile(string path, List<(string Img, string Label)> entries)
        {
            using var sw = new StreamWriter(path, false, new UTF8Encoding(false));
            foreach (var (img, label) in entries)
                sw.WriteLine($"{img}\t{label}");
        }

        // dict.txt = 학습 데이터에 나타난 모든 unique char (PP-OCR 학습 시 character set 정의)
        private void WriteDictFile(string path)
        {
            var charSet = new SortedSet<char>();
            foreach (var (_, label) in _trainEntries.Concat(_valEntries))
                foreach (var c in label) charSet.Add(c);

            using var sw = new StreamWriter(path, false, new UTF8Encoding(false));
            foreach (var c in charSet) sw.WriteLine(c);
        }

        private static void WriteCharLabelFile(string path, List<(string Img, string Label)> entries)
        {
            using var sw = new StreamWriter(path, false, new UTF8Encoding(false));
            foreach (var (img, label) in entries)
                sw.WriteLine($"{img}\t{label}");
        }
    }
}
