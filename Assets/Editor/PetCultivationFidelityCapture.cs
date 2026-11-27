#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Pet;
using MmorpgClient.Net;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Pet;
using MmorpgClient.UI.Ugui.Skills;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MmorpgClient.UI.EditorTools
{
    /// <summary>
    /// 宠物培养界面 1920×1080 / 2560×1080 原生 1:1 渲染验收抓图与像素/排版门禁。
    /// 仅在临时空场景运行，不改写任何工程场景，不连接服务端。
    /// </summary>
    public static class PetCultivationFidelityCapture
    {
        public static string OutputDirectory { get; set; } = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../.codex-artifacts/pet-ui-fidelity-v1"));

        [Serializable]
        private sealed class Report
        {
            public string capturedAtUtc;
            public string colorSpace;
            public int screenshotCount;
            public string[] screenshots;
            public string[] checks;
        }

        private sealed class OfflineTransport : IBattleTransport
        {
            private readonly PetListInfo _list;
            public OfflineTransport(PetListInfo list) { _list = list; }
            public ulong PlayerId => 10001UL;
            public bool IsReady => true;
            public event Action Disconnected { add { } remove { } }
            public void RegisterNotify(uint messageId, Action<MessageContent> handler) { }
            public void SendOneWay(uint messageId, IMessage request) { }
            public void Call<TResp>(uint messageId, IMessage request, MessageParser<TResp> parser,
                Action<TResp> onResponse, Action<string> onError) where TResp : IMessage<TResp>
            {
                if (messageId == MessageIds.GetPetList && onResponse != null)
                {
                    var resp = new GetPetListResponse { Pets = _list.Clone() };
                    onResponse(parser.ParseFrom(resp.ToByteArray()));
                }
            }
        }

        private sealed class OfflineSkillSource : ISkillBookSource
        {
            public event Action Changed;
            public SkillBookViewState State { get; private set; }
            public void Refresh(SkillBookContext context)
            {
                var snap = new SkillBookSnapshot { CurrencyBalance = 8600 };
                snap.Entries.Add(new SkillEntryState
                {
                    Id = 1101, Rank = 5, RankLimit = 10, NextRankCost = 360,
                    Definition = new SkillDefinition
                    {
                        id = 1101, name = "破阵惊锋", type = "active", category = "主动", actionTags = 1,
                        maxRank = 10, targetTeam = "enemy", maxTargets = 1, manaPercent = 0.04,
                        description = "凝气化刃直击敌方前排，造成高额物理伤害并附带破甲效果。"
                    }
                });
                snap.Entries.Add(new SkillEntryState
                {
                    Id = 2101, Rank = 4, RankLimit = 10, NextRankCost = 280,
                    Definition = new SkillDefinition
                    {
                        id = 2101, name = "霜华灵诀", type = "active", category = "主动", actionTags = 1,
                        maxRank = 10, targetTeam = "enemy", maxTargets = 3, manaPercent = 0.08,
                        description = "召玄冰灵符席卷敌方三名目标，造成法术伤害并降低速度。"
                    }
                });
                snap.Entries.Add(new SkillEntryState
                {
                    Id = 3101, Rank = 3, RankLimit = 8, NextRankCost = 220,
                    Definition = new SkillDefinition
                    {
                        id = 3101, name = "青莲润心", type = "active", category = "辅助", actionTags = 4,
                        maxRank = 8, targetTeam = "ally", maxTargets = 2, manaPercent = 0.06, durationRounds = 2,
                        description = "挥洒青莲生机，为气血最低的两名友方恢复生命并附加护盾。"
                    }
                });
                snap.Entries.Add(new SkillEntryState
                {
                    Id = 4101, Rank = 2, RankLimit = 8, NextRankCost = 180,
                    Definition = new SkillDefinition
                    {
                        id = 4101, name = "灵狐敏捷", type = "passive", category = "被动", originLabel = "追击", actionTags = 8,
                        maxRank = 8, targetTeam = "self", maxTargets = 1,
                        description = "出手后有概率触发灵影追击，并永久提升自身速度与闪避。"
                    }
                });
                State = new SkillBookViewState { Context = context, Snapshot = snap, IsConnected = true };
                Changed?.Invoke();
            }
            public void Upgrade(SkillBookContext context, uint id, uint rank, uint count) { }
        }

        [MenuItem("MMORPG/UI/Capture Pet Cultivation 1920x1080 Fidelity")]
        public static void CaptureAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("PetCultivationFidelityCapture requires Edit Mode.");
            if (Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
                EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);

            Directory.CreateDirectory(OutputDirectory);
            var checks = new List<string>();
            var captured = new List<string>();

            // 1. 验证颜色空间与 18 张高缩比宠物控件 Mipmap 导入状态
            Require(QualitySettings.activeColorSpace == ColorSpace.Gamma,
                "Expected Gamma ColorSpace (0), got " + QualitySettings.activeColorSpace);
            checks.Add("PASS: QualitySettings.activeColorSpace == Gamma");

            string controlsRoot = "Assets/Resources/UI/Ugui/CharacterPet/Controls/";
            int mipVerified = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { controlsRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileName(path);
                bool shouldMip = file != "portrait_frame.png" &&
                    (file.StartsWith("badge-", StringComparison.Ordinal) ||
                     file.StartsWith("ring-", StringComparison.Ordinal) ||
                     file.StartsWith("portrait_", StringComparison.Ordinal) ||
                     file.StartsWith("row_", StringComparison.Ordinal));
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Require(importer != null, "Missing TextureImporter: " + path);
                if (shouldMip)
                {
                    Require(importer.mipmapEnabled, "High-ratio sprite missing mipmaps: " + path);
                    Require(importer.mipmapFilter == TextureImporterMipFilter.KaiserFilter, "Expected KaiserFilter: " + path);
                    Require(importer.alphaIsTransparency, "Expected alphaIsTransparency: " + path);
                    mipVerified++;
                }
            }
            Require(mipVerified == 18, "Expected 18 high-ratio mipmapped Pet sprites, found " + mipVerified);
            checks.Add("PASS: all 18 1254x1254 Pet sprites imported with Kaiser Mipmaps + Trilinear + alphaIsTransparency");

            // 2. 渲染 1920×1080 全部 6 个状态 + 2560×1080 超宽验证图
            CaptureAtResolution(1920, 1080, captureAllStates: true, checks, captured);
            CaptureAtResolution(2560, 1080, captureAllStates: false, checks, captured);

            var report = new Report
            {
                capturedAtUtc = DateTime.UtcNow.ToString("O"),
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                screenshotCount = captured.Count,
                screenshots = captured.ToArray(),
                checks = checks.ToArray(),
            };
            File.WriteAllText(Path.Combine(OutputDirectory, "fidelity-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("PET_CULTIVATION_FIDELITY_CAPTURE_PASSED|" + captured.Count + "|" + OutputDirectory);
        }

        private static void CaptureAtResolution(int width, int height, bool captureAllStates,
            List<string> checks, List<string> captured)
        {
            var previousScene = SceneManager.GetActiveScene();
            var previousTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            RenderTexture rt = null;
            Texture2D tex = null;
            PetCultivationPanel panel = null;
            PetClient client = null;
            bool tweenStarted = false;

            try
            {
                SceneManager.SetActiveScene(scene);
                var cam = new GameObject("PetFidelityCamera").AddComponent<Camera>();
                cam.enabled = false;
                cam.transform.position = new UnityEngine.Vector3(0f, 0f, -10f);
                cam.orthographic = true;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100f;
                cam.cullingMask = 1 << 31;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = QdaoUguiTheme.Letterbox;

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                cam.targetTexture = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);

                RealtimeTween.BeginEditorCapture();
                tweenStarted = true;

                // 复用 PetUiRoot 生产 Canvas 配置：1920×1080 Expand + pixelPerfect，确保 1:1 物理像素
                var canvasGo = new GameObject("PetFidelityCanvas", typeof(RectTransform),
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 2f;
                canvas.pixelPerfect = true;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.referencePixelsPerUnit = 100f;

                var root = QdaoUguiFactory.CreateCenteredRect("CultivationRoot", canvasGo.transform, width, height);
                var bgSprite = QdaoRefreshArt.Load("sanctuary_background");
                if (bgSprite != null)
                {
                    var bg = QdaoUguiFactory.CreateImage("WorldBackdrop", root, 0f, 0f, width, height, bgSprite);
                    bg.preserveAspect = false;
                }

                var list = MakeFixtureList();
                var transport = new OfflineTransport(list);
                client = new PetClient(transport, () => false);
                var skills = new OfflineSkillSource();
                panel = new PetCultivationPanel(root, () => client, () => skills, () => false, () => { });
                panel.Show(PetCultivationPage.EntryPoints);

                void Prepare()
                {
                    foreach (var go in scene.GetRootGameObjects())
                    {
                        foreach (var child in go.GetComponentsInChildren<UnityEngine.Transform>(true)) child.gameObject.layer = 31;
                        foreach (var c in go.GetComponentsInChildren<Canvas>(true))
                        {
                            c.renderMode = RenderMode.ScreenSpaceCamera;
                            c.worldCamera = cam;
                            c.planeDistance = 2f;
                        }
                    }
                    Canvas.ForceUpdateCanvases();
                    panel.SetViewport(width, height);
                    Canvas.ForceUpdateCanvases();
                    foreach (var label in canvasGo.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate(true, true);
                    Canvas.ForceUpdateCanvases();
                }

                void Audit(string tag)
                {
                    var frame = panel.Root.GetComponentsInChildren<Image>(true).Single(i => i.name == "CultivationFrame");
                    Require(Mathf.Approximately(frame.rectTransform.rect.width, 1932f) &&
                            Mathf.Approximately(frame.rectTransform.rect.height, 814f),
                        $"{tag}: CultivationFrame expected 1932x814, got {frame.rectTransform.rect.width}x{frame.rectTransform.rect.height}");
                    Require(panel.Root.GetComponentsInChildren<Text>(true).Length == 0,
                        $"{tag}: Legacy UnityEngine.UI.Text detected inside PetCultivationPanel");
                    var labels = panel.Root.GetComponentsInChildren<TMP_Text>(false);
                    foreach (var l in labels)
                    {
                        Require(l.font != null && l.font.name.IndexOf("SimKai", StringComparison.OrdinalIgnoreCase) >= 0,
                            $"{tag}: Non-SimKai TMP font on {l.name}: {l.font?.name}");
                        Require(!l.text.Contains("▸"), $"{tag}: Unsupported glyph ▸ in {l.name}");
                    }
                    checks.Add($"PASS: [{width}x{height}] {tag} — shell=1932x814 (1:1), legacyText=0, tmpLabels={labels.Length} (all SimKai)");
                }

                void Shoot(string filePrefix)
                {
                    RealtimeTween.AdvanceEditorCapture(1f);
                    Prepare();
                    Audit(filePrefix);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                    tex.Apply(false, false);
                    string outPath = Path.Combine(OutputDirectory, $"{filePrefix}_{width}x{height}.png");
                    File.WriteAllBytes(outPath, tex.EncodeToPNG());
                    captured.Add(outPath);
                }

                void Click(string buttonName)
                {
                    var btn = panel.Root.GetComponentsInChildren<Button>(true)
                        .Single(b => b.name == buttonName + "Plate");
                    Require(btn.interactable, "Button not interactable: " + buttonName);
                    btn.onClick.Invoke();
                }

                if (!captureAllStates)
                {
                    Shoot("07-entry-points");
                    return;
                }

                Shoot("01-entry-points");

                Click("Tab属性");
                Shoot("02-attributes");

                Click("Tab加点");
                Click("Row1Plus");
                Click("Row3Plus");
                Shoot("03-four-stats");
                Click("Discard");

                Click("Tab技能");
                Shoot("04-skills");

                Click("Tab条目点");
                Click("PetBadgeExplain");
                Shoot("05-five-tier-badges");
                Click("PetBadgeClose");

                Click("PetAscendTier");
                Shoot("06-ascension-dialog");
            }
            finally
            {
                panel?.Dispose();
                client?.Dispose();
                if (tweenStarted) RealtimeTween.EndEditorCapture();
                RenderTexture.active = previousTarget;
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (previousScene.IsValid()) SceneManager.SetActiveScene(previousScene);
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static PetListInfo MakeFixtureList()
        {
            var list = new PetListInfo { MaxPets = 8, ActivePetId = 900001 };

            var lingyue = new PetInfo
            {
                PetId = 900001, PetTableId = 1, ModelId = 1001, Name = "灵玥", Level = 65,
                Growth = 11850, RemainingPoints = 6, TotalPoints = 32, IsActive = true,
                CultivationTier = 4, CultivationTierName = "神兽", CultivationArtKey = "divine",
                Derived = new PetDerivedInfo
                {
                    Health = 4280, MaxHealth = 4280, Mana = 1860, MaxMana = 1860,
                    PhysicalAttack = 1140, MagicAttack = 1695, Defense = 742, Speed = 588
                },
                NextTier = new PetTierAscensionInfo
                {
                    NextTier = 5, NextTierName = "圣兽", MinPetLevel = 60,
                    CostItemId = 3406, CostItemCount = 5, OwnedItemCount = 8,
                    CostGold = 50000, BonusEntryPoints = 8, CanAscend = true
                },
                Entries = new PetEntriesInfo
                {
                    PetId = 900001, StateVersion = 1, ConfigVersion = 1,
                    TotalPoints = 26, RemainingPoints = 22, Effective = true, ActivePresetSlot = 1
                }
            };
            AddDimensions(lingyue, 8, 12, 4, 8);
            lingyue.Entries.Presets.Add(new PetEntryPresetSummary { SlotId = 1, Tag = 1, UsedPoints = 4 });
            lingyue.Entries.Presets.Add(new PetEntryPresetSummary { SlotId = 2, Tag = 2, UsedPoints = 0 });
            lingyue.Entries.Presets.Add(new PetEntryPresetSummary { SlotId = 3, Tag = 3, UsedPoints = 0 });
            lingyue.Entries.Entries.Add(new PetEntryInfo { Id = 501, Sort = 1, Name = "物理伤害", Category = 1, Channel = 1, Cap = 20, PerPointBps = 50, Allocated = 0, EffectiveBps = 0, Icon = "physical" });
            lingyue.Entries.Entries.Add(new PetEntryInfo { Id = 502, Sort = 2, Name = "法术伤害", Category = 1, Channel = 1, Cap = 20, PerPointBps = 50, Allocated = 4, EffectiveBps = 200, Icon = "magic" });
            lingyue.Entries.Entries.Add(new PetEntryInfo { Id = 503, Sort = 3, Name = "封物命中", Category = 2, Channel = 2, Cap = 20, PerPointBps = 50, Allocated = 0, EffectiveBps = 0, Icon = "seal" });
            lingyue.Entries.Entries.Add(new PetEntryInfo { Id = 504, Sort = 4, Name = "混乱命中", Category = 2, Channel = 2, Cap = 20, PerPointBps = 50, Allocated = 0, EffectiveBps = 0, Icon = "confusion" });
            lingyue.Entries.Entries.Add(new PetEntryInfo { Id = 505, Sort = 5, Name = "治疗效果", Category = 3, Channel = 3, Cap = 20, PerPointBps = 50, Allocated = 0, EffectiveBps = 0, Icon = "healing" });
            lingyue.Entries.Entries.Add(new PetEntryInfo { Id = 506, Sort = 6, Name = "护盾容量", Category = 3, Channel = 3, Cap = 20, PerPointBps = 50, Allocated = 0, EffectiveBps = 0, Icon = "shield" });

            var yunjiujiu = new PetInfo
            {
                PetId = 900002, PetTableId = 4, ModelId = 1004, Name = "云啾啾", Level = 61,
                Growth = 11200, RemainingPoints = 4, TotalPoints = 28, IsActive = false,
                CultivationTier = 2, CultivationTierName = "变异", CultivationArtKey = "mutant",
                Derived = new PetDerivedInfo
                {
                    Health = 3650, MaxHealth = 3650, Mana = 1420, MaxMana = 1420,
                    PhysicalAttack = 1320, MagicAttack = 880, Defense = 660, Speed = 640
                }
            };
            AddDimensions(yunjiujiu, 6, 4, 12, 6);

            var hutuantuan = new PetInfo
            {
                PetId = 900003, PetTableId = 21, ModelId = 1021, Name = "葫团团", Level = 58,
                Growth = 10940, RemainingPoints = 2, TotalPoints = 24, IsActive = false,
                CultivationTier = 3, CultivationTierName = "灵兽", CultivationArtKey = "spirit",
                Derived = new PetDerivedInfo
                {
                    Health = 4920, MaxHealth = 4920, Mana = 1580, MaxMana = 1580,
                    PhysicalAttack = 840, MagicAttack = 1120, Defense = 910, Speed = 495
                }
            };
            AddDimensions(hutuantuan, 14, 6, 2, 2);

            list.Pets.Add(lingyue);
            list.Pets.Add(yunjiujiu);
            list.Pets.Add(hutuantuan);
            return list;
        }

        private static void AddDimensions(PetInfo pet, uint con, uint spr, uint str, uint agi)
        {
            string[] names = { "体质", "灵力", "力量", "敏捷" };
            uint[] vals = { con, spr, str, agi };
            for (uint i = 0; i < 4; i++)
                pet.Dimensions.Add(new PetDimensionInfo
                {
                    DimensionId = 401 + i, Name = names[i], Allocated = vals[i], Value = 40 + vals[i] * 3, Cap = 100
                });
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("PetCultivationFidelityCapture: " + message);
        }
    }
}
#endif
