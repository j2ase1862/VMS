using System;
using System.Collections.Generic;
using System.Text;

namespace VMS.VisionSetup.VisionTools.CodeReading
{
    /// <summary>
    /// GS1 Application Identifier (AI) 파싱 결과 항목.
    /// </summary>
    public class Gs1Element
    {
        public string AI { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// GS1 DataMatrix/QR 데이터 파서.
    /// FNC1(ASCII 29 — Group Separator) 구분자 처리 + 산업 표준 AI 사전 기반 분리.
    /// 가변 길이 AI 뒤에 FNC1이 없으면 데이터 끝까지 흡수.
    /// </summary>
    public static class Gs1Parser
    {
        /// <summary>
        /// FNC1 / Group Separator (ASCII 0x1D)
        /// </summary>
        public const char FNC1 = '\u001D';

        /// <summary>
        /// ZXing이 GS1 심볼에 부여하는 심볼 식별자 접두사.
        /// </summary>
        private static readonly string[] Gs1Prefixes = new[] { "]C1", "]e0", "]d2", "]Q3" };

        /// <summary>
        /// 디코딩된 문자열이 GS1 데이터인지 식별 (FNC1 또는 심볼 식별자 접두사 기준).
        /// </summary>
        public static bool LooksLikeGs1(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.IndexOf(FNC1) >= 0) return true;
            foreach (var p in Gs1Prefixes)
                if (text.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// GS1 데이터 파싱. 정의되지 않은 AI는 4자리까지 추정 후 가변 길이로 처리.
        /// </summary>
        public static List<Gs1Element> Parse(string text)
        {
            var elements = new List<Gs1Element>();
            if (string.IsNullOrEmpty(text)) return elements;

            // 심볼 식별자 접두사 제거
            foreach (var p in Gs1Prefixes)
            {
                if (text.StartsWith(p, StringComparison.Ordinal))
                {
                    text = text.Substring(p.Length);
                    break;
                }
            }

            int i = 0;
            while (i < text.Length)
            {
                // 선행 FNC1 스킵
                if (text[i] == FNC1) { i++; continue; }

                // AI 길이 판정 (2 → 3 → 4자리 순)
                string? ai = null;
                int aiLen = 0;
                for (int len = 2; len <= 4 && i + len <= text.Length; len++)
                {
                    string candidate = text.Substring(i, len);
                    if (!IsAllDigits(candidate)) break;
                    if (AiTable.TryGetValue(candidate, out _))
                    {
                        ai = candidate;
                        aiLen = len;
                        break;
                    }
                }

                // 사전에 없으면 첫 2자리를 AI로 가정
                if (ai == null)
                {
                    if (i + 2 > text.Length || !IsAllDigits(text.Substring(i, 2))) break;
                    ai = text.Substring(i, 2);
                    aiLen = 2;
                }

                i += aiLen;

                AiTable.TryGetValue(ai, out var spec);
                int dataLen;
                if (spec != null && spec.FixedLength > 0)
                {
                    dataLen = Math.Min(spec.FixedLength, text.Length - i);
                }
                else
                {
                    // 가변 길이: 다음 FNC1까지 (없으면 끝까지)
                    int next = text.IndexOf(FNC1, i);
                    dataLen = (next < 0 ? text.Length : next) - i;
                }

                string value = text.Substring(i, dataLen);
                i += dataLen;

                elements.Add(new Gs1Element
                {
                    AI = ai,
                    Title = spec?.Title ?? $"AI({ai})",
                    Value = value
                });
            }

            return elements;
        }

        /// <summary>
        /// 파싱 결과를 사람이 읽기 쉬운 단일 문자열로 직렬화. 예: "(01)08801234567890 (17)260101 (10)LOT123"
        /// </summary>
        public static string Format(IEnumerable<Gs1Element> elements)
        {
            var sb = new StringBuilder();
            foreach (var e in elements)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append('(').Append(e.AI).Append(')').Append(e.Value);
            }
            return sb.ToString();
        }

        private static bool IsAllDigits(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        private record AiSpec(string Title, int FixedLength);

        // GS1 General Specifications 주요 AI 사전.
        // FixedLength = 0 → 가변 길이 (FNC1까지)
        private static readonly Dictionary<string, AiSpec> AiTable = new()
        {
            ["00"] = new("SSCC", 18),
            ["01"] = new("GTIN", 14),
            ["02"] = new("GTIN of Content", 14),
            ["10"] = new("Batch/Lot", 0),
            ["11"] = new("Production Date", 6),
            ["12"] = new("Due Date", 6),
            ["13"] = new("Packaging Date", 6),
            ["15"] = new("Best Before", 6),
            ["16"] = new("Sell By", 6),
            ["17"] = new("Expiration Date", 6),
            ["20"] = new("Variant", 2),
            ["21"] = new("Serial", 0),
            ["22"] = new("CPV", 0),
            ["30"] = new("Variable Count", 0),
            ["37"] = new("Count of Items", 0),
            ["240"] = new("Additional Product ID", 0),
            ["241"] = new("Customer Part Number", 0),
            ["242"] = new("Made-to-Order Variation", 0),
            ["243"] = new("Packaging Component Number", 0),
            ["250"] = new("Secondary Serial", 0),
            ["251"] = new("Reference to Source Entity", 0),
            ["253"] = new("GDTI", 0),
            ["254"] = new("GLN Extension", 0),
            ["255"] = new("GCN", 0),
            ["310"] = new("Net Weight (kg)", 6),
            ["311"] = new("Length (m)", 6),
            ["312"] = new("Width (m)", 6),
            ["313"] = new("Height (m)", 6),
            ["320"] = new("Net Weight (lb)", 6),
            ["330"] = new("Logistic Weight (kg)", 6),
            ["390"] = new("Amount Payable", 0),
            ["400"] = new("Customer PO", 0),
            ["401"] = new("Consignment Number", 0),
            ["402"] = new("Shipment Number", 18),
            ["410"] = new("Ship To GLN", 13),
            ["411"] = new("Bill To GLN", 13),
            ["412"] = new("Purchased From GLN", 13),
            ["413"] = new("Ship For GLN", 13),
            ["414"] = new("Identification of a Physical Location GLN", 13),
            ["420"] = new("Ship To Postal Code", 0),
            ["421"] = new("Ship To Postal Code (with ISO)", 0),
            ["422"] = new("Country of Origin", 3),
            ["423"] = new("Countries of Initial Processing", 0),
            ["424"] = new("Country of Processing", 3),
            ["425"] = new("Country of Disassembly", 0),
            ["426"] = new("Country of Full Process Chain", 3),
            ["7001"] = new("NSN/NATO Stock Number", 13),
            ["7003"] = new("Expiration Date+Time", 10),
            ["7007"] = new("Harvest Date", 0),
            ["7011"] = new("Inspection Date", 0),
            ["8005"] = new("Price per Unit", 6),
            ["8006"] = new("GCTIN", 18),
            ["8017"] = new("GSRN Provider", 18),
            ["8018"] = new("GSRN Recipient", 18),
            ["91"] = new("Internal #1", 0),
            ["92"] = new("Internal #2", 0),
            ["93"] = new("Internal #3", 0),
            ["94"] = new("Internal #4", 0),
            ["95"] = new("Internal #5", 0),
            ["96"] = new("Internal #6", 0),
            ["97"] = new("Internal #7", 0),
            ["98"] = new("Internal #8", 0),
            ["99"] = new("Internal #9", 0),
        };
    }
}
