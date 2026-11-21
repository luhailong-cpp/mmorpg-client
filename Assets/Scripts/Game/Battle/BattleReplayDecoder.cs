using System;
using System.IO;
using System.IO.Compression;
using Google.Protobuf;

namespace MmorpgClient.Game.Battle
{
    public enum BattleReplayDecodeError : byte
    {
        None = 0,
        EmptyEnvelope,
        UnsupportedFormatVersion,
        UnsupportedCodec,
        TooLarge,
        SizeMismatch,
        CorruptData,
        InvalidReplay,
    }

    public readonly struct BattleReplayDecodeResult
    {
        public bool Success => Error == BattleReplayDecodeError.None && Replay != null;
        public BattleReplay Replay { get; }
        public BattleReplayDecodeError Error { get; }
        public string Message { get; }

        private BattleReplayDecodeResult(BattleReplay replay, BattleReplayDecodeError error, string message)
        {
            Replay = replay;
            Error = error;
            Message = message ?? string.Empty;
        }

        public static BattleReplayDecodeResult Ok(BattleReplay replay)
            => new BattleReplayDecodeResult(replay, BattleReplayDecodeError.None, string.Empty);

        public static BattleReplayDecodeResult Fail(BattleReplayDecodeError error, string message)
            => new BattleReplayDecodeResult(null, error, message);
    }

    /// <summary>
    /// 战报回放外壳解码(docs/handoff/battle-report-replay-client.md §4):
    ///   - 校验 format_version == 1;
    ///   - 校验 raw_bytes / payload 上限(BR-5: 原始 1 MiB, gzip 512 KiB);
    ///   - 支持 GZIP(RFC 1952,流式读取并设上限防解压炸弹)与 NONE(未压缩兜底);
    ///   - 校验解压后长度严格等于 raw_bytes,再反序列化 BattleReplay 并确认含 start_state。
    /// 纯 C#、单线程同步内存流,不依赖 Unity 运行时与后台线程(兼容 WebGL / 小游戏)。
    /// </summary>
    public static class BattleReplayDecoder
    {
        public const uint SupportedFormatVersion = 1;
        public const int MaxRawBytes = 1 << 20;      // 1 MiB
        public const int MaxPayloadBytes = 512 << 10; // 512 KiB

        public const string UpgradeRequiredMessage = "请更新客户端后查看";
        public const string UnsupportedCodecMessage = "当前客户端不支持该回放编码，无法播放";
        public const string CorruptReplayMessage = "回放数据损坏";

        public static BattleReplayDecodeResult Decode(BattleReplayEnvelope envelope)
        {
            if (envelope == null)
                return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.EmptyEnvelope, CorruptReplayMessage);

            if (envelope.FormatVersion != SupportedFormatVersion)
                return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.UnsupportedFormatVersion, UpgradeRequiredMessage);

            uint rawBytes = envelope.RawBytes;
            if (rawBytes == 0)
                return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.CorruptData, CorruptReplayMessage);

            if (rawBytes > MaxRawBytes)
                return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.TooLarge, CorruptReplayMessage);

            ByteString payload = envelope.Payload;
            if (payload == null || payload.Length == 0)
                return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.CorruptData, CorruptReplayMessage);

            byte[] raw;
            switch (envelope.Codec)
            {
                case eBattleReplayCodec.BattleReplayCodecNone:
                {
                    if (payload.Length > MaxRawBytes)
                        return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.TooLarge, CorruptReplayMessage);
                    if (payload.Length != (int)rawBytes)
                        return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.SizeMismatch, CorruptReplayMessage);
                    raw = payload.ToByteArray();
                    break;
                }
                case eBattleReplayCodec.BattleReplayCodecGzip:
                {
                    if (payload.Length > MaxPayloadBytes)
                        return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.TooLarge, CorruptReplayMessage);
                    var decompressed = TryDecompressGzip(payload.ToByteArray(), (int)rawBytes, out raw);
                    if (decompressed != BattleReplayDecodeError.None)
                        return BattleReplayDecodeResult.Fail(decompressed, CorruptReplayMessage);
                    break;
                }
                default:
                    return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.UnsupportedCodec, UnsupportedCodecMessage);
            }

            BattleReplay replay;
            try
            {
                replay = BattleReplay.Parser.ParseFrom(raw);
            }
            catch (InvalidProtocolBufferException)
            {
                return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.CorruptData, CorruptReplayMessage);
            }

            if (replay.StartState == null)
                return BattleReplayDecodeResult.Fail(BattleReplayDecodeError.InvalidReplay, CorruptReplayMessage);

            return BattleReplayDecodeResult.Ok(replay);
        }

        private static BattleReplayDecodeError TryDecompressGzip(byte[] compressed, int expectedBytes, out byte[] raw)
        {
            raw = null;
            // RFC 1952: 至少 10 字节头(魔数 1f 8b + deflate 08) + 8 字节尾(CRC32 + ISIZE)。
            if (compressed.Length < 18 || compressed[0] != 0x1f || compressed[1] != 0x8b || compressed[2] != 0x08)
                return BattleReplayDecodeError.CorruptData;

            uint trailerCrc = ReadUInt32LittleEndian(compressed, compressed.Length - 8);
            uint trailerIsize = ReadUInt32LittleEndian(compressed, compressed.Length - 4);

            // 至多读 expectedBytes + 1 字节:防解压炸弹,同时能区分「超 1 MiB」与「与 raw_bytes 不符」。
            int maxAllowed = Math.Min(expectedBytes, MaxRawBytes);
            try
            {
                using var input = new MemoryStream(compressed, writable: false);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream(Math.Min(maxAllowed, 65536));
                var buffer = new byte[16384];
                int total = 0;
                while (true)
                {
                    int remaining = maxAllowed - total + 1;
                    int toRead = Math.Min(buffer.Length, remaining);
                    int read = gzip.Read(buffer, 0, toRead);
                    if (read == 0) break;
                    total += read;
                    if (total > maxAllowed)
                    {
                        return maxAllowed >= MaxRawBytes
                            ? BattleReplayDecodeError.TooLarge
                            : BattleReplayDecodeError.SizeMismatch;
                    }
                    output.Write(buffer, 0, read);
                }
                // .NET GZipStream 对截断尾部与流尾多余字节不会抛异常:显式核对输入消费完 + RFC 1952 CRC32/ISIZE。
                if (input.Position != compressed.Length)
                    return BattleReplayDecodeError.CorruptData;

                byte[] bytes = output.ToArray();
                if (trailerIsize != (uint)bytes.Length || trailerCrc != ComputeCrc32(bytes))
                    return BattleReplayDecodeError.CorruptData;

                if (bytes.Length != expectedBytes)
                    return BattleReplayDecodeError.SizeMismatch;

                raw = bytes;
                return BattleReplayDecodeError.None;
            }
            catch (InvalidDataException)
            {
                return BattleReplayDecodeError.CorruptData;
            }
            catch (EndOfStreamException)
            {
                return BattleReplayDecodeError.CorruptData;
            }
            catch (IOException)
            {
                return BattleReplayDecodeError.CorruptData;
            }
        }

        private static uint ReadUInt32LittleEndian(byte[] data, int offset)
            => (uint)(data[offset]
                      | (data[offset + 1] << 8)
                      | (data[offset + 2] << 16)
                      | (data[offset + 3] << 24));

        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int bit = 0; bit < 8; bit++)
                    c = (c & 1u) != 0 ? 0xEDB88320u ^ (c >> 1) : (c >> 1);
                table[i] = c;
            }
            return table;
        }

        private static uint ComputeCrc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                crc = Crc32Table[(crc ^ data[i]) & 0xFFu] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
