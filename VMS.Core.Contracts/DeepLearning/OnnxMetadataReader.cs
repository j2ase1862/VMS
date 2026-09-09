using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VMS.Core.DeepLearning
{
    /// <summary>
    /// InferenceSession 생성 없이 ONNX 파일에서 metadata_props(key/value)와 그래프 입출력 이름만 꺼내는 경량 리더.
    /// OpenCV·ONNX Runtime 의존이 없어 VisionSetup·DeepLearning 앱·Web 모델 레지스트리·학습 워커가 공유한다 (VMS.Core 로 승격, 2026-09-08).
    /// 전체 모델 가중치가 들어있는 GraphProto(field 7)와 그 외 관심 없는 필드는
    /// Protobuf 태그/길이만 읽고 FileStream.Seek으로 건너뛰므로 대용량 모델에서도 수~수십 ms에 끝난다.
    ///
    /// ONNX ModelProto (subset we care about):
    ///   repeated StringStringEntryProto metadata_props = 14;  // wire_type 2
    /// StringStringEntryProto:
    ///   optional string key   = 1;  // wire_type 2
    ///   optional string value = 2;  // wire_type 2
    ///
    /// <b>입력은 신뢰하지 않는다.</b> VisionSetup 은 현장에서 임의 경로의 ONNX 를 열고, 레지스트리 캐시 파일도
    /// 네트워크를 거쳐 온다. 모든 길이 필드는 <see cref="TryReadLength"/> 로 읽어 남은 바이트 수와
    /// <c>ulong</c> 상태로 대조한다 — varint 는 2^64-1 까지 가능해 <c>(long)</c> 로 먼저 캐스팅하면 음수가 되고,
    /// 그 값으로 만든 끝 오프셋을 <c>Stream.Position</c> 에 대입하면 스트림이 뒤로 감겨 파싱이 영원히 반복된다
    /// (15바이트짜리 파일 하나로 스레드를 영구 점유하는 DoS). MLOps 서버의 OnnxSafeReader 와 같은 규칙이다.
    /// </summary>
    public static class OnnxMetadataReader
    {
        /// <summary>
        /// metadata 문자열·텐서 이름 한 개의 최대 바이트. 길이가 파일 안에 들어맞아도 수 GB 모델 파일에서
        /// 조작된 길이로 거대한 배열을 할당하지 않도록 상한을 둔다. 정상 모델의 names JSON 은 수 KB 수준이다.
        /// </summary>
        private const int MaxStringBytes = 16 * 1024 * 1024;

        private const int ModelProtoMetadataPropsField = 14;
        private const int EntryKeyField = 1;
        private const int EntryValueField = 2;

        // GraphProto (ModelProto field 7) — 입력/출력 ValueInfoProto 의 name 만 읽는다
        private const int ModelProtoGraphField = 7;
        private const int GraphInputField = 11;
        private const int GraphOutputField = 12;
        private const int ValueInfoNameField = 1;

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
                        if (!TryReadLength(fs, fileLen, out long entryLen)) break;
                        long entryEnd = fs.Position + entryLen;

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

        /// <summary>
        /// InferenceSession 없이 그래프 입력/출력 텐서 이름을 읽는다 — 검출 모델 규약(YOLO / D-FINE) 판별용.
        /// GraphProto 안의 initializer(가중치)·node 는 길이만 읽고 Seek 으로 건너뛰므로 대용량 모델에서도 빠르다.
        /// 실패하면 빈 목록 (호출 측은 YOLO 로 폴백).
        /// </summary>
        public static (List<string> Inputs, List<string> Outputs) ReadGraphIoNames(string modelPath)
        {
            var inputs = new List<string>();
            var outputs = new List<string>();
            if (!File.Exists(modelPath)) return (inputs, outputs);

            try
            {
                using var fs = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long fileLen = fs.Length;

                while (fs.Position < fileLen)
                {
                    if (!TryReadVarint(fs, out ulong tag)) break;
                    int fieldNumber = (int)(tag >> 3);
                    int wireType = (int)(tag & 0x7);

                    if (fieldNumber == ModelProtoGraphField && wireType == WireLengthDelimited)
                    {
                        if (!TryReadLength(fs, fileLen, out long graphLen)) break;
                        long graphEnd = fs.Position + graphLen;

                        ParseGraphIo(fs, graphEnd, inputs, outputs);
                        fs.Position = graphEnd;
                        break; // ModelProto 에 graph 는 하나
                    }
                    else if (!SkipField(fs, wireType, fileLen))
                    {
                        break;
                    }
                }
            }
            catch
            {
                // 파싱 실패 시 지금까지 읽은 것만 반환
            }

            return (inputs, outputs);
        }

        private static void ParseGraphIo(Stream s, long graphEnd, List<string> inputs, List<string> outputs)
        {
            while (s.Position < graphEnd)
            {
                if (!TryReadVarint(s, out ulong tag)) return;
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x7);

                if (wireType == WireLengthDelimited &&
                    (fieldNumber == GraphInputField || fieldNumber == GraphOutputField))
                {
                    if (!TryReadLength(s, graphEnd, out long len)) return;
                    long valueEnd = s.Position + len;

                    var name = ReadValueInfoName(s, valueEnd);
                    if (name != null)
                        (fieldNumber == GraphInputField ? inputs : outputs).Add(name);
                    s.Position = valueEnd;
                }
                else if (!SkipField(s, wireType, graphEnd))
                {
                    return;
                }
            }
        }

        private static string? ReadValueInfoName(Stream s, long valueEnd)
        {
            while (s.Position < valueEnd)
            {
                if (!TryReadVarint(s, out ulong tag)) return null;
                int fieldNumber = (int)(tag >> 3);
                int wireType = (int)(tag & 0x7);

                if (fieldNumber == ValueInfoNameField && wireType == WireLengthDelimited)
                {
                    if (!TryReadLength(s, valueEnd, out long len)) return null;
                    if (len > MaxStringBytes) return null;
                    var buf = new byte[len];
                    ReadExactly(s, buf);
                    return Encoding.UTF8.GetString(buf);
                }
                if (!SkipField(s, wireType, valueEnd)) return null;
            }
            return null;
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
                    if (!TryReadLength(s, entryEnd, out long len)) return;
                    if (len > MaxStringBytes) return;

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
                    if (!TryReadLength(s, boundary, out long len)) return false;
                    s.Position += len;
                    return true;
                case WireFixed32:
                    if (s.Position + 4 > boundary) return false;
                    s.Position += 4;
                    return true;
                default:
                    return false; // SGROUP/EGROUP은 ONNX에서 쓰지 않음
            }
        }

        /// <summary>
        /// 길이 필드를 읽되 <paramref name="boundary"/> 까지 남은 바이트 수를 넘으면 실패로 처리한다.
        /// 비교를 <c>ulong</c> 상태로 하므로 2^63 이상 값이 음수 long 으로 바뀌어 스트림을 되감는 일이 없다.
        /// 성공하면 <paramref name="length"/> 는 항상 0 이상이고 <c>Position + length &lt;= boundary</c> 다.
        /// </summary>
        private static bool TryReadLength(Stream s, long boundary, out long length)
        {
            length = 0;
            if (!TryReadVarint(s, out ulong raw)) return false;
            long remaining = boundary - s.Position;
            if (remaining < 0 || raw > (ulong)remaining) return false;
            length = (long)raw;
            return true;
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
