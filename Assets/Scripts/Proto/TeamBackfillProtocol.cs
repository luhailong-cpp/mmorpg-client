using System.IO;
using Google.Protobuf;

namespace Teampb
{
    /// <summary>
    /// 保留远端补位分支的协议字段，同时使用本地 CPP/Java 恢复契约生成的 Team.cs。
    /// 补位来源：CPP origin/main fea7f5e9 的 proto/team/team.proto；恢复来源：本地 CPP/Java 契约。
    /// 两分支的请求新增字段同号、不同 wire type，必须保留原 tag，不能重新猜号或覆盖生成代码。
    /// </summary>
    public static class TeamBackfillProtocol
    {
        public const TeamMatchState QueuedState = (TeamMatchState)2;
        public const TeamChangeReason MatchQueuedReason = (TeamChangeReason)15;
        public const TeamChangeReason MatchQueueCanceledReason = (TeamChangeReason)16;
    }

    public sealed partial class GetMyTeamRequest
    {
        public const int RegroupFieldNumber = 2;
        public bool Regroup
        {
            get => TeamBackfillWireFields.ReadVarint(_unknownFields, RegroupFieldNumber) != 0;
            set => _unknownFields = TeamBackfillWireFields.ReplaceVarint(_unknownFields, RegroupFieldNumber, value ? 1ul : 0ul);
        }
    }

    public sealed partial class StartTeamMatchRequest
    {
        public const int TargetTeamSizeFieldNumber = 3;
        public uint TargetTeamSize
        {
            get => unchecked((uint)TeamBackfillWireFields.ReadVarint(_unknownFields, TargetTeamSizeFieldNumber));
            set => _unknownFields = TeamBackfillWireFields.ReplaceVarint(_unknownFields, TargetTeamSizeFieldNumber, value);
        }
    }

    public sealed partial class TeamView
    {
        public const int MatchTargetSizeFieldNumber = 13;
        public uint MatchTargetSize
        {
            get => unchecked((uint)TeamBackfillWireFields.ReadVarint(_unknownFields, MatchTargetSizeFieldNumber));
            set => _unknownFields = TeamBackfillWireFields.ReplaceVarint(_unknownFields, MatchTargetSizeFieldNumber, value);
        }

        public const int MatchQueuedAtMsFieldNumber = 14;
        public ulong MatchQueuedAtMs
        {
            get => TeamBackfillWireFields.ReadVarint(_unknownFields, MatchQueuedAtMsFieldNumber);
            set => _unknownFields = TeamBackfillWireFields.ReplaceVarint(_unknownFields, MatchQueuedAtMsFieldNumber, value);
        }

        public const int MatchBattleConfigIdFieldNumber = 15;
        public uint MatchBattleConfigId
        {
            get => unchecked((uint)TeamBackfillWireFields.ReadVarint(_unknownFields, MatchBattleConfigIdFieldNumber));
            set => _unknownFields = TeamBackfillWireFields.ReplaceVarint(_unknownFields, MatchBattleConfigIdFieldNumber, value);
        }

        public const int MatchFillSupportedFieldNumber = 16;
        public bool MatchFillSupported
        {
            get => TeamBackfillWireFields.ReadVarint(_unknownFields, MatchFillSupportedFieldNumber) != 0;
            set => _unknownFields = TeamBackfillWireFields.ReplaceVarint(_unknownFields, MatchFillSupportedFieldNumber, value ? 1ul : 0ul);
        }
    }

    internal static class TeamBackfillWireFields
    {
        private static byte[] Encode(UnknownFieldSet fields)
        {
            if (fields == null) return System.Array.Empty<byte>();
            var bytes = new byte[fields.CalculateSize()];
            using (var output = new CodedOutputStream(bytes)) fields.WriteTo(output);
            return bytes;
        }

        internal static ulong ReadVarint(UnknownFieldSet fields, int number)
        {
            uint expectedTag = WireFormat.MakeTag(number, WireFormat.WireType.Varint);
            ulong value = 0;
            using (var input = new CodedInputStream(Encode(fields)))
            {
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    if (tag == expectedTag) value = input.ReadUInt64();
                    else input.SkipLastField();
                }
            }
            return value;
        }

        internal static UnknownFieldSet ReplaceVarint(UnknownFieldSet fields, int number, ulong value)
        {
            uint expectedTag = WireFormat.MakeTag(number, WireFormat.WireType.Varint);
            UnknownFieldSet result = null;
            using (var input = new CodedInputStream(Encode(fields)))
            {
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    // 只替换该字段的 varint；同号其他 wire type 与所有其他未知字段完整保留。
                    if (tag == expectedTag) input.ReadUInt64();
                    else result = UnknownFieldSet.MergeFieldFrom(result, input);
                }
            }
            if (value == 0) return result;
            using (var buffer = new MemoryStream())
            {
                using (var output = new CodedOutputStream(buffer, true))
                {
                    output.WriteTag(number, WireFormat.WireType.Varint);
                    output.WriteUInt64(value);
                    output.Flush();
                }
                using (var input = new CodedInputStream(buffer.ToArray()))
                {
                    input.ReadTag();
                    return UnknownFieldSet.MergeFieldFrom(result, input);
                }
            }
        }
    }
}
