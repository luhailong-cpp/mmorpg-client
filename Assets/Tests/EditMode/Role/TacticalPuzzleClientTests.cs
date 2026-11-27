using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.TacticalPuzzle;
using MmorpgClient.Net.Generated;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class TacticalPuzzleClientTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 8801ul;
            public bool IsReady { get; set; } = true;
            public event Action Disconnected;

            public readonly List<(uint msgId, IMessage req)> Calls = new();
            private readonly Queue<Action> _pending = new();
            public Func<uint, IMessage, (IMessage resp, string err)> Responder;

            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }

            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                Calls.Add((messageId, request));
                var (resp, err) = Responder(messageId, request);
                if (err != null) _pending.Enqueue(() => onError(err));
                else _pending.Enqueue(() => onResponse((TResp)resp));
            }

            public void SendOneWay(uint messageId, IMessage request) { }

            public void Flush()
            {
                while (_pending.Count > 0) _pending.Dequeue().Invoke();
            }

            public void TriggerDisconnect()
            {
                IsReady = false;
                Disconnected?.Invoke();
            }
        }

        [Test]
        public void Models_ValidateDraft_EnforcesAllBoundsAndWhitelists()
        {
            var spec = TacticalPuzzleModels.CreateDefaultSpec();
            Assert.That(TacticalPuzzleModels.ValidateDraft("三回合破盾反杀", "先驱散再爆发", 0x3u, spec, out string err), Is.True, err);

            // 单人无宠（PetPresetId = 0）合法
            spec.PetPresetId = 0u;
            Assert.That(TacticalPuzzleModels.ValidateDraft("单人无宠破阵局", "", 1u, spec, out err), Is.True, err);

            // 题名长度与控制字符限制
            Assert.That(TacticalPuzzleModels.ValidateDraft("太短", "", 0u, spec, out _), Is.False);
            Assert.That(TacticalPuzzleModels.ValidateDraft(new string('残', 21), "", 0u, spec, out _), Is.False);
            Assert.That(TacticalPuzzleModels.ValidateDraft("非法\n换行题名", "", 0u, spec, out _), Is.False);

            // 提示语与标签上限
            Assert.That(TacticalPuzzleModels.ValidateDraft("合法残局题名", new string('提', 41), 0u, spec, out _), Is.False);
            Assert.That(TacticalPuzzleModels.ValidateDraft("合法残局题名", "", 0x7u, spec, out _), Is.False); // 3 tags > 2
            Assert.That(TacticalPuzzleModels.ValidateDraft("合法残局题名", "", 1u << 7, spec, out _), Is.False); // out of 6-bit mask

            // 盘面边界校验
            var badRound = spec.Clone(); badRound.RoundLimit = 7u;
            Assert.That(TacticalPuzzleModels.ValidateSpec(badRound, out _), Is.False);
            var badChar = spec.Clone(); badChar.CharacterPresetId = 999u;
            Assert.That(TacticalPuzzleModels.ValidateSpec(badChar, out _), Is.False);
            var badHp = spec.Clone(); badHp.PlayerInitialHpPct = 10u;
            Assert.That(TacticalPuzzleModels.ValidateSpec(badHp, out _), Is.False);
            var badEnemyBuff = spec.Clone(); badEnemyBuff.Enemies[0].InitialBuffKind = 5u;
            Assert.That(TacticalPuzzleModels.ValidateSpec(badEnemyBuff, out _), Is.False);
            var badEnemySpeed = spec.Clone(); badEnemySpeed.Enemies[0].SpeedDeltaPct = 40;
            Assert.That(TacticalPuzzleModels.ValidateSpec(badEnemySpeed, out _), Is.False);
            var noEnemy = spec.Clone(); noEnemy.Enemies.Clear();
            Assert.That(TacticalPuzzleModels.ValidateSpec(noEnemy, out _), Is.False);
        }

        [Test]
        public void Models_ChatCardRoundTripAndFormatting()
        {
            var summary = new Match.TacticalPuzzleSummaryView
            {
                PuzzleId = 42009ul,
                Title = "玄盾反震二回合",
                GlobalBestRounds = 2u,
                Spec = TacticalPuzzleModels.CreateDefaultSpec(),
            };
            string draft = TacticalPuzzleModels.FormatChatShareDraft(summary);
            Assert.That(TacticalPuzzleModels.TryParsePuzzleCardId(draft, out ulong parsedId), Is.True);
            Assert.That(parsedId, Is.EqualTo(42009ul));
            string stripped = TacticalPuzzleModels.StripPuzzleCardTag(draft);
            Assert.That(stripped, Does.Not.Contain("#PUZ:"));
            Assert.That(stripped, Does.Contain("玄盾反震二回合"));

            Assert.That(TacticalPuzzleModels.TryParsePuzzleCardId("#PUZ:0#", out _), Is.False);
            Assert.That(TacticalPuzzleModels.TryParsePuzzleCardId("#TJC:123#", out _), Is.False);
            Assert.That(TacticalPuzzleModels.DeriveHonorTitle(3u), Is.EqualTo("万象棋圣"));
            Assert.That(TacticalPuzzleModels.FormatClearRate(10u, 3u), Is.EqualTo("破局率 30%（3/10）"));
            Assert.That(TacticalPuzzleModels.EnemyBuffName(4u), Does.Contain("反震"));
            for (uint tip = 16064u; tip <= 16072u; tip++)
            {
                Assert.That(TacticalPuzzleModels.DescribeTip(tip), Does.Not.Contain("错误码"));
            }
        }

        [Test]
        public void Client_FullRpcLifecycleAndSessionBoundaryIsolation()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 10f;
            var notices = new List<string>();

            var summary = new Match.TacticalPuzzleSummaryView
            {
                PuzzleId = 7001ul,
                AuthorPlayerId = 8801ul,
                AuthorName = "出题道友",
                State = Match.TacticalPuzzleState.Draft,
                SpecRevision = 1u,
                Title = "三回合破盾奇局",
                TagMask = 1u,
                Spec = TacticalPuzzleModels.CreateDefaultSpec(),
            };

            net.Responder = (msgId, req) =>
            {
                if (msgId == MatchServiceRpc.ListTacticalPuzzlesMessageId)
                {
                    var resp = new Match.ListTacticalPuzzlesResponse
                    {
                        TotalCount = 1u,
                        Profile = new Match.TacticalPuzzleProfileView
                        {
                            PublishedCount = 0u,
                            DraftCount = 1u,
                            TotalClearedCount = 3u,
                            TotalLikesReceived = 2u,
                            HonorTier = 1u,
                            HonorTitle = "破局国手",
                        },
                    };
                    resp.Puzzles.Add(summary.Clone());
                    return (resp, null);
                }
                if (msgId == MatchServiceRpc.SaveTacticalPuzzleDraftMessageId)
                {
                    return (new Match.SaveTacticalPuzzleDraftResponse { Puzzle = summary.Clone() }, null);
                }
                if (msgId == MatchServiceRpc.StartTacticalPuzzleBattleMessageId)
                {
                    var r = (Match.StartTacticalPuzzleBattleRequest)req;
                    Assert.That(r.IsAuthorVerify, Is.True);
                    Assert.That(r.RequestId, Is.Not.Empty);
                    return (new Match.StartTacticalPuzzleBattleResponse { BattleId = 99001ul, SpecRevision = 1u }, null);
                }
                if (msgId == MatchServiceRpc.LikeTacticalPuzzleMessageId)
                {
                    return (new Match.LikeTacticalPuzzleResponse { LikeCount = 5u, SelfLiked = true }, null);
                }
                if (msgId == MatchServiceRpc.DeleteTacticalPuzzleMessageId)
                {
                    return (new Match.DeleteTacticalPuzzleResponse(), null);
                }
                return (null, "unexpected");
            };

            using var client = new TacticalPuzzleClient(net, () => conn, () => now);
            client.Notice += msg => notices.Add(msg);

            // 1. List
            Assert.That(client.RequestList(Match.TacticalPuzzleListTab.MyCreated, 0u, 0u, 20u, force: true), Is.True);
            net.Flush();
            Assert.That(client.Puzzles.Count, Is.EqualTo(1));
            Assert.That(client.Profile?.HonorTitle, Is.EqualTo("破局国手"));

            // 2. Save draft
            now = 20f;
            Match.TacticalPuzzleSummaryView saved = null;
            Assert.That(client.SaveDraft(0ul, "三回合破盾奇局", "先破盾", 1u, TacticalPuzzleModels.CreateDefaultSpec(), p => saved = p), Is.True);
            net.Flush();
            Assert.That(saved?.PuzzleId, Is.EqualTo(7001ul));

            // 3. Start author verify battle
            now = 30f;
            ulong startedBattleId = 0ul;
            Assert.That(client.StartBattle(7001ul, true, bid => startedBattleId = bid), Is.True);
            net.Flush();
            Assert.That(startedBattleId, Is.EqualTo(99001ul));

            // 4. Like updates cache
            now = 40f;
            uint likes = 0u;
            Assert.That(client.LikePuzzle(7001ul, l => likes = l), Is.True);
            net.Flush();
            Assert.That(likes, Is.EqualTo(5u));
            Assert.That(client.Puzzles[0].SelfLiked, Is.True);

            // 5. HandleBattleSettled invalidates list
            client.HandleBattleSettled();
            Assert.That(client.IsListDirty, Is.True);

            // 6. Delete removes item
            now = 50f;
            bool deleted = false;
            Assert.That(client.DeletePuzzle(7001ul, () => deleted = true), Is.True);
            net.Flush();
            Assert.That(deleted, Is.True);
            Assert.That(client.Puzzles.Count, Is.EqualTo(0));

            // 7. Role switch discards stale in-flight callback
            now = 60f;
            Assert.That(client.RequestList(Match.TacticalPuzzleListTab.Hot, 0u, 0u, 20u, force: true), Is.True);
            net.PlayerId = 9999ul;
            net.Flush();
            Assert.That(client.Puzzles.Count, Is.EqualTo(0));
            Assert.That(client.Profile, Is.Null);
        }

        [Test]
        public void V2ArtSlices_AllTenFilesExistWithExactDimensionsAndNineSliceMetas()
        {
            string sliceDir = ResolveTacticalPuzzleSliceDir();
            Assert.That(Directory.Exists(sliceDir), Is.True, $"Missing slice directory: {sliceDir}");

            var expected = new (string file, int w, int h, string border)[]
            {
                ("hud_entry_puzzle.png", 96, 96, "{x: 0, y: 0, z: 0, w: 0}"),
                ("puzzle_card_bg.png", 840, 176, "{x: 28, y: 24, z: 28, w: 24}"),
                ("puzzle_editor_panel_bg.png", 820, 540, "{x: 28, y: 28, z: 28, w: 28}"),
                ("puzzle_chat_card_bg.png", 640, 126, "{x: 24, y: 20, z: 24, w: 20}"),
                ("badge_verified_seal.png", 128, 128, "{x: 0, y: 0, z: 0, w: 0}"),
                ("badge_cleared_stamp.png", 128, 128, "{x: 0, y: 0, z: 0, w: 0}"),
                ("badge_draft_tag.png", 148, 36, "{x: 16, y: 0, z: 16, w: 0}"),
                ("icon_puzzle_like.png", 128, 128, "{x: 0, y: 0, z: 0, w: 0}"),
                ("icon_puzzle_solution.png", 128, 128, "{x: 0, y: 0, z: 0, w: 0}"),
                ("honor_puzzle_master.png", 256, 256, "{x: 0, y: 0, z: 0, w: 0}"),
            };

            var seenGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guidRegex = new Regex(@"^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (var item in expected)
            {
                string pngPath = Path.Combine(sliceDir, item.file);
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing PNG: {pngPath}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing meta: {metaPath}");

                byte[] header = new byte[24];
                using (var fs = File.OpenRead(pngPath))
                {
                    Assert.That(fs.Read(header, 0, 24), Is.EqualTo(24));
                }
                Assert.That(header[0], Is.EqualTo(0x89));
                Assert.That(header[1], Is.EqualTo(0x50));
                Assert.That(header[2], Is.EqualTo(0x4E));
                Assert.That(header[3], Is.EqualTo(0x47));
                int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                Assert.That(w, Is.EqualTo(item.w), $"{item.file} width mismatch");
                Assert.That(h, Is.EqualTo(item.h), $"{item.file} height mismatch");

                string meta = File.ReadAllText(metaPath);
                var m = guidRegex.Match(meta);
                Assert.That(m.Success, Is.True, $"{item.file}.meta missing valid guid");
                Assert.That(seenGuids.Add(m.Groups[1].Value), Is.True, $"Duplicate guid on {item.file}");
                Assert.That(meta, Does.Contain("textureType: 8"), $"{item.file}.meta must be Sprite");
                Assert.That(meta, Does.Contain("spriteMode: 1"), $"{item.file}.meta must be Single sprite");
                Assert.That(meta, Does.Contain("alphaIsTransparency: 1"), $"{item.file}.meta must preserve alpha");
                Assert.That(meta, Does.Contain("spriteBorder: " + item.border), $"{item.file}.meta spriteBorder mismatch");
            }
        }

        private static string ResolveTacticalPuzzleSliceDir()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, "Assets", "Resources", "UI", "Ugui", "TacticalPuzzleV1");
                if (Directory.Exists(candidate)) return candidate;
                string nested = Path.Combine(dir, "mmorpg-client", "Assets", "Resources", "UI", "Ugui", "TacticalPuzzleV1");
                if (Directory.Exists(nested)) return nested;
                dir = Path.GetDirectoryName(dir);
            }
            return Path.Combine("Assets", "Resources", "UI", "Ugui", "TacticalPuzzleV1");
        }
    }
}
