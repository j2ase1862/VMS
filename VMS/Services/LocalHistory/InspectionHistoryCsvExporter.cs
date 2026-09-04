using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VMS.Services.LocalHistory
{
    /// <summary>
    /// 로컬 검사 이력 CSV 내보내기 — RFC 4180 인용 + UTF-8 BOM (Excel 에서 한글 깨짐 방지).
    /// 시각은 로컬 시간으로 기록한다 (현장 보고서 기준).
    /// </summary>
    public static class InspectionHistoryCsvExporter
    {
        public static readonly string[] Header =
        {
            "InspectedAt", "Verdict", "Recipe", "NgCodes", "Mode", "CycleTimeMs",
            "SerialNumber", "WorkOrderId", "LotId", "ImagePath", "CorrelationKey", "ToolResults"
        };

        public static void Export(IEnumerable<LocalInspectionEntry> entries, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("outputPath");
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            Write(entries, writer);
        }

        public static void Write(IEnumerable<LocalInspectionEntry> entries, TextWriter writer)
        {
            writer.WriteLine(string.Join(",", Header));
            foreach (var e in entries)
                writer.WriteLine(ToCsvLine(e));
        }

        public static string ToCsvLine(LocalInspectionEntry e)
        {
            var tools = new StringBuilder();
            foreach (var t in e.ToolResults)
            {
                if (tools.Length > 0) tools.Append("; ");
                tools.Append(t.ToolName).Append('=').Append(t.ResultText);
                if (!t.Success && !string.IsNullOrEmpty(t.Message))
                    tools.Append(" (").Append(t.Message).Append(')');
            }

            var cells = new[]
            {
                e.InspectedAtLocal.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                e.VerdictText,
                e.RecipeName ?? string.Empty,
                e.NgCodesText,
                e.Mode.ToString(),
                e.CycleTimeMs?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                e.SerialNumber ?? string.Empty,
                e.WorkOrderId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                e.LotId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                e.ImagePath ?? string.Empty,
                e.CorrelationKey ?? string.Empty,
                tools.ToString()
            };
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(cells[i]));
            }
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
