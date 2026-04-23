using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VMS.VisionSetup.VisionTools.DeepLearning
{
    /// <summary>
    /// InferenceSession 생성 없이 ONNX 파일에서 metadata_props(key/value)만 꺼내는 경량 리더.
    /// 전체 모델 가중치가 들어있는 GraphProto(field 7)와 그 외 관심 없는 필드는
    /// Protobuf 태그/길이만 읽고 FileStream.Seek으로 건너뛰므로 대용량 모델에서도 수~수십 ms에 끝난다.
    ///
    /// ONNX ModelProto (subset we care about):
    ///   repeated StringStringEntryProto metadata_props = 14;  // wire_type 2
    /// StringStringEntryProto:
    ///   optional string key   = 1;  // wire_type 2
    ///   optional string value = 2;  // wire_type 2
    /// </summary>
    public static class OnnxMetadataReader
    {
        private const int ModelProtoMetadataPropsField = 14;
        private const int EntryKeyField = 1;
        private const int EntryValueField = 2;

        private const int WireVarint = 0;
        private const int WireFixed64 = 1;
        private const int WireLengthDelimited = 2;
        private const int WireFixed32 = 5;

        /// <summary>
        /// 실패하거나 metadata_props가 없으면 빈 딕셔너리를 반환한다.
        /// </summary>
        public static Dictionary<string, string> Read(string modelPath)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(modelPath)) return result;

            try
            {
                using var fs = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long fileLen = fs.Length;

                while (fs.Position < fileLen)
                {
                    if (!TryReadVarint(fs, out ulong tag)) break;
                    int fieldNumber = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x7);

                    if (fieldNumber == ModelProtoMetadataPropsField && wireType == WireLengthDelimited)
                    {
                        if (!TryReadVarint(fs, out ulong entryLen)) break;
                        long entryEnd = fs.Position + (long)entryLen;
                        if (entryEnd > fileLen) break;

                        ParseEntry(fs, entryEnd, result);
                        fs.Position = entryEnd;
                    }
                    else if (!SkipField(fs, wireType, fileLen))
                    {
                        break;
                    }
                }
            }
            catch
            {
                // 파싱 실패 시 빈 결과 반환 — 호출 측은 이미 catch로 감싸고 있다.
            }

            return result;
        }

        private static void ParseEntry(Stream s, long entryEnd, Dictionary<string, string> result)
        {
            string? key = null;
            string? value = null;

            while (s.Position < entryEnd)
            {
                if (!TryReadVarint(s, out ulong tag)) return;
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x7);

                if (wireType == WireLengthDelimited &&
                    (fieldNumber == EntryKeyField || fieldNumber == EntryValueField))
                {
                    if (!TryReadVarint(s, out ulong len)) return;
                    if (s.Position + (long)len > entryEnd) return;

                    var buf = new byte[len];
                    ReadExactly(s, buf);
                    var str = Encoding.UTF8.GetString(buf);
                    if (fieldNumber == EntryKeyField) key = str;
                    else value = str;
                }
                else if (!SkipField(s, wireType, entryEnd))
                {
                    return;
                }
            }

            if (key != null)
                result[key] = value ?? string.Empty;
        }

        private static bool SkipField(Stream s, int wireType, long boundary)
        {
            switch (wireType)
            {
                case WireVarint:
                    return TryReadVarint(s, out _);
                case WireFixed64:
                    if (s.Position + 8 > boundary) return false;
                    s.Position += 8;
                    return true;
                case WireLengthDelimited:
                    if (!TryReadVarint(s, out ulong len)) return false;
                    if (s.Position + (long)len > boundary) return false;
                    s.Position += (long)len;
                    return true;
                case WireFixed32:
                    if (s.Position + 4 > boundary) return false;
                    s.Position += 4;
                    return true;
                default:
                    return false; // SGROUP/EGROUP은 ONNX에서 쓰지 않음
            }
        }

        private static bool TryReadVarint(Stream s, out ulong value)
        {
            value = 0;
            int shift = 0;
            while (shift < 64)
            {
                int b = s.ReadByte();
                if (b < 0) return false;
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
                shift += 7;
            }
            return false;
        }

        private static void ReadExactly(Stream s, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = s.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
