using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace VMS.VisionSetup.VisionTools.Identification
{
    /// <summary>
    /// OCR 결과 형식 프리셋. 산업 현장에서 자주 쓰는 날짜/시간/LOT 패턴.
    /// </summary>
    public enum OcrOutputFormatPreset
    {
        [Description("(없음 — 형식 무시)")]
        None,
        [Description("DD/MM/YYYY")]
        DateDmy,
        [Description("MM/DD/YYYY")]
        DateMdy,
        [Description("YYYY-MM-DD")]
        DateYmdDash,
        [Description("YYYY/MM/DD")]
        DateYmdSlash,
        [Description("DD/MM/YY")]
        DateDmyShort,
        [Description("DD/MM/YYYY HH:MM")]
        DateTimeMin,
        [Description("DD/MM/YYYY HH:MM:SS")]
        DateTimeSec,
        [Description("DD/MM/YYYY HH:MM:SS.fff")]
        DateTimeMs,
        [Description("YYYY-MM-DDTHH:MM:SS (ISO)")]
        IsoDateTime,
        [Description("LOT 6자리")]
        Lot6,
        [Description("LOT 7자리")]
        Lot7,
        [Description("LOT 8자리")]
        Lot8,
        [Description("직접 입력")]
        Custom
    }

    /// <summary>
    /// OCR 결과 형식 매처.
    /// 토큰 기반 mini-DSL: 'D/M/Y/H/S/f' = 숫자 자리, 'L' = 영문자, 'A' = 영숫자,
    /// '?' = 임의, 그 외 모든 char = 리터럴 (해당 위치를 강제 치환).
    /// 다중 패턴 지원 (줄바꿈 구분, 길이 긴 순으로 시도).
    /// </summary>
    public static class OcrFormatMatcher
    {
        // 숫자 자리로 취급할 토큰 (대소문자 무관)
        private static readonly HashSet<char> DigitTokens = new()
        {
            'D', 'd', 'M', 'm', 'Y', 'y', 'H', 'h', 'S', 's', 'f', 'F'
        };

        public static string GetPatternFor(OcrOutputFormatPreset preset)
        {
            return preset switch
            {
                OcrOutputFormatPreset.None => string.Empty,
                OcrOutputFormatPreset.DateDmy => "DD/MM/YYYY",
                OcrOutputFormatPreset.DateMdy => "MM/DD/YYYY",
                OcrOutputFormatPreset.DateYmdDash => "YYYY-MM-DD",
                OcrOutputFormatPreset.DateYmdSlash => "YYYY/MM/DD",
                OcrOutputFormatPreset.DateDmyShort => "DD/MM/YY",
                OcrOutputFormatPreset.DateTimeMin => "DD/MM/YYYY HH:MM",
                OcrOutputFormatPreset.DateTimeSec => "DD/MM/YYYY HH:MM:SS",
                OcrOutputFormatPreset.DateTimeMs => "DD/MM/YYYY HH:MM:SS.fff",
                OcrOutputFormatPreset.IsoDateTime => "YYYY-MM-DDTHH:MM:SS",
                OcrOutputFormatPreset.Lot6 => "DDDDDD",
                OcrOutputFormatPreset.Lot7 => "DDDDDDD",
                OcrOutputFormatPreset.Lot8 => "DDDDDDDD",
                _ => string.Empty
            };
        }

        /// <summary>
        /// OCR 원본 텍스트에 형식 매칭 + 리터럴 강제 보정 적용.
        /// 매칭 성공 시 보정된 문자열, 실패 시 원본 그대로 반환.
        /// patterns가 비어있으면 원본 반환.
        /// </summary>
        public static (string Text, bool Matched, string? MatchedPattern) Apply(string rawText, string patterns)
        {
            if (string.IsNullOrEmpty(rawText) || string.IsNullOrWhiteSpace(patterns))
                return (rawText, false, null);

            var patternList = SplitPatterns(patterns);
            if (patternList.Count == 0) return (rawText, false, null);

            // 길이 긴 순 — 더 specific한 패턴 우선
            patternList.Sort((a, b) => b.Length.CompareTo(a.Length));

            foreach (var pattern in patternList)
            {
                if (TryMatch(rawText, pattern, out var corrected))
                    return (corrected, true, pattern);
            }
            return (rawText, false, null);
        }

        private static List<string> SplitPatterns(string raw)
        {
            var list = new List<string>();
            foreach (var line in raw.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = line.Trim();
                if (!string.IsNullOrEmpty(p)) list.Add(p);
            }
            return list;
        }

        // 1) 정확 길이 매칭 → 2) 슬라이딩 윈도우. 첫 성공한 매칭 반환.
        private static bool TryMatch(string text, string pattern, out string corrected)
        {
            corrected = string.Empty;
            if (pattern.Length == 0) return false;

            if (text.Length == pattern.Length && TryApply(text, pattern, out corrected))
                return true;

            if (text.Length > pattern.Length)
            {
                for (int start = 0; start <= text.Length - pattern.Length; start++)
                {
                    var sub = text.Substring(start, pattern.Length);
                    if (TryApply(sub, pattern, out corrected))
                        return true;
                }
            }
            return false;
        }

        // 위치별 검증/치환. 리터럴은 무조건 pattern char로 강제, 토큰은 char class만 검증.
        private static bool TryApply(string text, string pattern, out string corrected)
        {
            var sb = new StringBuilder(pattern.Length);
            for (int i = 0; i < pattern.Length; i++)
            {
                char tc = text[i];
                char pc = pattern[i];

                if (IsToken(pc))
                {
                    if (!TokenMatches(tc, pc)) { corrected = string.Empty; return false; }
                    sb.Append(tc);
                }
                else
                {
                    // 리터럴 — OCR이 무엇을 봤든 강제 치환
                    sb.Append(pc);
                }
            }
            corrected = sb.ToString();
            return true;
        }

        private static bool IsToken(char c) =>
            DigitTokens.Contains(c) || c == 'L' || c == 'l' || c == 'A' || c == 'a' || c == '?';

        private static bool TokenMatches(char text, char token)
        {
            if (DigitTokens.Contains(token)) return char.IsDigit(text);
            if (token == 'L' || token == 'l') return char.IsLetter(text);
            if (token == 'A' || token == 'a') return char.IsLetterOrDigit(text);
            if (token == '?') return true;
            return false;
        }
    }
}
