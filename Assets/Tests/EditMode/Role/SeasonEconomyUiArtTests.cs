using System.Collections.Generic;
using System.IO;
using MmorpgClient.UI.Ugui;
using NUnit.Framework;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class SeasonEconomyUiArtTests
    {
        [Test]
        public void IntentAndBossIcons_CoverAllEightSeasonEconomyIntentAssets()
        {
            var seen = new HashSet<string>();
            for (uint kind = 1u; kind <= 4u; ++kind)
            {
                SeasonEconomyUiArt.BossCardIntentIcons(kind, out string primary, out string secondary);
                Assert.That(primary, Is.Not.Empty);
                Assert.That(secondary, Is.Not.Empty);
                seen.Add(primary);
                seen.Add(secondary);
            }
            Assert.That(seen.Count, Is.EqualTo(8), "四张首领试炼卡应完整覆盖全部 8 张意图图标");
            Assert.That(SeasonEconomyUiArt.IntentIconKey(1u), Is.EqualTo("intent_slow_cleave"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(2u), Is.EqualTo("intent_guard_heal"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(3u), Is.EqualTo("intent_stance_phys"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(4u), Is.EqualTo("intent_stance_magic"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(5u), Is.EqualTo("intent_guard_shield"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(6u), Is.EqualTo("intent_guard_bind"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(7u), Is.EqualTo("intent_magic_strike"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(8u), Is.EqualTo("intent_venom_aoe"));
            Assert.That(SeasonEconomyUiArt.IntentIconKey(99u), Is.EqualTo("intent_slow_cleave"));

            Assert.That(SeasonEconomyUiArt.BossAvatarIconKey(1u), Is.EqualTo("boss_break_avatar"));
            Assert.That(SeasonEconomyUiArt.BossAvatarIconKey(2u), Is.EqualTo("boss_break_avatar"));
            Assert.That(SeasonEconomyUiArt.BossAvatarIconKey(3u), Is.EqualTo("boss_barrier_avatar"));
            Assert.That(SeasonEconomyUiArt.BossAvatarIconKey(4u), Is.EqualTo("boss_venom_avatar"));
            Assert.That(SeasonEconomyUiArt.BossAvatarIconKey(8u), Is.EqualTo("boss_avatar_dual_guard"));
            Assert.That(SeasonEconomyUiArt.BossAvatarIconKey(99u), Is.EqualTo("boss_break_avatar"));
            SeasonEconomyUiArt.BossCardIntentIcons(8u, out string dualPrimary, out string dualSecondary);
            Assert.That(dualPrimary, Is.EqualTo("intent_guard_heal"));
            Assert.That(dualSecondary, Is.EqualTo("intent_stance_phys"));
        }

        [Test]
        public void QualityTagAndMasteryBadge_MapAllTiersDeterministically()
        {
            Assert.That(SeasonEconomyUiArt.QualityTagKey(0u), Is.EqualTo(string.Empty));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(1u), Is.EqualTo("quality_tag_1_common"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(2u), Is.EqualTo("quality_tag_2_good"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(3u), Is.EqualTo("quality_tag_3_fine"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(4u), Is.EqualTo("quality_tag_4_precious"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(5u), Is.EqualTo("quality_tag_5_supreme"));
            Assert.That(SeasonEconomyUiArt.QualityTagKey(9u), Is.EqualTo("quality_tag_5_supreme"));

            Assert.That(SeasonEconomyUiArt.MasteryBadgeKey(0u), Is.EqualTo("mastery_badge_apprentice"));
            Assert.That(SeasonEconomyUiArt.MasteryBadgeKey(1u), Is.EqualTo("mastery_badge_apprentice"));
            Assert.That(SeasonEconomyUiArt.MasteryBadgeKey(2u), Is.EqualTo("mastery_badge_artisan"));
            Assert.That(SeasonEconomyUiArt.MasteryBadgeKey(3u), Is.EqualTo("mastery_badge_grandmaster"));

            Assert.That(SeasonEconomyUiArt.GrowthSkillIconKey(12020u), Is.EqualTo("skill_yu_zhan_yu_yong"));
            Assert.That(SeasonEconomyUiArt.GrowthSkillIconKey(12021u), Is.EqualTo("skill_shen_mou_yuan_lv"));
            Assert.That(SeasonEconomyUiArt.GrowthSkillIconKey(12022u), Is.EqualTo("skill_bu_bu_wei_ying"));
            Assert.That(SeasonEconomyUiArt.GrowthSkillIconKey(1001u), Is.EqualTo(string.Empty));
        }

        [Test]
        public void TryFindProtectedGuardHp_ReadsAuthoritativeActorHealth()
        {
            var state = new BattleStateS2C();
            state.Actors.Add(new BattleActorState
            {
                ActorId = 9002UL,
                Name = "回春护卫",
                MaxHealth = 4200UL,
                Attributes = new BaseAttributesComp { Health = 2940UL },
            });

            Assert.That(SeasonEconomyUiArt.TryFindProtectedGuardHp(state, 9002UL, out string name, out ulong hp, out ulong maxHp), Is.True);
            Assert.That(name, Is.EqualTo("回春护卫"));
            Assert.That(hp, Is.EqualTo(2940UL));
            Assert.That(maxHp, Is.EqualTo(4200UL));
            Assert.That(SeasonEconomyUiArt.TryFindProtectedGuardHp(state, 9999UL, out _, out _, out _), Is.False);
            Assert.That(SeasonEconomyUiArt.TryFindProtectedGuardHp(null, 9002UL, out _, out _, out _), Is.False);
        }

        [Test]
        public void WeeklyResetAndProgressRatio_HandleBoundaryValues()
        {
            const ulong nowMs = 1_760_000_000_000UL;
            const ulong hourMs = 3_600_000UL;
            const ulong dayMs = 24UL * hourMs;
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(nowMs + 3UL * dayMs + 14UL * hourMs, nowMs),
                Is.EqualTo("剩余 3天14时"));
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(nowMs + 5UL * hourMs + 20UL * 60_000UL, nowMs),
                Is.EqualTo("剩余 5小时20分"));
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(nowMs + 10_000UL, nowMs),
                Is.EqualTo("剩余 1分"));
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(nowMs, nowMs), Is.EqualTo(string.Empty));
            Assert.That(SeasonEconomyUiArt.FormatWeeklyResetRemaining(0UL, nowMs), Is.EqualTo(string.Empty));

            Assert.That(SeasonEconomyUiArt.ComputeProgressRatio(0UL, 0UL), Is.EqualTo(0f));
            Assert.That(SeasonEconomyUiArt.ComputeProgressRatio(7UL, 20UL), Is.EqualTo(0.35f).Within(0.001f));
            Assert.That(SeasonEconomyUiArt.ComputeProgressRatio(19UL, 20UL, forceFull: true), Is.EqualTo(1f));
            Assert.That(SeasonEconomyUiArt.ComputeProgressRatio(999UL, 660UL), Is.EqualTo(1f));

            Assert.That(SeasonEconomyUiArt.IsMaxEnhanced(0u, 0u), Is.False);
            Assert.That(SeasonEconomyUiArt.IsMaxEnhanced(10u, 11u), Is.False);
            Assert.That(SeasonEconomyUiArt.IsMaxEnhanced(11u, 11u), Is.True);
            Assert.That(SeasonEconomyUiArt.IsMaxEnhanced(12u, 11u), Is.True);
            Assert.That(SeasonEconomyUiArt.SeasonFrame9SliceControlKey, Is.EqualTo("window_frame_season_9slice"));
            Assert.That(SeasonEconomyUiArt.SeasonTitlePlaqueControlKey, Is.EqualTo("season_title_plaque"));
            Assert.That(SeasonEconomyUiArt.Plus11SlotHaloIconKey, Is.EqualTo("plus11_slot_halo"));
        }

        [Test]
        public void CraftQualitySkillAssets_All26RuntimeSpritesAndBordersExistWithMeta()
        {
            string cwd = Directory.GetCurrentDirectory();
            string assetsRoot = Directory.Exists(Path.Combine(cwd, "Assets", "Resources"))
                ? Path.Combine(cwd, "Assets")
                : Path.Combine(cwd, "mmorpg-client", "Assets");
            string craftRoot = Path.Combine(assetsRoot, "Resources", "UI", "Ugui", "CraftQualitySkill");
            string skillsRoot = Path.Combine(assetsRoot, "Resources", "UI", "Skills");
            Assert.That(Directory.Exists(craftRoot), Is.True, $"Missing CraftQualitySkill directory at {craftRoot}");

            var expectedControls = new (string File, string Border)[]
            {
                ("quality_tag_1_common.png", "spriteBorder: {x: 64, y: 0, z: 64, w: 0}"),
                ("quality_tag_2_good.png", "spriteBorder: {x: 64, y: 0, z: 64, w: 0}"),
                ("quality_tag_3_fine.png", "spriteBorder: {x: 64, y: 0, z: 64, w: 0}"),
                ("quality_tag_4_precious.png", "spriteBorder: {x: 64, y: 0, z: 64, w: 0}"),
                ("quality_tag_5_supreme.png", "spriteBorder: {x: 68, y: 0, z: 68, w: 0}"),
                ("pity_bar_bg.png", "spriteBorder: {x: 32, y: 0, z: 32, w: 0}"),
                ("pity_bar_fill_normal.png", "spriteBorder: {x: 28, y: 0, z: 28, w: 0}"),
                ("pity_bar_fill_ready.png", "spriteBorder: {x: 28, y: 0, z: 28, w: 0}"),
                ("seal_master_signature.png", "spriteBorder: {x: 53, y: 0, z: 53, w: 0}"),
                ("banner_safe_escrow.png", "spriteBorder: {x: 62, y: 0, z: 62, w: 0}"),
                ("mastery_badge_apprentice.png", "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("mastery_badge_artisan.png", "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("mastery_badge_grandmaster.png", "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("scroll_tab_normal.png", "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("scroll_tab_selected.png", "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("badge_rare_skill.png", "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
            };

            foreach (var (file, border) in expectedControls)
            {
                string pngPath = Path.Combine(craftRoot, file);
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing control PNG: {file}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing control meta: {file}.meta");
                string meta = File.ReadAllText(metaPath);
                Assert.That(meta, Does.Contain("textureType: 8"), file);
                Assert.That(meta, Does.Contain("spriteMode: 1"), file);
                Assert.That(meta, Does.Contain(border), file);
            }

            string[] expectedIcons =
            {
                "item_star_guarantee_charm.png",
                "item_enlightenment_page.png",
                "item_rare_scroll_fragment.png",
                "item_celestial_ink.png",
                "skill_yu_zhan_yu_yong.png",
                "skill_shen_mou_yuan_lv.png",
                "skill_bu_bu_wei_ying.png",
            };
            foreach (string icon in expectedIcons)
            {
                string pngPath = Path.Combine(craftRoot, "Icons", icon);
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing icon PNG: {icon}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing icon meta: {icon}.meta");
                string meta = File.ReadAllText(metaPath);
                Assert.That(meta, Does.Contain("textureType: 8"), icon);
                Assert.That(meta, Does.Contain("spriteMode: 1"), icon);
                Assert.That(meta, Does.Contain("spriteBorder: {x: 0, y: 0, z: 0, w: 0}"), icon);
            }

            foreach (string sid in new[] { "12020.png", "12021.png", "12022.png", "10026.png", "10041.png", "11009.png", "11016.png", "12009.png", "12019.png" })
            {
                string pngPath = Path.Combine(skillsRoot, sid);
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing UI/Skills icon: {sid}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing UI/Skills meta: {sid}.meta");
                string meta = File.ReadAllText(metaPath);
                Assert.That(meta, Does.Contain("textureType: 8"), sid);
                Assert.That(meta, Does.Contain("spriteMode: 1"), sid);
            }

            string itemIconsRoot = Path.Combine(assetsRoot, "Resources", "UI", "Icons");
            foreach (string itemFile in new[] { "item_reforge_sand.png", "item_bound_artifact_voucher.png", "item_sealed_skill_book.png" })
            {
                string pngPath = Path.Combine(itemIconsRoot, itemFile);
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing UI/Icons PNG: {itemFile}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing UI/Icons meta: {itemFile}.meta");
                byte[] header = new byte[26];
                using (var fs = File.OpenRead(pngPath))
                    Assert.That(fs.Read(header, 0, header.Length), Is.EqualTo(header.Length), itemFile);
                int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                Assert.That(w, Is.EqualTo(512), itemFile);
                Assert.That(h, Is.EqualTo(512), itemFile);
                Assert.That(header[25], Is.EqualTo((byte)6), $"{itemFile} must be transparent RGBA PNG");
                string meta = File.ReadAllText(metaPath);
                Assert.That(meta, Does.Contain("textureType: 8"), itemFile);
                Assert.That(meta, Does.Contain("spriteMode: 1"), itemFile);
            }
        }

        [Test]
        public void CraftQualitySkillV2V5IncrementalAssets_All5SpritesAndMetasMatchContract()
        {
            string cwd = Directory.GetCurrentDirectory();
            string craftRoot = Directory.Exists(Path.Combine(cwd, "Assets", "Resources"))
                ? Path.Combine(cwd, "Assets", "Resources", "UI", "Ugui", "CraftQualitySkill")
                : Path.Combine(cwd, "mmorpg-client", "Assets", "Resources", "UI", "Ugui", "CraftQualitySkill");

            var expected = new (string RelPath, int Width, int Height, string Border)[]
            {
                ("Icons/icon_master_honor.png", 512, 512, "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("Icons/icon_batch_polish.png", 512, 512, "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("Icons/icon_scroll_archive.png", 512, 512, "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("Icons/icon_wanted_board.png", 512, 512, "spriteBorder: {x: 0, y: 0, z: 0, w: 0}"),
                ("badge_team_free_proxy.png", 220, 56, "spriteBorder: {x: 20, y: 0, z: 20, w: 0}"),
            };

            var seenGuids = new HashSet<string>();
            foreach (var (rel, wantW, wantH, border) in expected)
            {
                string pngPath = Path.Combine(craftRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing Craft V2-V5 PNG: {rel}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing Craft V2-V5 meta: {rel}.meta");

                byte[] header = new byte[26];
                using (var fs = File.OpenRead(pngPath))
                    Assert.That(fs.Read(header, 0, header.Length), Is.EqualTo(header.Length), rel);
                int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                Assert.That(w, Is.EqualTo(wantW), $"{rel} width");
                Assert.That(h, Is.EqualTo(wantH), $"{rel} height");
                Assert.That(header[25], Is.EqualTo((byte)6), $"{rel} must be transparent RGBA PNG-32");

                string meta = File.ReadAllText(metaPath);
                Assert.That(meta, Does.Contain("enableMipMap: 0"), rel);
                Assert.That(meta, Does.Contain("sRGBTexture: 1"), rel);
                Assert.That(meta, Does.Contain("alphaIsTransparency: 1"), rel);
                Assert.That(meta, Does.Contain("textureType: 8"), rel);
                Assert.That(meta, Does.Contain("spriteMode: 1"), rel);
                Assert.That(meta, Does.Contain(border), rel);

                string guidLine = null;
                foreach (string line in File.ReadAllLines(metaPath))
                {
                    if (line.StartsWith("guid: ")) { guidLine = line.Substring(6).Trim(); break; }
                }
                Assert.That(guidLine, Is.Not.Null.And.Not.Empty, rel);
                Assert.That(seenGuids.Add(guidLine), Is.True, $"Duplicate GUID {guidLine} on {rel}");
            }
            Assert.That(seenGuids.Count, Is.EqualTo(5));
        }

        [Test]
        public void BossTrialV2Assets_All26RuntimeTexturesAndMetasMatchContract()
        {
            string cwd = Directory.GetCurrentDirectory();
            string resourcesRoot = Directory.Exists(Path.Combine(cwd, "Assets", "Resources"))
                ? Path.Combine(cwd, "Assets", "Resources")
                : Path.Combine(cwd, "mmorpg-client", "Assets", "Resources");

            var expected = new (string RelPath, int Width, int Height, bool IsSprite)[]
            {
                ("Battle/Monsters/109/idle_E_strip.png", 2048, 256, false),
                ("Battle/Monsters/109/attack_E_strip.png", 2048, 256, false),
                ("Battle/Monsters/109/hit_E_strip.png", 2048, 256, false),
                ("Battle/Monsters/109/die_E_strip.png", 2048, 256, false),
                ("Battle/Monsters/109/portrait.png", 256, 256, false),
                ("Battle/Monsters/110/idle_E_strip.png", 2048, 256, false),
                ("Battle/Monsters/110/attack_E_strip.png", 2048, 256, false),
                ("Battle/Monsters/110/hit_E_strip.png", 2048, 256, false),
                ("Battle/Monsters/110/die_E_strip.png", 2048, 256, false),
                ("Battle/Monsters/110/portrait.png", 256, 256, false),
                ("Battle/Fx/boss_exposed_crack_strip.png", 2048, 256, false),
                ("Battle/Fx/boss_stance_break_strip.png", 3072, 384, false),
                ("Battle/Fx/boss_phase2_enrage_strip.png", 3072, 384, false),
                ("UI/Ugui/SeasonEconomyV1/Icons/boss_avatar_dual_guard.png", 256, 256, true),
                ("UI/Ugui/Battle/BossTrialV2/icon_exposed.png", 64, 64, true),
                ("UI/Ugui/Battle/BossTrialV2/icon_momentum.png", 64, 64, true),
                ("UI/Ugui/Battle/BossTrialV2/icon_sunder.png", 64, 64, true),
                ("UI/Ugui/Battle/BossTrialV2/icon_dishearten.png", 64, 64, true),
                ("UI/Ugui/Battle/BossTrialV2/icon_focus.png", 64, 64, true),
                ("UI/Ugui/Battle/BossTrialV2/icon_splash.png", 64, 64, true),
                ("UI/Ugui/TrialJournal/Badges/badge_trial_dual_guard.png", 128, 128, true),
                ("UI/Ugui/TrialJournal/Badges/badge_trial_sword.png", 128, 128, true),
                ("UI/Ugui/TrialJournal/Badges/badge_trial_master.png", 128, 128, true),
                ("UI/Ugui/TrialJournal/Badges/badge_trial_three_bosses.png", 128, 128, true),
                ("UI/Ugui/TrialJournal/Badges/badge_trial_grandmaster.png", 128, 128, true),
                ("UI/Ugui/TrialJournal/Badges/badge_season_s1_trial.png", 128, 128, true),
            };

            var seenGuids = new HashSet<string>();
            foreach (var (rel, wantW, wantH, isSprite) in expected)
            {
                string pngPath = Path.Combine(resourcesRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing BossTrialV2 PNG: {rel}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing BossTrialV2 meta: {rel}.meta");

                byte[] header = new byte[26];
                using (var fs = File.OpenRead(pngPath))
                    Assert.That(fs.Read(header, 0, header.Length), Is.EqualTo(header.Length), rel);
                int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                byte colorType = header[25];
                Assert.That(w, Is.EqualTo(wantW), $"{rel} width");
                Assert.That(h, Is.EqualTo(wantH), $"{rel} height");
                Assert.That(colorType, Is.EqualTo((byte)6), $"{rel} must be RGBA PNG (colorType=6)");

                string meta = File.ReadAllText(metaPath);
                Assert.That(meta, Does.Contain("enableMipMap: 0"), rel);
                Assert.That(meta, Does.Contain("filterMode: 1"), rel);
                Assert.That(meta, Does.Contain("wrapU: 1"), rel);
                Assert.That(meta, Does.Contain("alphaIsTransparency: 1"), rel);
                Assert.That(meta, Does.Contain(isSprite ? "textureType: 8" : "textureType: 0"), rel);
                Assert.That(meta, Does.Contain(isSprite ? "spriteMode: 1" : "spriteMode: 0"), rel);
                if (isSprite) Assert.That(meta, Does.Contain("spriteBorder: {x: 0, y: 0, z: 0, w: 0}"), rel);

                string guidLine = null;
                foreach (string line in File.ReadAllLines(metaPath))
                {
                    if (line.StartsWith("guid: ")) { guidLine = line.Substring(6).Trim(); break; }
                }
                Assert.That(guidLine, Is.Not.Null.And.Not.Empty, rel);
                Assert.That(seenGuids.Add(guidLine), Is.True, $"Duplicate GUID {guidLine} on {rel}");
            }
            Assert.That(seenGuids.Count, Is.EqualTo(26));
        }

        [Test]
        public void BossTrialWeeklyAffixAssets_All5IconsAndMetasMatchContract()
        {
            string cwd = Directory.GetCurrentDirectory();
            string resourcesRoot = Directory.Exists(Path.Combine(cwd, "Assets", "Resources"))
                ? Path.Combine(cwd, "Assets", "Resources")
                : Path.Combine(cwd, "mmorpg-client", "Assets", "Resources");

            string[] elements = { "metal", "wood", "water", "fire", "earth" };
            var seenGuids = new HashSet<string>();
            foreach (string el in elements)
            {
                string rel = $"UI/Ugui/CoreLoopV2/icon_weekly_affix_{el}.png";
                string pngPath = Path.Combine(resourcesRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                string metaPath = pngPath + ".meta";
                Assert.That(File.Exists(pngPath), Is.True, $"Missing WeeklyAffix PNG: {rel}");
                Assert.That(File.Exists(metaPath), Is.True, $"Missing WeeklyAffix meta: {rel}.meta");

                byte[] header = new byte[26];
                using (var fs = File.OpenRead(pngPath))
                    Assert.That(fs.Read(header, 0, header.Length), Is.EqualTo(header.Length), rel);
                int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                Assert.That(w, Is.EqualTo(64), $"{rel} width");
                Assert.That(h, Is.EqualTo(64), $"{rel} height");
                Assert.That(header[25], Is.EqualTo((byte)6), $"{rel} must be RGBA PNG-32");

                string meta = File.ReadAllText(metaPath);
                Assert.That(meta, Does.Contain("enableMipMap: 0"), rel);
                Assert.That(meta, Does.Contain("filterMode: 1"), rel);
                Assert.That(meta, Does.Contain("wrapU: 1"), rel);
                Assert.That(meta, Does.Contain("alphaIsTransparency: 1"), rel);
                Assert.That(meta, Does.Contain("textureType: 8"), rel);
                Assert.That(meta, Does.Contain("spriteMode: 1"), rel);
                Assert.That(meta, Does.Contain("spriteBorder: {x: 0, y: 0, z: 0, w: 0}"), rel);

                string guidLine = null;
                foreach (string line in File.ReadAllLines(metaPath))
                {
                    if (line.StartsWith("guid: ")) { guidLine = line.Substring(6).Trim(); break; }
                }
                Assert.That(guidLine, Is.Not.Null.And.Not.Empty, rel);
                Assert.That(seenGuids.Add(guidLine), Is.True, $"Duplicate GUID {guidLine} on {rel}");
            }
            Assert.That(seenGuids.Count, Is.EqualTo(5));
        }
    }
}
