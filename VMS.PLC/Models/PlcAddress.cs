using System.Text.RegularExpressions;

namespace VMS.PLC.Models
{
    /// <summary>
    /// Vendor-agnostic PLC address abstraction.
    /// Parses vendor-specific address strings into a normalized form.
    /// </summary>
    public class PlcAddress
    {
        public string RawAddress { get; set; } = string.Empty;
        public string DeviceCode { get; set; } = string.Empty;
        public int Offset { get; set; }
        public int BitPosition { get; set; } = -1;
        public int DbNumber { get; set; } = -1;

        public bool IsBitAddress => BitPosition >= 0;

        public string ToKey() => $"{DeviceCode}:{DbNumber}:{Offset}:{BitPosition}";

        /// <summary>
        /// Parse a raw address string into a PlcAddress based on vendor format.
        /// 파싱 결과의 Offset 범위를 벤더별 합리적 상한으로 검증하여
        /// 비정상적으로 큰 Offset (음수 / 메모리 폭탄 / 오버플로우) 을 차단.
        /// </summary>
        public static PlcAddress Parse(string rawAddress, PlcVendor vendor)
        {
            // raw 입력 길이 상한 — 비현실적 PLC 주소 문자열 즉시 거부 (DoS 방어).
            const int MaxRawLength = 64;
            if (string.IsNullOrWhiteSpace(rawAddress))
                throw new System.ArgumentException("PLC 주소가 비어 있습니다.", nameof(rawAddress));
            if (rawAddress.Length > MaxRawLength)
                throw new System.ArgumentException(
                    $"PLC 주소가 너무 깁니다 (최대 {MaxRawLength}자): '{rawAddress}'", nameof(rawAddress));

            var result = vendor switch
            {
                PlcVendor.Mitsubishi => ParseMitsubishi(rawAddress),
                PlcVendor.Siemens => ParseSiemens(rawAddress),
                PlcVendor.LS => ParseLsXgt(rawAddress),
                PlcVendor.Omron => ParseOmron(rawAddress),
                PlcVendor.Modbus => ParseModbus(rawAddress),
                _ => ParseGeneric(rawAddress)
            };
            EnsureOffsetInRange(result, vendor);
            return result;
        }

        /// <summary>
        /// PLC 주소 Offset 범위 검증 — 음수 / 비현실적 메모리 인덱스 차단.
        /// 산업 PLC 의 공통 상한 기준이며, 모델별 정확한 한계는 더 좁힐 수 있음.
        /// </summary>
        private static void EnsureOffsetInRange(PlcAddress addr, PlcVendor vendor)
        {
            if (addr.Offset < 0)
                throw new System.ArgumentException(
                    $"PLC Offset 이 음수입니다 ({addr.Offset}). 주소='{addr.RawAddress}', 벤더={vendor}");

            int max = vendor switch
            {
                PlcVendor.Modbus => 0xFFFF,        // 16-bit register address space (65535)
                PlcVendor.Siemens => 0x1FFFFF,     // 약 2M (대형 DB 한계)
                PlcVendor.Mitsubishi => 0xFFFFFF,  // 약 16M (Q series 최대)
                PlcVendor.LS => 0xFFFFFF,
                PlcVendor.Omron => 0xFFFFFF,
                _ => int.MaxValue / 2              // 알 수 없는 벤더 — 보수적 상한
            };
            if (addr.Offset > max)
                throw new System.ArgumentException(
                    $"PLC Offset {addr.Offset} 가 벤더 {vendor} 의 허용 범위 [0, {max}] 를 벗어났습니다. 주소='{addr.RawAddress}'");
        }

        /// <summary>
        /// Modbus format:
        ///   0xNNNN or 00001..09999  → Coil (FC1/FC5/FC15)
        ///   1xNNNN or 10001..19999  → Discrete Input (FC2, read-only)
        ///   3xNNNN or 30001..39999  → Input Register (FC4, read-only)
        ///   4xNNNN or 40001..49999  → Holding Register (FC3/FC6/FC16)
        ///   4xNNNN.B                → Holding Register bit (B = 0..15)
        /// DeviceCode is normalized to "0x"/"1x"/"3x"/"4x". Offset is 0-based.
        /// </summary>
        private static PlcAddress ParseModbus(string raw)
        {
            var trimmed = raw.Trim().ToLowerInvariant();

            // {prefix}{offset}[.{bit}] — prefix = 0x / 1x / 3x / 4x
            var prefixMatch = Regex.Match(trimmed, @"^([0134])x(\d+)(?:\.(\d+))?$");
            if (prefixMatch.Success)
            {
                var prefix = prefixMatch.Groups[1].Value + "x";
                var offset = int.Parse(prefixMatch.Groups[2].Value);
                var bit = prefixMatch.Groups[3].Success ? int.Parse(prefixMatch.Groups[3].Value) : -1;
                return new PlcAddress
                {
                    RawAddress = raw,
                    DeviceCode = prefix,
                    Offset = offset,
                    BitPosition = bit
                };
            }

            // Modicon-style 5/6-digit: first digit = area, 나머지 = 1-based offset
            //   5-digit: 0xxxx / 1xxxx / 3xxxx / 4xxxx  (4-digit rest)
            //   6-digit: 0xxxxx / 1xxxxx / 3xxxxx / 4xxxxx  (5-digit rest)
            var modiconMatch = Regex.Match(trimmed, @"^(\d)(\d{4,5})$");
            if (modiconMatch.Success)
            {
                var first = int.Parse(modiconMatch.Groups[1].Value);
                var rest = int.Parse(modiconMatch.Groups[2].Value);
                string prefix = first switch
                {
                    4 => "4x",
                    3 => "3x",
                    1 => "1x",
                    _ => "0x"
                };
                int offset = rest - 1;
                if (offset < 0) offset = 0;
                return new PlcAddress
                {
                    RawAddress = raw,
                    DeviceCode = prefix,
                    Offset = offset,
                    BitPosition = -1
                };
            }

            throw new FormatException($"Invalid Modbus address format: {raw} (expected 0x100, 4x100, 4x100.3, 40101 etc.)");
        }

        /// <summary>
        /// Mitsubishi format: D100, M0, X0, Y0, R100, W100
        /// </summary>
        private static PlcAddress ParseMitsubishi(string raw)
        {
            var match = Regex.Match(raw.Trim().ToUpper(), @"^([A-Z]+)(\d+)$");
            if (!match.Success)
                throw new FormatException($"Invalid Mitsubishi address format: {raw}");

            var device = match.Groups[1].Value;
            var offset = int.Parse(match.Groups[2].Value);

            // M, X, Y are bit devices
            bool isBit = device is "M" or "X" or "Y";

            return new PlcAddress
            {
                RawAddress = raw,
                DeviceCode = device,
                Offset = isBit ? offset / 16 : offset,
                BitPosition = isBit ? offset % 16 : -1
            };
        }

        /// <summary>
        /// Siemens format: DB1.DBW0, DB1.DBX0.3, MW100, M0.5
        /// </summary>
        private static PlcAddress ParseSiemens(string raw)
        {
            var trimmed = raw.Trim().ToUpper();

            // DB access: DB1.DBW0 or DB1.DBX0.3
            var dbMatch = Regex.Match(trimmed, @"^DB(\d+)\.DB([XBWD])(\d+)(?:\.(\d+))?$");
            if (dbMatch.Success)
            {
                var dbNum = int.Parse(dbMatch.Groups[1].Value);
                var type = dbMatch.Groups[2].Value;
                var offset = int.Parse(dbMatch.Groups[3].Value);
                var bitPos = dbMatch.Groups[4].Success ? int.Parse(dbMatch.Groups[4].Value) : -1;

                return new PlcAddress
                {
                    RawAddress = raw,
                    DeviceCode = "DB" + type,
                    DbNumber = dbNum,
                    Offset = offset,
                    BitPosition = type == "X" ? bitPos : -1
                };
            }

            // Direct access: MW100, M0.5, IW0, QW0
            var directMatch = Regex.Match(trimmed, @"^([MIQ])([WBD]?)(\d+)(?:\.(\d+))?$");
            if (directMatch.Success)
            {
                var area = directMatch.Groups[1].Value;
                var size = directMatch.Groups[2].Value;
                var offset = int.Parse(directMatch.Groups[3].Value);
                var bitPos = directMatch.Groups[4].Success ? int.Parse(directMatch.Groups[4].Value) : -1;

                return new PlcAddress
                {
                    RawAddress = raw,
                    DeviceCode = area + size,
                    Offset = offset,
                    BitPosition = string.IsNullOrEmpty(size) ? bitPos : -1
                };
            }

            throw new FormatException($"Invalid Siemens address format: {raw}");
        }

        /// <summary>
        /// LS XGT format: %DW100, %MX0, %MW0
        /// </summary>
        private static PlcAddress ParseLsXgt(string raw)
        {
            var match = Regex.Match(raw.Trim().ToUpper(), @"^%?([A-Z])([XBWD])(\d+)$");
            if (!match.Success)
                throw new FormatException($"Invalid LS XGT address format: {raw}");

            var area = match.Groups[1].Value;
            var size = match.Groups[2].Value;
            var offset = int.Parse(match.Groups[3].Value);

            return new PlcAddress
            {
                RawAddress = raw,
                DeviceCode = area,
                Offset = size == "X" ? offset / 16 : offset,
                BitPosition = size == "X" ? offset % 16 : -1
            };
        }

        /// <summary>
        /// Omron format: D100, W0.00, CIO0.00, HR100
        /// </summary>
        private static PlcAddress ParseOmron(string raw)
        {
            var trimmed = raw.Trim().ToUpper();

            // Word.Bit format: W0.00, CIO0.15
            var bitMatch = Regex.Match(trimmed, @"^([A-Z]+)(\d+)\.(\d+)$");
            if (bitMatch.Success)
            {
                return new PlcAddress
                {
                    RawAddress = raw,
                    DeviceCode = bitMatch.Groups[1].Value,
                    Offset = int.Parse(bitMatch.Groups[2].Value),
                    BitPosition = int.Parse(bitMatch.Groups[3].Value)
                };
            }

            // Word only: D100, W0, HR100
            var wordMatch = Regex.Match(trimmed, @"^([A-Z]+)(\d+)$");
            if (wordMatch.Success)
            {
                return new PlcAddress
                {
                    RawAddress = raw,
                    DeviceCode = wordMatch.Groups[1].Value,
                    Offset = int.Parse(wordMatch.Groups[2].Value),
                    BitPosition = -1
                };
            }

            throw new FormatException($"Invalid Omron address format: {raw}");
        }

        /// <summary>
        /// Generic/Simulated format: same as Mitsubishi
        /// </summary>
        private static PlcAddress ParseGeneric(string raw)
        {
            var match = Regex.Match(raw.Trim().ToUpper(), @"^([A-Z]+)(\d+)$");
            if (match.Success)
            {
                return new PlcAddress
                {
                    RawAddress = raw,
                    DeviceCode = match.Groups[1].Value,
                    Offset = int.Parse(match.Groups[2].Value),
                    BitPosition = -1
                };
            }

            // Fallback: treat entire string as key
            return new PlcAddress
            {
                RawAddress = raw,
                DeviceCode = raw,
                Offset = 0,
                BitPosition = -1
            };
        }

        public override string ToString() => RawAddress;
    }
}
