using System;
using System.Text;

namespace VMS.VisionSetup.Services.SynthData
{
    /// <summary>
    /// OCR 합성 데이터용 패턴 기반 무작위 문자열 생성.
    /// OcrFormatMatcher의 토큰 규칙을 역으로 사용:
    /// D/M/Y/H/S/f = 숫자, L = 영문자, A = 영숫자, ? = 임의, 그 외 = 리터럴.
    /// 알려진 날짜/시간 패턴은 실제 유효한 값 생성 (모델이 학습 분포 외 값을 보는 것 방지).
    /// </summary>
    public static class PatternStringGenerator
    {
        private const string AsciiUpper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string AsciiAlnum = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        public static string Generate(string pattern, Random rng)
        {
            if (string.IsNullOrEmpty(pattern)) return string.Empty;

            // 알려진 날짜/시간 패턴은 실제 유효한 datetime 값으로 — 학습 분포 realism 향상
            var known = TryKnownDateTime(pattern, rng);
            if (known != null) return known;

            var sb = new StringBuilder(pattern.Length);
            foreach (var c in pattern)
            {
                if (IsDigitToken(c)) sb.Append((char)('0' + rng.Next(10)));
                else if (c == 'L' || c == 'l') sb.Append(AsciiUpper[rng.Next(AsciiUpper.Length)]);
                else if (c == 'A' || c == 'a') sb.Append(AsciiAlnum[rng.Next(AsciiAlnum.Length)]);
                else if (c == '?') sb.Append(AsciiAlnum[rng.Next(AsciiAlnum.Length)]);
                else sb.Append(c); // 리터럴
            }
            return sb.ToString();
        }

        private static bool IsDigitToken(char c) =>
            c == 'D' || c == 'd' || c == 'M' || c == 'm' || c == 'Y' || c == 'y'
            || c == 'H' || c == 'h' || c == 'S' || c == 's' || c == 'f' || c == 'F';

        private static string? TryKnownDateTime(string p, Random rng)
        {
            var d = RandomDate(rng);
            var dt = RandomDateTime(rng);
            return p switch
            {
                "DD/MM/YYYY" => d.ToString("dd/MM/yyyy"),
                "DD/MM/YY" => d.ToString("dd/MM/yy"),
                "MM/DD/YYYY" => d.ToString("MM/dd/yyyy"),
                "YYYY-MM-DD" => d.ToString("yyyy-MM-dd"),
                "YYYY/MM/DD" => d.ToString("yyyy/MM/dd"),
                "DD/MM/YYYY HH:MM" => dt.ToString("dd/MM/yyyy HH:mm"),
                "DD/MM/YYYY HH:MM:SS" => dt.ToString("dd/MM/yyyy HH:mm:ss"),
                "DD/MM/YYYY HH:MM:SS.fff" => dt.ToString("dd/MM/yyyy HH:mm:ss.fff"),
                "YYYY-MM-DDTHH:MM:SS" => dt.ToString("yyyy-MM-ddTHH:mm:ss"),
                _ => null
            };
        }

        // 유효 날짜 (모든 월의 1~28일까지만 — 윤년/짝수월 케이스 모두 안전)
        private static DateTime RandomDate(Random rng) =>
            new DateTime(2020 + rng.Next(80), 1 + rng.Next(12), 1 + rng.Next(28));

        private static DateTime RandomDateTime(Random rng) =>
            RandomDate(rng)
                .AddHours(rng.Next(24))
                .AddMinutes(rng.Next(60))
                .AddSeconds(rng.Next(60))
                .AddMilliseconds(rng.Next(1000));
    }
}
