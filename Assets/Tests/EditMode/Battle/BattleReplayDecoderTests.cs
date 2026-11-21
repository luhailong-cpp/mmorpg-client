using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Google.Protobuf;
using NUnit.Framework;
using MmorpgClient.Game.Battle;

namespace MmorpgClient.Tests.EditMode.Battle
{
    /// <summary>
    /// 战报回放解码器单测(docs/handoff/battle-report-replay-client.md §4、§7):
    /// 覆盖 gzip 正常、codec=NONE、未知 codec、format_version=2、解压长度与 raw_bytes 不符、超 1 MiB(含解压炸弹)、payload 损坏与缺 start_state。
    /// </summary>
    public sealed class BattleReplayDecoderTests
    {
        private static BattleReplay SampleReplay(ulong battleId = 9001, int rounds = 2)
        {
            var replay = new BattleReplay
            {
                BattleId = battleId,
                MatchMode = (uint)Match.MatchMode._1V1,
                BattleConfigId = 1,
                StartedAtMs = 1700000000000UL,
                FinishedAtMs = 1700000012000UL,
                Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                EndReason = eBattleReplayEndReason.BattleReplayEndFinished,
                TotalRounds = (uint)rounds,
                TableFingerprint = "fp-v1",
                StartState = new BattleStateS2C { BattleId = battleId, RoundIndex = 1 },
            };
            replay.StartState.Actors.Add(new BattleActorState { ActorId = 11, TeamIndex = 0, Name = "甲", MaxHealth = 100 });
            replay.StartState.Actors.Add(new BattleActorState { ActorId = 22, TeamIndex = 1, Name = "乙", MaxHealth = 100 });
            for (int i = 1; i <= rounds; i++)
            {
                replay.Rounds.Add(new TurnResultS2C
                {
                    BattleId = battleId,
                    RoundIndex = (uint)i,
                    State = new BattleStateS2C { BattleId = battleId, RoundIndex = (uint)i },
                });
            }
            return replay;
        }

        private static byte[] Gzip(byte[] raw)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                gz.Write(raw, 0, raw.Length);
            }
            return ms.ToArray();
        }

        [Test]
        public void GzipEnvelope_RoundTripsEveryField()
        {
            var expected = SampleReplay();
            byte[] raw = expected.ToByteArray();
            var env = new BattleReplayEnvelope
            {
                FormatVersion = BattleReplayDecoder.SupportedFormatVersion,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(Gzip(raw)),
            };

            var res = BattleReplayDecoder.Decode(env);
            Assert.IsTrue(res.Success, res.Message);
            Assert.AreEqual(expected.BattleId, res.Replay.BattleId);
            Assert.AreEqual(expected.Rounds.Count, res.Replay.Rounds.Count);
            Assert.AreEqual(expected.TableFingerprint, res.Replay.TableFingerprint);
            Assert.AreEqual(expected.Outcome, res.Replay.Outcome);
        }

        [Test]
        public void CodecNone_RoundTripsDirectly()
        {
            var expected = SampleReplay(9002, rounds: 1);
            byte[] raw = expected.ToByteArray();
            var env = new BattleReplayEnvelope
            {
                FormatVersion = BattleReplayDecoder.SupportedFormatVersion,
                Codec = eBattleReplayCodec.BattleReplayCodecNone,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(raw),
            };

            var res = BattleReplayDecoder.Decode(env);
            Assert.IsTrue(res.Success, res.Message);
            Assert.AreEqual(9002UL, res.Replay.BattleId);
            Assert.AreEqual(1, res.Replay.Rounds.Count);
        }

        [Test]
        public void UnknownCodec_IsRejected()
        {
            byte[] raw = SampleReplay().ToByteArray();
            var env = new BattleReplayEnvelope
            {
                FormatVersion = BattleReplayDecoder.SupportedFormatVersion,
                Codec = (eBattleReplayCodec)99,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(raw),
            };

            var res = BattleReplayDecoder.Decode(env);
            Assert.IsFalse(res.Success);
            Assert.AreEqual(BattleReplayDecodeError.UnsupportedCodec, res.Error);
            Assert.AreEqual(BattleReplayDecoder.UnsupportedCodecMessage, res.Message);
        }

        [Test]
        public void UnsupportedFormatVersion_PromptsUpgrade()
        {
            byte[] raw = SampleReplay().ToByteArray();
            var env = new BattleReplayEnvelope
            {
                FormatVersion = 2,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(Gzip(raw)),
            };

            var res = BattleReplayDecoder.Decode(env);
            Assert.IsFalse(res.Success);
            Assert.AreEqual(BattleReplayDecodeError.UnsupportedFormatVersion, res.Error);
            Assert.AreEqual(BattleReplayDecoder.UpgradeRequiredMessage, res.Message);
        }

        [Test]
        public void DecodedLengthMismatch_BothShorterAndLonger_IsRejected()
        {
            byte[] raw = SampleReplay().ToByteArray();
            byte[] gz = Gzip(raw);

            var declaredTooSmall = new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)(raw.Length - 1),
                Payload = ByteString.CopyFrom(gz),
            };
            var resSmall = BattleReplayDecoder.Decode(declaredTooSmall);
            Assert.IsFalse(resSmall.Success);
            Assert.AreEqual(BattleReplayDecodeError.SizeMismatch, resSmall.Error);

            var declaredTooLarge = new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)(raw.Length + 1),
                Payload = ByteString.CopyFrom(gz),
            };
            var resLarge = BattleReplayDecoder.Decode(declaredTooLarge);
            Assert.IsFalse(resLarge.Success);
            Assert.AreEqual(BattleReplayDecodeError.SizeMismatch, resLarge.Error);
        }

        [Test]
        public void OverOneMiB_RawDeclaredOrGzipBomb_IsRejected()
        {
            var declaredOver = new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)(BattleReplayDecoder.MaxRawBytes + 1),
                Payload = ByteString.CopyFrom(Gzip(new byte[16])),
            };
            var resDeclared = BattleReplayDecoder.Decode(declaredOver);
            Assert.IsFalse(resDeclared.Success);
            Assert.AreEqual(BattleReplayDecodeError.TooLarge, resDeclared.Error);

            // 高压缩比零填充炸弹:压缩后很小,解压超过 1 MiB 立即截停并判超限
            byte[] bombRaw = new byte[BattleReplayDecoder.MaxRawBytes + 4096];
            byte[] bombGz = Gzip(bombRaw);
            Assert.Less(bombGz.Length, BattleReplayDecoder.MaxPayloadBytes);
            var bombEnv = new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)BattleReplayDecoder.MaxRawBytes,
                Payload = ByteString.CopyFrom(bombGz),
            };
            var resBomb = BattleReplayDecoder.Decode(bombEnv);
            Assert.IsFalse(resBomb.Success);
            Assert.AreEqual(BattleReplayDecodeError.TooLarge, resBomb.Error);
        }

        [Test]
        public void CorruptOrTruncatedPayload_AndNullEnvelope_AreRejected()
        {
            Assert.AreEqual(BattleReplayDecodeError.EmptyEnvelope, BattleReplayDecoder.Decode(null).Error);

            byte[] raw = SampleReplay().ToByteArray();
            byte[] gz = Gzip(raw);

            // 截断的 gzip(截半与少最后一个尾部字节)
            byte[] truncated = new byte[gz.Length / 2];
            Buffer.BlockCopy(gz, 0, truncated, 0, truncated.Length);
            var resTrunc = BattleReplayDecoder.Decode(new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(truncated),
            });
            Assert.IsFalse(resTrunc.Success);
            Assert.AreEqual(BattleReplayDecodeError.CorruptData, resTrunc.Error);

            byte[] missingLastByte = new byte[gz.Length - 1];
            Buffer.BlockCopy(gz, 0, missingLastByte, 0, missingLastByte.Length);
            var resMissingTail = BattleReplayDecoder.Decode(new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(missingLastByte),
            });
            Assert.IsFalse(resMissingTail.Success);
            Assert.AreEqual(BattleReplayDecodeError.CorruptData, resMissingTail.Error);

            // gzip 流尾夹带多余字节
            byte[] trailing = new byte[gz.Length + 1];
            Buffer.BlockCopy(gz, 0, trailing, 0, gz.Length);
            trailing[gz.Length] = 0x78;
            var resTrailing = BattleReplayDecoder.Decode(new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(trailing),
            });
            Assert.IsFalse(resTrailing.Success);
            Assert.AreEqual(BattleReplayDecodeError.CorruptData, resTrailing.Error);

            // 非 gzip 字节
            var resNotGz = BattleReplayDecoder.Decode(new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = 4,
                Payload = ByteString.CopyFrom(new byte[] { 1, 2, 3, 4 }),
            });
            Assert.IsFalse(resNotGz.Success);
            Assert.AreEqual(BattleReplayDecodeError.CorruptData, resNotGz.Error);

            // 合法 gzip 但内部不是合法 protobuf
            byte[] garbage = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            var resBadPb = BattleReplayDecoder.Decode(new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecGzip,
                RawBytes = (uint)garbage.Length,
                Payload = ByteString.CopyFrom(Gzip(garbage)),
            });
            Assert.IsFalse(resBadPb.Success);
            Assert.AreEqual(BattleReplayDecodeError.CorruptData, resBadPb.Error);
        }

        [Test]
        public void ReplayWithoutStartState_IsRejected()
        {
            byte[] raw = new BattleReplay { BattleId = 100 }.ToByteArray();
            var env = new BattleReplayEnvelope
            {
                FormatVersion = 1,
                Codec = eBattleReplayCodec.BattleReplayCodecNone,
                RawBytes = (uint)raw.Length,
                Payload = ByteString.CopyFrom(raw),
            };
            var res = BattleReplayDecoder.Decode(env);
            Assert.IsFalse(res.Success);
            Assert.AreEqual(BattleReplayDecodeError.InvalidReplay, res.Error);
        }

        private static byte[] LoadWireFixture(string fileName, [CallerFilePath] string callerFile = "")
        {
            string dir = Path.GetDirectoryName(callerFile) ?? string.Empty;
            string path = Path.Combine(dir, "Fixtures", fileName);
            Assert.IsTrue(File.Exists(path), $"missing wire fixture: {path}");
            return File.ReadAllBytes(path);
        }

        [Test]
        public void RealRobot1v1WireFixture_DecodesAndPlaysThroughEveryRound()
        {
            byte[] wire = LoadWireFixture("robot_1v1_get_battle_report_response.pb.bytes");
            var response = Match.GetBattleReportResponse.Parser.ParseFrom(wire);
            Assert.IsTrue(response.ErrorMessage == null || response.ErrorMessage.Id == 0);
            Assert.NotNull(response.Replay);
            Assert.AreEqual(BattleReplayDecoder.SupportedFormatVersion, response.Replay.FormatVersion);
            Assert.AreEqual(eBattleReplayCodec.BattleReplayCodecGzip, response.Replay.Codec);

            var decoded = BattleReplayDecoder.Decode(response.Replay);
            Assert.IsTrue(decoded.Success, decoded.Message);

            var replay = decoded.Replay;
            Assert.AreNotEqual(0UL, replay.BattleId);
            Assert.AreEqual((uint)Match.MatchMode._1V1, replay.MatchMode);
            Assert.AreEqual(eBattleReplayEndReason.BattleReplayEndFinished, replay.EndReason);
            Assert.GreaterOrEqual(replay.StartState.Actors.Count, 2);
            Assert.AreEqual(0UL, replay.StartState.ActionDeadlineMs);
            Assert.AreEqual(0, replay.StartState.PendingActorIds.Count);
            Assert.AreEqual(0, replay.StartState.SelfItems.Count);
            foreach (var actor in replay.StartState.Actors)
            {
                Assert.AreEqual(0, actor.SkillCooldownRounds.Count);
                Assert.IsNull(actor.Combat);
                Assert.IsNull(actor.SupportEffects);
            }
            Assert.GreaterOrEqual(replay.Rounds.Count, 1);
            if (!replay.Truncated)
                Assert.AreEqual((int)replay.TotalRounds, replay.Rounds.Count);

            var player = new BattleReplayPlayer(replay);
            Assert.AreSame(replay.StartState, player.SeekToRound(1));
            int played = 0;
            while (player.TryNextRound(out var turn))
            {
                played++;
                Assert.NotNull(turn.State);
                Assert.AreEqual(0UL, turn.State.ActionDeadlineMs);
                Assert.AreEqual(0, turn.State.PendingActorIds.Count);
            }
            Assert.AreEqual(replay.Rounds.Count, played);
            Assert.IsTrue(player.IsFinished);
            Assert.AreSame(replay.Rounds[replay.Rounds.Count - 1].State, player.FinalDisplayState());

            if (replay.Rounds.Count >= 3)
            {
                int mid = replay.Rounds.Count / 2 + 1;
                Assert.AreSame(replay.Rounds[mid - 2].State, player.SeekToRound(mid));
                Assert.IsTrue(player.TryNextRound(out var resumed));
                Assert.AreEqual(replay.Rounds[mid - 1].RoundIndex, resumed.RoundIndex);
            }
        }

        [Test]
        public void DeadlineDrawWireFixture_DecodesAndLandsOnFinalState()
        {
            byte[] wire = LoadWireFixture("deadline_draw_get_battle_report_response.pb.bytes");
            var response = Match.GetBattleReportResponse.Parser.ParseFrom(wire);
            Assert.IsTrue(response.ErrorMessage == null || response.ErrorMessage.Id == 0);
            Assert.NotNull(response.Replay);
            Assert.AreEqual(eBattleReplayCodec.BattleReplayCodecGzip, response.Replay.Codec);

            var decoded = BattleReplayDecoder.Decode(response.Replay);
            Assert.IsTrue(decoded.Success, decoded.Message);

            var replay = decoded.Replay;
            Assert.AreEqual(eBattleReplayEndReason.BattleReplayEndDeadlineDraw, replay.EndReason);
            Assert.AreEqual(eBattleOutcome.BattleOutcomeDraw, replay.Outcome);
            Assert.AreEqual(0, replay.Rounds.Count);
            Assert.NotNull(replay.FinalState);
            Assert.AreEqual(0UL, replay.FinalState.ActionDeadlineMs);
            Assert.AreEqual(0, replay.FinalState.PendingActorIds.Count);

            var player = new BattleReplayPlayer(replay);
            Assert.AreSame(replay.StartState, player.SeekToRound(1));
            Assert.IsFalse(player.TryNextRound(out _));
            Assert.IsTrue(player.IsFinished);
            Assert.AreSame(replay.FinalState, player.FinalDisplayState());
            Assert.AreEqual(BattleReplayPlayer.DeadlineDrawLabel, player.BuildEndingBanner());
        }
    }
}
