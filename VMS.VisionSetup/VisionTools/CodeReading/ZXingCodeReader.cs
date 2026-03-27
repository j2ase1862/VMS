using OpenCvSharp;
using System;
using System.Collections.Generic;
using ZXing;
using ZXing.Common;

namespace VMS.VisionSetup.VisionTools.CodeReading
{
    /// <summary>
    /// ZXing.Net 기반 ICodeReader 구현체
    /// </summary>
    public class ZXingCodeReader : ICodeReader
    {
        public List<CodeResult> Read(Mat image, CodeReaderMode mode, bool tryHarder = true)
        {
            if (image == null || image.Empty())
                return new List<CodeResult>();

            // 그레이스케일 변환
            Mat gray;
            bool ownsGray = false;
            if (image.Channels() > 1)
            {
                gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
                ownsGray = true;
            }
            else
            {
                gray = image;
            }

            try
            {
                // 1차 시도: 원본 그레이스케일로 디코딩
                var results = DecodeFromGray(gray, mode, tryHarder);
                if (results.Count > 0)
                    return results;

                // 2차 시도: CLAHE 적용 후 디코딩 (레이저 마킹 등 저대비 코드 대응)
                using var clahe = Cv2.CreateCLAHE(clipLimit: 4.0, tileGridSize: new Size(8, 8));
                using var enhanced = new Mat();
                clahe.Apply(gray, enhanced);

                return DecodeFromGray(enhanced, mode, tryHarder);
            }
            finally
            {
                if (ownsGray)
                    gray.Dispose();
            }
        }

        /// <summary>
        /// 그레이스케일 Mat에서 ZXing 디코딩 수행
        /// </summary>
        private static List<CodeResult> DecodeFromGray(Mat gray, CodeReaderMode mode, bool tryHarder)
        {
            var results = new List<CodeResult>();
            int width = gray.Cols;
            int height = gray.Rows;

            // Mat → byte[] 변환
            byte[] pixelData = new byte[width * height];
            unsafe
            {
                byte* ptr = (byte*)gray.Data;
                long stride = gray.Step();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        pixelData[y * width + x] = ptr[y * stride + x];
                    }
                }
            }

            var luminanceSource = new RGBLuminanceSource(pixelData, width, height, RGBLuminanceSource.BitmapFormat.Gray8);

            var hints = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = GetBarcodeFormats(mode)
            };

            var reader = new BarcodeReaderGeneric
            {
                Options = hints
            };

            var zxingResults = reader.DecodeMultiple(luminanceSource);

            if (zxingResults != null)
            {
                foreach (var zr in zxingResults)
                {
                    if (zr == null) continue;

                    var codeResult = new CodeResult
                    {
                        Text = zr.Text ?? string.Empty,
                        Format = zr.BarcodeFormat.ToString()
                    };

                    // 결과 포인트 추출 (검출 위치)
                    if (zr.ResultPoints != null && zr.ResultPoints.Length > 0)
                    {
                        var points = new List<Point2f>();
                        foreach (var rp in zr.ResultPoints)
                        {
                            if (rp != null)
                                points.Add(new Point2f(rp.X, rp.Y));
                        }
                        codeResult.Points = points.ToArray();
                    }

                    results.Add(codeResult);
                }
            }

            return results;
        }

        private static List<BarcodeFormat> GetBarcodeFormats(CodeReaderMode mode)
        {
            return mode switch
            {
                CodeReaderMode.QRCode => new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                CodeReaderMode.Barcode1D => new List<BarcodeFormat>
                {
                    BarcodeFormat.CODE_128,
                    BarcodeFormat.CODE_39,
                    BarcodeFormat.CODE_93,
                    BarcodeFormat.EAN_13,
                    BarcodeFormat.EAN_8,
                    BarcodeFormat.UPC_A,
                    BarcodeFormat.UPC_E,
                    BarcodeFormat.ITF,
                    BarcodeFormat.CODABAR
                },
                CodeReaderMode.DataMatrix => new List<BarcodeFormat> { BarcodeFormat.DATA_MATRIX },
                CodeReaderMode.PDF417 => new List<BarcodeFormat> { BarcodeFormat.PDF_417 },
                _ => new List<BarcodeFormat>() // Auto: empty = all formats
            };
        }
    }
}
