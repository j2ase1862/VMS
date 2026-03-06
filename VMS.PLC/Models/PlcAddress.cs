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
        /// </summary>
        public static PlcAddress Parse(string rawAddress, PlcVendor vendor)
        {
            return vendor switch
            {
                PlcVendor.Mitsubishi => ParseMitsubishi(rawAddress),
                PlcVendor.Siemens => ParseSiemens(rawAddress),
                PlcVendor.LS => ParseLsXgt(rawAddress),
                PlcVendor.Omron => ParseOmron(rawAddress),
                _ => ParseGeneric(rawAddress)
            };
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
