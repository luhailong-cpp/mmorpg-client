using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>切好格的序列帧条(角色动作 / 特效 / 跑步条)。</summary>
    public sealed class StripAnim
    {
        public Sprite[] Frames;
        public float Fps = 10f;
        /// <summary>true = 资源只有 E 向、需要水平镜像显示。</summary>
        public bool Mirrored;
        public Vector2 Pivot = new Vector2(0.5f, 0.5f);

        public int Count => Frames?.Length ?? 0;
        public float DurationSeconds => Count == 0 || Fps <= 0f ? 0f : Count / Fps;

        /// <summary>按归一化进度取帧(末帧含)。</summary>
        public Sprite FrameAt(float t01)
        {
            if (Count == 0) return null;
            int i = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(t01) * Count), 0, Count - 1);
            return Frames[i];
        }
    }

    /// <summary>伤害数字字集(等宽单格,digits_meta.json 定义字符顺序)。</summary>
    public sealed class DigitFont
    {
        public Sprite[] Cells;
        public string Chars = string.Empty;
        public int CellWidth = 48;
        public int CellHeight = 64;

        public Sprite Lookup(char c)
        {
            if (Cells == null || string.IsNullOrEmpty(Chars)) return null;
            int i = Chars.IndexOf(c);
            return i >= 0 && i < Cells.Length ? Cells[i] : null;
        }
    }

    /// <summary>技能演出提示(客户端表 skill_presentation.json;缺表用启发式缺省)。</summary>
    public sealed class SkillPresentation
    {
        public uint SkillId;
        public string FxId = "slash_arc";
        /// <summary>attack | cast。</summary>
        public string Action = "attack";
        /// <summary>命中帧(0 基):动作帧条播到该帧时触发目标受击。</summary>
        public int HitFrame = 3;
        public bool IsCast => Action == "cast";
    }

    /// <summary>
    /// 战斗美术资源路径契约与降级策略(docs/design/battle-art-prompts.md §3;turn-battle-presentation.md §3/§5)
    /// —— 全部集中在这一处,缺图不报错、只回退:
    ///  - 角色动作帧条 Battle/Characters/&lt;id&gt;/&lt;action&gt;_{E,W}_strip:缺 W 用 E 镜像,缺动作返回 null(程序化动作);
    ///  - 怪物 Battle/Monsters/&lt;id&gt;/&lt;action&gt;_{E,W}_strip:缺图用程序化剪影;
    ///  - 玩家 idle:World/Characters/QdaoHeadbandBoy/walk_{E,W} 首帧;
    ///  - 特效 Battle/Fx/&lt;fxId&gt;_strip:8 帧 256,fps/pivot 读 ART_MANIFEST.json;
    ///  - 数字字集 Battle/UI/digits_&lt;variant&gt; + digits_meta.json;缺则 null(飘字退回 TMP 文本);
    ///  - buff 图标 Battle/Buff/&lt;id&gt;:缺则 null(字母块);
    ///  - 贴图按 Texture2D 读入后运行时 Sprite.Create 切格(与 QdaoBoySpriteAnimator 一致,不依赖 Sprite 导入设置)。
    /// </summary>
    public static class BattleArtCatalog
    {
        // ── 路径契约 ─────────────────────────────────────────
        public const string CharactersRoot = "Battle/Characters";
        public const string MonstersRoot = "Battle/Monsters";
        public const string FxRoot = "Battle/Fx";
        public const string UiRoot = "Battle/UI";
        public const string BuffRoot = "Battle/Buff";
        public const string BackgroundsRoot = "Battle/Backgrounds";
        public const string ManifestPath = "Battle/ART_MANIFEST";
        public const string DigitsMetaPath = "Battle/UI/digits_meta";
        public const string SkillPresentationPath = "Battle/skill_presentation";
        public const string AdditiveShaderPath = "Battle/Shaders/BattleUiAdditive";

        public const string PlayerWalkRoot = "World/Characters/QdaoHeadbandBoy";
        public const string DefaultCharacterId = "QdaoHeadbandBoy";

        public const string ArenaBackgroundPath = "UI/Ugui/Battle/Backgrounds/qdao_battle_arena_cloud_terrace_2560x1080_v1";
        public const string EntryLoadingPath = "UI/Ugui/Battle/Backgrounds/qdao_battle_entry_loading_2560x1080_v1";
        public const string EntryCloudsPath = "UI/Ugui/Battle/Overlays/qdao_battle_entry_clouds_fg_2560x1080_v1";
        public const string SpawnRingFriendlyPath = "UI/Ugui/Battle/Fx/qdao_battle_spawn_ring_friendly_1024_v1";
        public const string SpawnRingEnemyPath = "UI/Ugui/Battle/Fx/qdao_battle_spawn_ring_enemy_1024_v1";

        /// <summary>22 张 1024 立绘(玩家头像用:裁头部区域)。</summary>
        public const string PortraitsRoot = "UI/qdao_v3/characters";
        public static readonly string[] PortraitFiles =
        {
            "01_ice_sword_girl_v3", "02_fire_talisman_boy_v3", "03_lotus_healer_girl_v3",
            "04_mountain_guardian_boy_v3", "05_celestial_musician_girl_v3", "06_thunder_caster_boy_v3",
            "07_moon_shadow_assassin_girl_v3", "08_alchemy_prodigy_boy_v3", "09_bamboo_archer_girl_v3",
            "10_crimson_spear_girl_v3", "11_jade_fist_flat_top_boy_v3", "12_iron_saber_flat_top_boy_v3",
            "13_short_hair_wind_blade_girl_v3", "14_short_hair_snow_summoner_girl_v3", "15_water_dragon_scholar_boy_v3",
            "16_golden_bell_dancer_girl_v3", "17_ghost_script_calligrapher_boy_v3", "18_desert_sun_monk_girl_v3",
            "19_spirit_beast_tamer_boy_v3", "20_star_formation_master_girl_v3", "21_lidazui_hair_cook_boy_v3",
            "22_lidazui_hair_waiter_saber_boy_v3",
        };
        /// <summary>立绘头部裁切框(归一化,x/y 从左上量;1024 立绘的头约在 x 33%~68%、y 6%~41%)。</summary>
        public static readonly Rect PortraitHeadCrop = new Rect(0.325f, 0.06f, 0.352f, 0.352f);

        /// <summary>角色/怪物帧条的 pivot(脚底基线)。</summary>
        public static readonly Vector2 FeetPivot = new Vector2(0.5f, 0.08f);
        public const float SpritePixelsPerUnit = 100f;

        public static readonly string[] CharacterActions = { "idle", "attack", "cast", "hit", "die", "win" };

        // ── 缓存 ────────────────────────────────────────────
        private static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Sprite> s_sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, StripAnim> s_strips = new Dictionary<string, StripAnim>();
        private static readonly Dictionary<uint, Sprite> s_silhouettes = new Dictionary<uint, Sprite>();
        private static readonly Dictionary<string, DigitFont> s_digits = new Dictionary<string, DigitFont>();
        private static Dictionary<uint, SkillPresentation> s_skillTable;
        private static ArtManifest s_manifest;
        private static bool s_manifestLoaded;
        private static Material s_flashMaterial;
        private static bool s_flashMaterialTried;
        private static Sprite s_shadow;
        private static Sprite s_ring;
        private static Sprite s_circle;

        /// <summary>清空缓存(编辑器域重载/资源热替换后调用)。</summary>
        public static void ResetCaches()
        {
            s_textures.Clear();
            s_sprites.Clear();
            s_strips.Clear();
            s_silhouettes.Clear();
            s_digits.Clear();
            s_visibleBounds.Clear();
            s_skillTable = null;
            s_manifest = null;
            s_manifestLoaded = false;
            s_flashMaterial = null;
            s_flashMaterialTried = false;
            s_shadow = null;
            s_ring = null;
            s_circle = null;
        }

        // ── 朝向 / 身份 ──────────────────────────────────────

        /// <summary>阵营朝向:敌方在左上朝右下(E),我方在右下朝左上(W)。</summary>
        public static bool FacingEast(bool teamIsMine) => !teamIsMine;

        /// <summary>
        /// 22 套角色战斗帧条 id = 立绘文件名去掉 `_v3`(tools/battle_art_gen -mode characters 的产出契约,
        /// 落在 Battle/Characters/&lt;id&gt;/&lt;action&gt;_{E,W}_strip)。缺图时 LoadCharacterAction 返回 null,
        /// BattleUnitView 自动回退到 QdaoHeadbandBoy 跑步条首帧,所以这里不做存在性检查(避免开局同步 IO)。
        /// </summary>
        public static readonly string[] CharacterIds = BuildCharacterIds();

        private static string[] BuildCharacterIds()
        {
            var ids = new string[PortraitFiles.Length];
            for (int i = 0; i < PortraitFiles.Length; i++)
            {
                string name = PortraitFiles[i];
                ids[i] = name.EndsWith("_v3", StringComparison.Ordinal) ? name.Substring(0, name.Length - 3) : name;
            }
            return ids;
        }

        /// <summary>
        /// 玩家外观 id:按 actor_id 从 22 套里稳定挑一套(与头像同一分派函数,所以同一个人
        /// 的立绘头像与场上身形是同一个角色)。外观表接入后改为按 class/appearance 分派。
        /// </summary>
        public static string CharacterIdFor(BattleActorState actor)
        {
            if (actor == null || CharacterIds.Length == 0) return DefaultCharacterId;
            int index = BattleHudLogic.PortraitIndexFor(actor.ActorId, CharacterIds.Length);
            return CharacterIds[index];
        }

        /// <summary>动作缺省帧率(battle-art-prompts.md §1)。</summary>
        public static float ActionFps(string action)
        {
            switch (action)
            {
                case "idle": return 6f;
                case "attack": return 12f;
                case "cast": return 10f;
                case "hit": return 12f;
                case "die": return 8f;
                case "win": return 8f;
                default: return 10f;
            }
        }

        // ── 角色 / 怪物 ──────────────────────────────────────

        /// <summary>角色动作帧条;缺 W 用 E 镜像;都缺返回 null(调用方走程序化动作)。</summary>
        public static StripAnim LoadCharacterAction(string characterId, string action, bool facingEast)
            => LoadDirectionalStrip($"{CharactersRoot}/{characterId}/{action}", action, facingEast);

        /// <summary>怪物动作帧条;缺图返回 null(调用方用剪影)。</summary>
        public static StripAnim LoadMonsterAction(uint monsterId, string action, bool facingEast)
            => LoadDirectionalStrip($"{MonstersRoot}/{monsterId}/{action}", action, facingEast);

        /// <summary>怪物头像(行动预告条);缺图返回剪影。</summary>
        public static Sprite LoadMonsterPortrait(uint monsterId)
            => LoadSprite($"{MonstersRoot}/{monsterId}/portrait") ?? GetMonsterSilhouette(monsterId);

        /// <summary>玩家跑步条(World 资源,8 帧 512 单格):E/W 两向,缺则 null。</summary>
        public static StripAnim LoadPlayerWalk(bool facingEast)
        {
            string key = $"{PlayerWalkRoot}/walk_{(facingEast ? "E" : "W")}";
            var strip = LoadStrip(key, 0, FeetPivot, 12f);
            if (strip != null) return strip;
            // 缺一向:用另一向镜像
            string other = $"{PlayerWalkRoot}/walk_{(facingEast ? "W" : "E")}";
            var fallback = LoadStrip(other, 0, FeetPivot, 12f);
            if (fallback == null) return null;
            return new StripAnim { Frames = fallback.Frames, Fps = fallback.Fps, Pivot = fallback.Pivot, Mirrored = true };
        }

        /// <summary>玩家 idle 单帧(跑步条首帧);缺则 null。</summary>
        public static Sprite LoadPlayerIdle(bool facingEast, out bool mirrored)
        {
            var walk = LoadPlayerWalk(facingEast);
            mirrored = walk?.Mirrored ?? false;
            return walk?.Count > 0 ? walk.Frames[0] : null;
        }

        private static StripAnim LoadDirectionalStrip(string basePath, string action, bool facingEast)
        {
            float fps = ActionFps(action);
            string want = $"{basePath}_{(facingEast ? "E" : "W")}_strip";
            var strip = LoadStrip(want, 0, FeetPivot, fps);
            if (strip != null) return strip;

            string other = $"{basePath}_{(facingEast ? "W" : "E")}_strip";
            var fallback = LoadStrip(other, 0, FeetPivot, fps);
            if (fallback == null) return null;
            return new StripAnim { Frames = fallback.Frames, Fps = fallback.Fps, Pivot = fallback.Pivot, Mirrored = true };
        }

        // ── 特效 ────────────────────────────────────────────

        /// <summary>特效帧条(Battle/Fx/&lt;fxId&gt;_strip);fps/pivot 优先取 ART_MANIFEST;缺图 null。</summary>
        public static StripAnim LoadFx(string fxId)
        {
            if (string.IsNullOrEmpty(fxId)) return null;
            string path = $"{FxRoot}/{fxId}_strip";
            float fps = 12f;
            var pivot = new Vector2(0.5f, 0.5f);
            var entry = FindManifestEntry($"Fx/{fxId}_strip.png");
            if (entry != null)
            {
                if (entry.fps_hint > 0) fps = entry.fps_hint;
                if (entry.pivot != null && entry.pivot.Length >= 2) pivot = new Vector2(entry.pivot[0], entry.pivot[1]);
            }
            return LoadStrip(path, 0, pivot, fps);
        }

        /// <summary>技能 → 特效 id(表映射;缺映射按 skillId 轮换 fire_burst/ice_shard/lightning_strike;普攻 slash_arc)。</summary>
        public static string FxForSkill(uint skillId, bool isAttack = false)
            => ResolveSkillFx(skillId, isAttack).FxId;

        /// <summary>技能 → 特效/动作/命中帧;缺表按 id 启发式分派(普攻斩击,技能三系轮换)。</summary>
        public static SkillPresentation ResolveSkillFx(uint skillId, bool isAttack)
        {
            if (!isAttack)
            {
                EnsureSkillTable();
                if (s_skillTable != null && s_skillTable.TryGetValue(skillId, out var found)) return found;
            }
            if (isAttack || skillId == 0)
                return new SkillPresentation { SkillId = skillId, FxId = "slash_arc", Action = "attack", HitFrame = 3 };

            string[] cycle = { "fire_burst", "ice_shard", "lightning_strike" };
            return new SkillPresentation
            {
                SkillId = skillId,
                FxId = cycle[(int)(skillId % (uint)cycle.Length)],
                Action = "cast",
                HitFrame = 3,
            };
        }

        // ── UI / 数字 / buff ────────────────────────────────

        /// <summary>buff 图标(Battle/Buff/&lt;id&gt;);缺图 null(调用方画字母块)。</summary>
        public static Sprite LoadBuffIcon(uint buffId) => LoadSprite($"{BuffRoot}/{buffId}");

        /// <summary>数字字集(normal/crit/heal/miss);贴图或 meta 缺失返回 null。</summary>
        public static DigitFont LoadDigits(string variant)
        {
            if (string.IsNullOrEmpty(variant)) variant = "normal";
            if (s_digits.TryGetValue(variant, out var cached)) return cached;

            DigitFont font = null;
            var tex = LoadTexture($"{UiRoot}/digits_{variant}");
            if (tex != null)
            {
                int cellW = 48, cellH = 64;
                string chars = "0123456789-+闪暴击";
                var meta = LoadJson<DigitsMeta>(DigitsMetaPath);
                if (meta != null)
                {
                    if (meta.cell_width > 0) cellW = meta.cell_width;
                    if (meta.cell_height > 0) cellH = meta.cell_height;
                    if (!string.IsNullOrEmpty(meta.chars_string)) chars = meta.chars_string;
                }
                var cells = SliceCells(tex, cellW, cellH, new Vector2(0.5f, 0.5f), $"digits_{variant}");
                if (cells != null && cells.Length > 0)
                    font = new DigitFont { Cells = cells, Chars = chars, CellWidth = cellW, CellHeight = cellH };
            }
            s_digits[variant] = font;
            return font;
        }

        /// <summary>UI 底图(panel_9slice / button_9slice / bar_slot / command_ring),九宫边距读 manifest。</summary>
        public static Sprite LoadUiSprite(string name)
        {
            string key = $"{UiRoot}/{name}";
            if (s_sprites.TryGetValue(key, out var cached)) return cached;

            Sprite sprite = null;
            var tex = LoadTexture(key);
            if (tex != null)
            {
                var border = Vector4.zero;
                var entry = FindManifestEntry($"UI/{name}.png");
                if (entry?.border_9slice_lbrt != null && entry.border_9slice_lbrt.Length >= 4)
                {
                    border = new Vector4(entry.border_9slice_lbrt[0], entry.border_9slice_lbrt[1],
                        entry.border_9slice_lbrt[2], entry.border_9slice_lbrt[3]);
                }
                sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                    SpritePixelsPerUnit, 0, SpriteMeshType.FullRect, border);
                sprite.name = $"battle_ui_{name}";
            }
            s_sprites[key] = sprite;
            return sprite;
        }

        /// <summary>通用单图:先按 Sprite 读,再退回 Texture2D 整图建 Sprite;缺图 null。</summary>
        public static Sprite LoadSprite(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (s_sprites.TryGetValue(resourcePath, out var cached)) return cached;

            Sprite sprite = null;
            try
            {
                sprite = Resources.Load<Sprite>(resourcePath);
                if (sprite == null)
                {
                    var tex = LoadTexture(resourcePath);
                    if (tex != null)
                    {
                        sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f),
                            SpritePixelsPerUnit, 0, SpriteMeshType.FullRect);
                        sprite.name = resourcePath;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BattleArtCatalog] 读取 {resourcePath} 失败:{e.Message}");
                sprite = null;
            }
            s_sprites[resourcePath] = sprite;
            return sprite;
        }

        /// <summary>出生光环(目标高亮/入场用);缺图 null。</summary>
        public static Sprite LoadSpawnRing(bool teamIsMine)
            => LoadSprite(teamIsMine ? SpawnRingFriendlyPath : SpawnRingEnemyPath);

        /// <summary>入场云层前景(开场扫过);缺图 null。</summary>
        public static Sprite LoadEntryClouds() => LoadSprite(EntryCloudsPath);

        /// <summary>
        /// 玩家头像:按 actor_id 稳定挑一张 qdao_v3 立绘并裁头部(<see cref="PortraitHeadCrop"/>);
        /// 立绘缺失返回 null(调用方画首字母块)。
        /// </summary>
        public static Sprite LoadPlayerPortrait(ulong actorId)
        {
            if (PortraitFiles.Length == 0) return null;
            int index = BattleHudLogic.PortraitIndexFor(actorId, PortraitFiles.Length);
            string key = $"{PortraitsRoot}/{PortraitFiles[index]}#head";
            if (s_sprites.TryGetValue(key, out var cached)) return cached;

            Sprite sprite = null;
            var tex = LoadTexture($"{PortraitsRoot}/{PortraitFiles[index]}");
            if (tex != null)
            {
                float w = tex.width, h = tex.height;
                var crop = PortraitHeadCrop;
                // Rect 的 y 从左上量,Sprite.Create 的 rect 原点在左下
                var rect = new Rect(crop.x * w, h - (crop.y + crop.height) * h, crop.width * w, crop.height * h);
                sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), SpritePixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = $"portrait_head_{index:00}";
            }
            s_sprites[key] = sprite;
            return sprite;
        }

        /// <summary>程序化圆环(128×128,内径 78%;回合倒计时的径向填充用)。</summary>
        public static Sprite RingSprite
        {
            get
            {
                if (s_ring != null) return s_ring;
                s_ring = MakeDisc(128, 0.78f, "battle_ring");
                return s_ring;
            }
        }

        /// <summary>程序化实心圆(128×128;命令环按钮底/头像遮罩缺图时用)。</summary>
        public static Sprite CircleSprite
        {
            get
            {
                if (s_circle != null) return s_circle;
                s_circle = MakeDisc(128, 0f, "battle_circle");
                return s_circle;
            }
        }

        private static Sprite MakeDisc(int size, float innerRatio, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, r = size * 0.5f - 1f, ri = r * innerRatio;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    if (ri > 0f) a *= Mathf.Clamp01(d - ri + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), SpritePixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }

        // ── 程序化占位 ──────────────────────────────────────

        /// <summary>受击闪白用加色材质(Battle/UiAdditive);shader 缺失返回 null(调用方退回 alpha 闪)。</summary>
        public static Material FlashMaterial
        {
            get
            {
                if (s_flashMaterialTried) return s_flashMaterial;
                s_flashMaterialTried = true;
                try
                {
                    var shader = Resources.Load<Shader>(AdditiveShaderPath);
                    if (shader == null) shader = Shader.Find("Battle/UiAdditive");
                    if (shader != null && shader.isSupported)
                    {
                        s_flashMaterial = new Material(shader) { name = "BattleUiAdditive(runtime)" };
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BattleArtCatalog] 闪白材质不可用:{e.Message}");
                    s_flashMaterial = null;
                }
                return s_flashMaterial;
            }
        }

        /// <summary>脚底椭圆阴影(程序化 96×40 软边椭圆)。</summary>
        public static Sprite ShadowSprite
        {
            get
            {
                if (s_shadow != null) return s_shadow;
                const int w = 96, h = 40;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "battle_shadow", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[w * h];
                float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
                float rx = w * 0.48f, ry = h * 0.46f;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x - cx) / rx, dy = (y - cy) / ry;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01((1f - d) / 0.35f); // 边缘 35% 渐隐
                        a = a * a * (3f - 2f * a);
                        px[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                    }
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_shadow = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), SpritePixelsPerUnit, 0, SpriteMeshType.FullRect);
                s_shadow.name = "battle_shadow";
                return s_shadow;
            }
        }

        /// <summary>
        /// 怪物程序化剪影(128×160):深色轮廓 + 浅色描边 + 发光眼睛,形状按 id 哈希取变体
        /// (耳朵/犄角/体型),同 id 稳定。缺图占位,不追求美观。
        /// </summary>
        public static Sprite GetMonsterSilhouette(uint monsterId)
        {
            if (s_silhouettes.TryGetValue(monsterId, out var cached)) return cached;

            const int w = 128, h = 160;
            uint seed = monsterId * 2654435761u + 0x9E3779B9u;
            int variant = (int)((seed >> 8) % 4u);          // 0 圆胖 1 高瘦 2 尖耳 3 犄角
            int eyeVariant = (int)((seed >> 16) % 4u);
            Color32 body = new Color32(28, 24, 46, 255);
            Color32 edge = new Color32(96, 86, 140, 255);
            Color32[] eyePalette =
            {
                new Color32(255, 70, 60, 255), new Color32(255, 214, 80, 255),
                new Color32(90, 230, 255, 255), new Color32(120, 255, 130, 255),
            };
            Color32 eye = eyePalette[eyeVariant];

            float bodyCx = w * 0.5f, bodyCy = variant == 1 ? 58f : 52f;
            float bodyRx = variant == 1 ? 30f : 42f, bodyRy = variant == 1 ? 46f : 40f;
            float headCx = w * 0.5f, headCy = variant == 1 ? 118f : 110f, headR = variant == 1 ? 26f : 34f;

            var mask = new float[w * h]; // 1 = 实体
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float m = 0f;
                    m = Mathf.Max(m, Ellipse(x, y, bodyCx, bodyCy, bodyRx, bodyRy));
                    m = Mathf.Max(m, Ellipse(x, y, headCx, headCy, headR, headR));
                    // 脚
                    m = Mathf.Max(m, Ellipse(x, y, bodyCx - 22f, 16f, 16f, 10f));
                    m = Mathf.Max(m, Ellipse(x, y, bodyCx + 22f, 16f, 16f, 10f));
                    if (variant == 2)
                    {
                        // 尖耳:两只三角
                        m = Mathf.Max(m, Triangle(x, y, headCx - 30f, headCy + 14f, headCx - 6f, headCy + 26f, headCx - 24f, headCy + 46f));
                        m = Mathf.Max(m, Triangle(x, y, headCx + 30f, headCy + 14f, headCx + 6f, headCy + 26f, headCx + 24f, headCy + 46f));
                    }
                    else if (variant == 3)
                    {
                        // 犄角
                        m = Mathf.Max(m, Triangle(x, y, headCx - 26f, headCy + 20f, headCx - 12f, headCy + 24f, headCx - 30f, headCy + 48f));
                        m = Mathf.Max(m, Triangle(x, y, headCx + 26f, headCy + 20f, headCx + 12f, headCy + 24f, headCx + 30f, headCy + 48f));
                    }
                    mask[y * w + x] = m;
                }
            }

            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float m = mask[y * w + x];
                    if (m <= 0f) { px[y * w + x] = new Color32(0, 0, 0, 0); continue; }
                    // 描边:邻域 2px 内有空像素
                    bool isEdge = false;
                    for (int oy = -2; oy <= 2 && !isEdge; oy++)
                    {
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            int nx = x + ox, ny = y + oy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h || mask[ny * w + nx] <= 0f) { isEdge = true; break; }
                        }
                    }
                    px[y * w + x] = isEdge ? edge : body;
                }
            }
            // 眼睛发光
            float eyeY = headCy + 4f, eyeDx = headR * 0.42f;
            PaintGlow(px, w, h, headCx - eyeDx, eyeY, 7f, eye);
            PaintGlow(px, w, h, headCx + eyeDx, eyeY, 7f, eye);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = $"monster_silhouette_{monsterId}", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.02f), SpritePixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = tex.name;
            s_silhouettes[monsterId] = sprite;
            return sprite;
        }

        // ── 可见范围测量(名牌贴头顶用) ──────────────────────

        private static readonly Dictionary<Sprite, Rect> s_visibleBounds = new Dictionary<Sprite, Rect>();

        /// <summary>
        /// 立绘可见顶点到脚底(sprite pivot)的高度,换算到 bodyHeight(sprite 整格显示高)下的设计像素。
        /// 贴图多数不可读(isReadable=0),走 Blit → 临时 RT → ReadPixels 的 GPU 回读,按 1/4 分辨率扫 alpha,
        /// 每张 sprite 只测一次(缓存)。任何失败返回 fallback。
        /// </summary>
        public static float MeasureVisibleTop(Sprite sprite, float bodyHeight, float fallback)
        {
            if (sprite == null || bodyHeight <= 0f) return fallback;
            var bounds = MeasureVisibleBounds(sprite);
            if (bounds.height <= 0f) return fallback;
            float pivot01 = sprite.rect.height > 0f ? sprite.pivot.y / sprite.rect.height : 0f;
            float top = (bounds.yMax - pivot01) * bodyHeight;
            return top > 0f ? top : fallback;
        }

        /// <summary>
        /// sprite 内不透明像素的包围盒(归一化到 sprite rect,y 从下往上,0..1);测不到返回空 Rect。
        /// </summary>
        public static Rect MeasureVisibleBounds(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return default;
            var tex = sprite.texture;
            var r = sprite.rect;
            // 目录里的 Sprite 都是一次创建后长期缓存的,按 Sprite 引用缓存即可(同一帧条的不同帧各测各的)
            if (s_visibleBounds.TryGetValue(sprite, out var cached)) return cached;

            var result = default(Rect);
            RenderTexture rt = null;
            Texture2D read = null;
            var prevActive = RenderTexture.active;
            try
            {
                const int Down = 4;
                int w = Mathf.Max(16, Mathf.RoundToInt(r.width / Down));
                int h = Mathf.Max(16, Mathf.RoundToInt(r.height / Down));
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                var scale = new Vector2(r.width / tex.width, r.height / tex.height);
                var offset = new Vector2(r.x / tex.width, r.y / tex.height);
                Graphics.Blit(tex, rt, scale, offset);
                RenderTexture.active = rt;
                read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                read.Apply(false);
                var px = read.GetPixels32();
                int minX = w, minY = h, maxX = -1, maxY = -1;
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        if (px[row + x].a < 24) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
                if (maxX >= minX && maxY >= minY)
                {
                    result = new Rect(minX / (float)w, minY / (float)h, (maxX - minX + 1) / (float)w, (maxY - minY + 1) / (float)h);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BattleArtCatalog] 可见范围测量失败 {sprite.name}:{e.Message}");
                result = default;
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (read != null) UnityEngine.Object.Destroy(read);
            }
            s_visibleBounds[sprite] = result;
            return result;
        }

        // ── 底层:贴图读取与切格 ─────────────────────────────

        /// <summary>读 Texture2D(带 null 缓存,缺图只查一次)。</summary>
        public static Texture2D LoadTexture(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath)) return null;
            if (s_textures.TryGetValue(resourcePath, out var cached)) return cached;
            Texture2D tex = null;
            try { tex = Resources.Load<Texture2D>(resourcePath); }
            catch (Exception e) { Debug.LogWarning($"[BattleArtCatalog] 读取 {resourcePath} 失败:{e.Message}"); }
            s_textures[resourcePath] = tex;
            return tex;
        }

        /// <summary>
        /// 横条切格:cellSize=0 表示方格(边长 = 贴图高);帧数 = 宽 / 格宽。
        /// 缺图或尺寸不整除(容忍末尾余量)返回 null / 可用帧。
        /// </summary>
        public static StripAnim LoadStrip(string resourcePath, int cellSize, Vector2 pivot, float fps)
        {
            string key = $"{resourcePath}|{cellSize}|{pivot.x:0.##},{pivot.y:0.##}";
            if (s_strips.TryGetValue(key, out var cached)) return cached;

            StripAnim strip = null;
            var tex = LoadTexture(resourcePath);
            if (tex != null)
            {
                int cell = cellSize > 0 ? cellSize : tex.height;
                var frames = SliceCells(tex, cell, tex.height, pivot, resourcePath);
                if (frames != null && frames.Length > 0)
                    strip = new StripAnim { Frames = frames, Fps = fps, Pivot = pivot };
            }
            s_strips[key] = strip;
            return strip;
        }

        private static Sprite[] SliceCells(Texture2D tex, int cellW, int cellH, Vector2 pivot, string nameHint)
        {
            if (tex == null || cellW <= 0 || cellH <= 0) return null;
            int count = tex.width / cellW;
            if (count <= 0 || cellH > tex.height) return null;
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                var sprite = Sprite.Create(tex, new Rect(i * cellW, 0f, cellW, cellH), pivot,
                    SpritePixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = $"{nameHint}_{i:00}";
                frames[i] = sprite;
            }
            return frames;
        }

        // ── manifest / json ──────────────────────────────────

        [Serializable]
        private sealed class ArtManifest
        {
            public ArtEntry[] fx;
            public ArtEntry[] ui;
            public ArtEntry[] digits_files;
            public ArtEntry[] buff_files;
        }

        [Serializable]
        private sealed class ArtEntry
        {
            public string path;
            public string kind;
            public int width;
            public int height;
            public int frames;
            public int cell_width;
            public int cell_height;
            public int fps_hint;
            public float[] pivot;
            public int[] border_9slice_lbrt;
        }

        [Serializable]
        private sealed class DigitsMeta
        {
            public int cell_width;
            public int cell_height;
            public int cell_count;
            public string chars_string;
        }

        [Serializable]
        private sealed class SkillPresentationRow
        {
            public uint skill_id;
            public string fx_id;
            public string action;
            public int hit_frame;
        }

        [Serializable]
        private sealed class SkillPresentationTable
        {
            public SkillPresentationRow[] entries;
        }

        private static ArtEntry FindManifestEntry(string relativePath)
        {
            EnsureManifest();
            if (s_manifest == null) return null;
            return Find(s_manifest.fx, relativePath) ?? Find(s_manifest.ui, relativePath)
                ?? Find(s_manifest.digits_files, relativePath) ?? Find(s_manifest.buff_files, relativePath);
        }

        private static ArtEntry Find(ArtEntry[] entries, string relativePath)
        {
            if (entries == null) return null;
            foreach (var e in entries)
            {
                if (e != null && string.Equals(e.path, relativePath, StringComparison.OrdinalIgnoreCase)) return e;
            }
            return null;
        }

        private static void EnsureManifest()
        {
            if (s_manifestLoaded) return;
            s_manifestLoaded = true;
            s_manifest = LoadJson<ArtManifest>(ManifestPath);
        }

        private static void EnsureSkillTable()
        {
            if (s_skillTable != null) return;
            s_skillTable = new Dictionary<uint, SkillPresentation>();
            TextAsset text = null;
            try { text = Resources.Load<TextAsset>(SkillPresentationPath); }
            catch (Exception) { }
            if (text == null || string.IsNullOrWhiteSpace(text.text)) return;

            string json = text.text.TrimStart();
            if (json.StartsWith("[")) json = "{\"entries\":" + json + "}"; // 裸数组包一层
            try
            {
                var table = JsonUtility.FromJson<SkillPresentationTable>(json);
                if (table?.entries == null) return;
                foreach (var row in table.entries)
                {
                    if (row == null || row.skill_id == 0) continue;
                    s_skillTable[row.skill_id] = new SkillPresentation
                    {
                        SkillId = row.skill_id,
                        FxId = string.IsNullOrEmpty(row.fx_id) ? "fire_burst" : row.fx_id,
                        Action = row.action == "cast" ? "cast" : "attack",
                        HitFrame = Mathf.Max(0, row.hit_frame),
                    };
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BattleArtCatalog] skill_presentation.json 解析失败:{e.Message}");
            }
        }

        private static T LoadJson<T>(string resourcePath) where T : class
        {
            try
            {
                var text = Resources.Load<TextAsset>(resourcePath);
                if (text == null || string.IsNullOrWhiteSpace(text.text)) return null;
                return JsonUtility.FromJson<T>(text.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BattleArtCatalog] {resourcePath} 解析失败:{e.Message}");
                return null;
            }
        }

        // ── 绘图小工具 ──────────────────────────────────────

        private static float Ellipse(int x, int y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return dx * dx + dy * dy <= 1f ? 1f : 0f;
        }

        private static float Triangle(int px, int py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = Sign(px, py, ax, ay, bx, by);
            float d2 = Sign(px, py, bx, by, cx, cy);
            float d3 = Sign(px, py, cx, cy, ax, ay);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos) ? 1f : 0f;
        }

        private static float Sign(float px, float py, float ax, float ay, float bx, float by)
            => (px - bx) * (ay - by) - (ax - bx) * (py - by);

        private static void PaintGlow(Color32[] px, int w, int h, float cx, float cy, float radius, Color32 color)
        {
            int x0 = Mathf.Max(0, (int)(cx - radius - 1)), x1 = Mathf.Min(w - 1, (int)(cx + radius + 1));
            int y0 = Mathf.Max(0, (int)(cy - radius - 1)), y1 = Mathf.Min(h - 1, (int)(cy + radius + 1));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                    if (d > 1f) continue;
                    float core = Mathf.Clamp01(1f - d / 0.45f);       // 白芯
                    float glow = 1f - d;                                 // 外晕
                    var c = px[y * w + x];
                    var mixed = Color32.Lerp(c, color, glow);
                    mixed = Color32.Lerp(mixed, new Color32(255, 255, 255, 255), core);
                    mixed.a = 255;
                    px[y * w + x] = mixed;
                }
            }
        }
    }
}
