using System;
using System.Collections.Generic;
using Google.Protobuf;
using MmorpgClient.Game.Appearance;
using MmorpgClient.Game.Battle;
using MmorpgClient.Game.Jubaozhai;
using MmorpgClient.Net;
using NUnit.Framework;
using Trade;

namespace MmorpgClient.Tests.EditMode.Role
{
    public sealed class WardrobeCosmeticsTests
    {
        private sealed class FakeTransport : IBattleTransport
        {
            public ulong PlayerId { get; set; } = 91001;
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

        private sealed class MemoryRecoveryStore : ICommerceRecoveryStore
        {
            private readonly Dictionary<ulong, string> _data = new();
            public string Load(ulong player) => _data.TryGetValue(player, out var v) ? v : "";
            public void Save(ulong player, string value) => _data[player] = value;
            public void Clear(ulong player, string expected)
            {
                if (_data.TryGetValue(player, out var cur) && cur == expected) _data.Remove(player);
            }
        }

        private static readonly AppearanceMessageIds Ids = new AppearanceMessageIds(442, 443, 444, 445, 447, 530, 531);

        [Test]
        public void Models_PetRowsEnforceLiveSkuGatingSpeciesFilterAndOwnershipStates()
        {
            var info = new PlayerAppearanceInfo();
            info.PetCosmetics.Add(new PetCosmeticEntry
            {
                ConfigId = 1,
                Name = "苍嶂麟·墨玉",
                ArtKey = "07-cangzhanglin",
                VisualPetTableId = 12,
                TargetPetTableId = 0,
                MallSkuId = 0,
                AcquireHint = "暂未上架",
                Owned = true,
            });
            info.PetCosmetics.Add(new PetCosmeticEntry
            {
                ConfigId = 2,
                Name = "绯耳灵·丹霞",
                ArtKey = "14-feierling",
                VisualPetTableId = 18,
                TargetPetTableId = 18,
                MallSkuId = 8801,
                AcquireHint = "前往官方商城「永久衣柜」获取",
                Owned = false,
            });
            info.PetCosmetics.Add(new PetCosmeticEntry
            {
                ConfigId = 3,
                Name = "月弦师·素月",
                ArtKey = "12-yuexianshi",
                VisualPetTableId = 16,
                TargetPetTableId = 0,
                MallSkuId = 8802,
                AcquireHint = "暂未上架",
                Owned = false,
            });
            info.EquippedPetCosmetics.Add(new PetEquippedCosmetic { PetId = 7001, CosmeticId = 1 });

            var petA = new WardrobePetTargetOption(7001, 12, 1012, "灵宠甲", "07-cangzhanglin", 1);
            var rows = WardrobeCosmeticModels.BuildPetRows(info, petA, skuId => skuId == 8801ul);

            Assert.That(rows.Count, Is.EqualTo(4));
            Assert.That(rows[0].IsBaseDefault, Is.True);
            Assert.That(rows[0].State, Is.EqualTo(WardrobeCosmeticRowState.Owned));
            Assert.That(rows[1].ConfigId, Is.EqualTo(1u));
            Assert.That(rows[1].State, Is.EqualTo(WardrobeCosmeticRowState.Equipped));

            // 8801 有真实在售 SKU -> 可跳转永久衣柜；8802 无在售 SKU -> 不可伪造购买按钮
            Assert.That(rows[2].State, Is.EqualTo(WardrobeCosmeticRowState.LockedPurchasable));
            Assert.That(rows[3].State, Is.EqualTo(WardrobeCosmeticRowState.LockedNotListed));

            var actEquipped = WardrobeCosmeticModels.ResolvePetPrimaryAction(rows[1], petA, canSubmit: true, actionPending: false, corePreviewOnly: false);
            Assert.That(actEquipped.Kind, Is.EqualTo(WardrobePrimaryActionKind.RestoreDefault));
            Assert.That(actEquipped.Label, Is.EqualTo("卸下幻化"));
            Assert.That(actEquipped.Interactable, Is.True);

            var actBase = WardrobeCosmeticModels.ResolvePetPrimaryAction(rows[0], petA, canSubmit: true, actionPending: false, corePreviewOnly: false);
            Assert.That(actBase.Kind, Is.EqualTo(WardrobePrimaryActionKind.RestoreDefault));
            Assert.That(actBase.Label, Is.EqualTo("恢复原形"));

            var actBuyable = WardrobeCosmeticModels.ResolvePetPrimaryAction(rows[2], petA, canSubmit: true, actionPending: false, corePreviewOnly: false);
            Assert.That(actBuyable.Kind, Is.EqualTo(WardrobePrimaryActionKind.OpenPermanentWardrobeMall));
            Assert.That(actBuyable.Interactable, Is.True);

            var actUnlisted = WardrobeCosmeticModels.ResolvePetPrimaryAction(rows[3], petA, canSubmit: true, actionPending: false, corePreviewOnly: false);
            Assert.That(actUnlisted.Kind, Is.EqualTo(WardrobePrimaryActionKind.Unavailable));
            Assert.That(actUnlisted.Interactable, Is.False);

            // 种类不匹配（仅支持 pet_table_id=18，当前灵宠为 12）
            info.PetCosmetics[1].Owned = true;
            var rowsOwnedMismatch = WardrobeCosmeticModels.BuildPetRows(info, petA, _ => false);
            var actMismatch = WardrobeCosmeticModels.ResolvePetPrimaryAction(rowsOwnedMismatch[2], petA, canSubmit: true, actionPending: false, corePreviewOnly: false);
            Assert.That(actMismatch.Kind, Is.EqualTo(WardrobePrimaryActionKind.SpeciesMismatch));
            Assert.That(actMismatch.Interactable, Is.False);

            // 首测预览模式下禁止跳转商城或穿戴
            var actCorePreview = WardrobeCosmeticModels.ResolvePetPrimaryAction(rows[2], petA, canSubmit: true, actionPending: false, corePreviewOnly: true);
            Assert.That(actCorePreview.Interactable, Is.False);
        }

        [Test]
        public void Models_SkillRowsTintParsingAndTabStateLifecycle()
        {
            Assert.That(WardrobeCosmeticModels.TryParseTintHex("#FFE28A", out float r, out float g, out float b), Is.True);
            Assert.That(r, Is.EqualTo(1f).Within(0.01f));
            Assert.That(g, Is.EqualTo(226f / 255f).Within(0.01f));
            Assert.That(b, Is.EqualTo(138f / 255f).Within(0.01f));
            Assert.That(WardrobeCosmeticModels.TryParseTintHex("bad", out _, out _, out _), Is.False);
            Assert.That(WardrobeCosmeticModels.TryParseTintHex("", out _, out _, out _), Is.False);

            var info = new PlayerAppearanceInfo();
            info.SkillVfxCosmetics.Add(new SkillVfxCosmeticEntry
            {
                ConfigId = 1,
                SkillTableId = 1201,
                Name = "鎏金符纹",
                FxId = "xuanfu_luoyao_impact",
                TintHex = "#FFE28A",
                Owned = true,
            });
            info.SkillVfxCosmetics.Add(new SkillVfxCosmeticEntry
            {
                ConfigId = 2,
                SkillTableId = 1202,
                Name = "紫霄惊雷",
                FxId = "zixiao_leizhen_strike",
                TintHex = "#D2A8FF",
                MallSkuId = 9001,
                Owned = false,
            });
            info.EquippedSkillVfx[1201] = 1;

            var allRows = WardrobeCosmeticModels.BuildSkillEffectRows(info, 0u, sku => sku == 9001ul);
            Assert.That(allRows.Count, Is.EqualTo(3));
            Assert.That(allRows[1].State, Is.EqualTo(WardrobeCosmeticRowState.Equipped));
            Assert.That(allRows[2].State, Is.EqualTo(WardrobeCosmeticRowState.LockedPurchasable));

            var filtered = WardrobeCosmeticModels.BuildSkillEffectRows(info, 1202u, _ => false);
            Assert.That(filtered.Count, Is.EqualTo(2));
            Assert.That(filtered[0].IsBaseDefault, Is.True);
            Assert.That(filtered[0].State, Is.EqualTo(WardrobeCosmeticRowState.Equipped));
            Assert.That(filtered[1].ConfigId, Is.EqualTo(2u));
            Assert.That(filtered[1].State, Is.EqualTo(WardrobeCosmeticRowState.LockedNotListed));

            var state = new WardrobeCosmeticTabState();
            state.OpenContextual(WardrobeCategoryTab.Pet, targetPetId: 7002ul);
            Assert.That(state.ActiveCategory, Is.EqualTo(WardrobeCategoryTab.Pet));
            Assert.That(state.SelectedPetId, Is.EqualTo(7002ul));
            state.ToggleCompareOriginalPet();
            Assert.That(state.CompareOriginalPet, Is.True);
            state.SelectRow(WardrobeCategoryTab.Pet, 1u, 0.42f, openCompactDetail: true);
            Assert.That(state.CompareOriginalPet, Is.False);
            Assert.That(state.CompactDetailOpen, Is.True);
            Assert.That(state.SavedScroll(WardrobeCategoryTab.Pet), Is.EqualTo(0.42f).Within(0.001f));

            state.ResetForClose();
            Assert.That(state.CompactDetailOpen, Is.False);
            Assert.That(state.ActiveCategory, Is.EqualTo(WardrobeCategoryTab.Pet));

            state.ResetForRoleChangeOrDisconnect();
            Assert.That(state.ActiveCategory, Is.EqualTo(WardrobeCategoryTab.Character));
            Assert.That(state.SelectedPetId, Is.EqualTo(0ul));
        }

        [Test]
        public void Client_EquipPetAndSkillCosmeticHandleHappyPathInFlightLockTimeoutAndRoleSwitch()
        {
            var net = new FakeTransport();
            object conn = new object();
            float now = 100f;
            int equipPetCalls = 0;

            net.Responder = (msgId, req) =>
            {
                if (msgId == Ids.EquipPetCosmetic)
                {
                    equipPetCalls++;
                    var r = (EquipPetCosmeticRequest)req;
                    if (equipPetCalls == 1)
                    {
                        var info = new PlayerAppearanceInfo();
                        if (r.CosmeticId != 0)
                            info.EquippedPetCosmetics.Add(new PetEquippedCosmetic { PetId = r.PetId, CosmeticId = r.CosmeticId });
                        return (new EquipPetCosmeticResponse
                        {
                            ErrorMessage = new TipInfoMessage { Id = 0 },
                            Info = info,
                        }, null);
                    }
                    if (equipPetCalls == 2)
                        return (null, OutstandingCalls.TimeoutDetail);
                    return (new EquipPetCosmeticResponse
                    {
                        ErrorMessage = new TipInfoMessage { Id = (uint)pet_error.KPetNotFound },
                    }, null);
                }
                if (msgId == Ids.EquipSkillVfx)
                {
                    var r = (EquipSkillVfxRequest)req;
                    var info = new PlayerAppearanceInfo();
                    if (r.CosmeticId != 0) info.EquippedSkillVfx[r.SkillTableId] = r.CosmeticId;
                    return (new EquipSkillVfxResponse
                    {
                        ErrorMessage = new TipInfoMessage { Id = 0 },
                        Info = info,
                    }, null);
                }
                return (new GetAppearanceInfoResponse { Info = new PlayerAppearanceInfo() }, null);
            };

            using var client = new AppearanceClient(net, () => conn, () => now, Ids);
            Assert.That(client.IsCosmeticsSupported, Is.True);

            // 1. 穿戴灵宠幻化 + 在途锁拒绝二次并发请求
            Assert.That(client.EquipPetCosmetic(7001ul, 1u), Is.True);
            Assert.That(client.ActionPending, Is.True);
            Assert.That(client.EquipSkillVfx(1201u, 1u), Is.False, "在途锁生效时应拒绝并发穿戴请求");
            net.Flush();
            Assert.That(client.ActionPending, Is.False);
            Assert.That(client.StatusIsError, Is.False);
            Assert.That(client.Info.EquippedPetCosmetics.Count, Is.EqualTo(1));
            Assert.That(client.Info.EquippedPetCosmetics[0].CosmeticId, Is.EqualTo(1u));

            // 2. 超时应自动触发刷新而非伪造穿戴结果
            int callsBeforeTimeout = net.Calls.Count;
            Assert.That(client.EquipPetCosmetic(7001ul, 0u), Is.True);
            net.Flush();
            Assert.That(client.StatusIsError, Is.True);
            Assert.That(net.Calls.Count, Is.EqualTo(callsBeforeTimeout + 2));
            Assert.That(net.Calls[net.Calls.Count - 1].msgId, Is.EqualTo(Ids.Get));

            // 3. 服务端返回 KPetNotFound 时显示中文提示并触发同步
            Assert.That(client.EquipPetCosmetic(9999ul, 1u), Is.True);
            net.Flush();
            Assert.That(client.StatusIsError, Is.True);
            Assert.That(client.Status, Does.Contain("不存在"));

            // 4. 穿戴技能特效与恢复默认
            Assert.That(client.EquipSkillVfx(1201u, 1u), Is.True);
            net.Flush();
            Assert.That(client.Info.EquippedSkillVfx[1201u], Is.EqualTo(1u));
            Assert.That(client.EquipSkillVfx(1201u, 0u), Is.True);
            net.Flush();
            Assert.That(client.Info.EquippedSkillVfx.ContainsKey(1201u), Is.False);

            // 5. 换角后迟到回包不得污染新角色状态
            Assert.That(client.EquipSkillVfx(1201u, 1u), Is.True);
            net.PlayerId = 99999ul;
            net.Flush();
            Assert.That(client.Info, Is.Null, "切换角色后旧角色的穿戴回包不得污染当前状态");
        }

        [Test]
        public void CommerceClient_TracksLiveSellableOfficialSkusAndClearsOnDisconnectOrRoleSwitch()
        {
            var net = new FakeTransport();
            object conn = new object();
            net.Responder = (_, _) =>
            {
                var resp = new ListOfficialSkusResponse();
                resp.Skus.Add(new OfficialSkuView { SkuId = 8801ul, Available = 5 });
                resp.Skus.Add(new OfficialSkuView { SkuId = 8802ul, Available = 0 });
                return (resp, null);
            };

            using var commerce = new CommerceClient(net, new MemoryRecoveryStore(), () => conn);
            Assert.That(commerce.HasSellableOfficialSku(8801ul), Is.False);
            commerce.ListSkus();
            net.Flush();

            Assert.That(commerce.HasSellableOfficialSku(8801ul), Is.True);
            Assert.That(commerce.HasSellableOfficialSku(8802ul), Is.False, "库存为 0 的 SKU 不视为可售");
            Assert.That(commerce.HasSellableOfficialSku(0ul), Is.False);

            // 换角色清空官方商城 SKU 缓存
            net.PlayerId = 91002ul;
            commerce.ObserveConnection();
            Assert.That(commerce.HasSellableOfficialSku(8801ul), Is.False);

            // 重新拉取后断线也必须清空
            commerce.ListSkus();
            net.Flush();
            Assert.That(commerce.HasSellableOfficialSku(8801ul), Is.True);
            net.TriggerDisconnect();
            Assert.That(commerce.HasSellableOfficialSku(8801ul), Is.False);
        }
    }
}
