using System;
using System.Collections.Generic;
using System.Linq;
using MmorpgClient.Game.Attribute;
using MmorpgClient.Game.PlayerFeatures;
using MmorpgClient.Game.Season;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.EquipmentUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    public enum EquipmentPage { Overview, Craft, Enhance, Artifacts, Polish }

    /// <summary>Live equipment UI. Painted screens are references only; every value comes from a server snapshot.</summary>
    public sealed partial class EquipmentWindow : IDisposable
    {
        private enum Modal { None, Compare, Affix, Detail, Materials, PolishOdds, Reforge, BindConfirm, EnhanceBindConfirm, UnsealConfirm, InheritEnhance, UpgradeTier }
        private readonly RectTransform _root, _frame, _surface, _body, _modalLayer;
        private readonly CanvasGroup _surfaceInput;
        private readonly EquipWishDialogInput _input;
        private readonly EquipmentCraftUiState _craft = new EquipmentCraftUiState();
        private readonly EquipForgeUiState _forge = new EquipForgeUiState();
        private readonly EquipForgeDialog _legacyForge;
        private PlayerFeaturesClient _client;
        private AttributeClient _attributes;
        private SeasonClient _season;
        private AttributePanelInfo _panel;
        private EquipmentPage _page;
        private Modal _modal;
        private uint? _targetSlot;
        private ulong _selectedItem, _candidate, _affixItem;
        private uint _affixSlot, _recipeKind = 11, _recipeConfig, _artifactConfig;
        private string _notice = string.Empty;
        private bool _rendering, _renderAgain, _disposed;
        private GameObject _returnFocus;
        private readonly Dictionary<string, float> _scroll = new Dictionary<string, float>();
        public event Action Closed;
        public bool IsVisible => !_disposed && _root != null && _root.gameObject.activeSelf;
        public RectTransform Root => _root;

        public EquipmentWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("EquipmentWindow", parent, Vector4.zero);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .08f, .065f, .76f); shade.raycastTarget = true;
            _root.gameObject.AddComponent<MmorpgClient.World.Tianyong.GameplayInputBlocker>();
            _frame = QdaoUguiFactory.CreateCenteredRect("EquipmentComposition", _root, Width, Height);
            _frame.gameObject.AddComponent<EquipmentUiFit>();
            _surface = QdaoUguiFactory.CreateStretch("EquipmentSurface", _frame, Vector4.zero);
            _surfaceInput = _surface.gameObject.AddComponent<CanvasGroup>();
            Panel(_surface, "EquipmentPaper", 0, 0, Width, Height, true);
            _body = QdaoUguiFactory.CreateStretch("EquipmentContent", _surface, Vector4.zero);
            _modalLayer = QdaoUguiFactory.CreateStretch("EquipmentModal", _frame, Vector4.zero);
            _input = _root.gameObject.AddComponent<EquipWishDialogInput>();
            _input.CancelRequested = Back;
            _legacyForge = new EquipForgeDialog(_root, _forge);
            _craft.Changed += Render; _craft.Applied += Applied; _craft.AppliedReceipt += AppliedCraftReceipt;
            _forge.Changed += Render;
            _root.gameObject.SetActive(false);
        }

        public void Bind(PlayerFeaturesClient client, AttributeClient attributes = null)
        {
            if (!ReferenceEquals(_attributes, attributes))
            {
                if (_attributes != null) _attributes.OnPanel -= OnPanel;
                _attributes = attributes; _panel = attributes?.Panel;
                if (_attributes != null) _attributes.OnPanel += OnPanel;
            }
            if (ReferenceEquals(_client, client)) return;
            Hide();
            if (_client != null) { _client.OnChanged -= OnClientChanged; _client.OnMaterialRewardClaimed -= OnMaterialRewardClaimed; }
            _client = client; _targetSlot = null; _selectedItem = _candidate = _affixItem = 0;
            _recipeConfig = _artifactConfig = 0; _notice = string.Empty; _scroll.Clear();
            _craft.Bind(client); _forge.Bind(client);
            _materialSelectionReady = false;
            if (_client != null) { _client.OnChanged += OnClientChanged; _client.OnMaterialRewardClaimed += OnMaterialRewardClaimed; }
        }

        public void Show(EquipmentPage page = EquipmentPage.Overview)
        {
            if (_disposed) return;
            if (!IsVisible) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            BindSeason(AppBootstrap.Instance?.GameClient?.Season);
            _input.enabled = true;
            _root.SetAsLastSibling(); _root.gameObject.SetActive(true);
            _page = page; _modal = Modal.None; _notice = string.Empty;
            Render(); Refresh();
        }

        public void Hide()
        {
            if (!IsVisible) return;
            BindSeason(null);
            _legacyForge.Close(); _forge.Close(); _craft.ClearSelection();
            _modal = Modal.None; _input.Controls.Clear(); _root.gameObject.SetActive(false);
            EventSystem.current?.SetSelectedGameObject(_returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null; Closed?.Invoke();
        }

        private void BindSeason(SeasonClient season)
        {
            if (ReferenceEquals(_season, season)) return;
            if (_season != null) _season.Changed -= OnSeasonChanged;
            _season = season;
            if (_season != null)
            {
                _season.Changed += OnSeasonChanged;
                if (_season.Snapshot == null && !_season.StatePending) _season.RequestState();
            }
        }

        private void OnSeasonChanged() { if (IsVisible) Render(); }

        public void Back()
        {
            if (_legacyForge.IsVisible) { _legacyForge.Close(); return; }
            if (_modal != Modal.None) { CloseModal(); return; }
            Hide();
        }

        public void Tick() { if (IsVisible) _forge.Tick(); }
        private void OnPanel(AttributePanelInfo panel) { _panel = panel; Render(); }
        private void OnClientChanged()
        {
            if (_client?.SnapshotPlayerId == 0 && IsVisible) { Hide(); return; }
            Render();
        }
        private void Refresh()
        {
            _craft.ClearSelection();
            _client?.RequestBag(PlayerFeaturesClient.CharacterBagType); _client?.RequestEquipment();
            _craft.RefreshRules(); _attributes?.RequestPanel();
        }
        private bool WriteBlocked => _client == null || _craft.Busy || _craft.Pending ||
            _client.HasPendingActivityReward || _client.HasPendingMaterialReward || _client.BusyMaterialReward ||
            _client.BagLoading || _client.EquipmentLoading;
        private string WriteReason => _craft.Pending ? "正在确认上次装备操作，请恢复结果" :
            _client?.HasPendingMaterialReward == true ? "请先确认上次材料领取" :
            _client?.HasPendingActivityReward == true ? "请先确认上次奖励领取" :
            _craft.Busy ? "装备操作处理中，请稍候" : "正在同步装备，请稍候";
        private BagItemInfo FindItem(ulong id)
        {
            if (id == 0) return null;
            return _client?.Equipment?.Items.FirstOrDefault(i => i.ItemId == id) ??
                _client?.CharacterBag?.Items.FirstOrDefault(i => i.ItemId == id);
        }
        private static string Name(BagItemInfo item) => item == null ? "尚未装备" : GameplayUiArt.ItemName(item);
        private void Applied(ulong id)
        {
            bool wasUpgradeTier = _modal == Modal.UpgradeTier;
            _selectedItem = id;
            _notice = wasUpgradeTier ? "装备已原地无损升档（强化、保底、词条与品相已保留）" : "装备操作已完成";
            _modal = Modal.None;
            // A created empty item stays a real instance: each subsequent affix is previewed and confirmed separately.
            if (_page == EquipmentPage.Craft)
            { _craftOwnedMode = true; _notice = "装备已保存，可继续选择尚未安装的词条位置"; }
            Render(); _attributes?.RequestPanel();
        }
        private void AppliedCraftReceipt(CommitEquipmentCraftResponse response)
        {
            if (response == null) return;
            if (_page == EquipmentPage.Polish)
            {
                string tier = EquipmentScreenModel.QualityTierName(response.ResultQualityTier);
                _notice = response.GuaranteedByPity
                    ? $"打磨触发保底，成功达成【{tier}】！"
                    : !string.IsNullOrEmpty(tier)
                        ? $"打磨完成，当前品相：【{tier}】（保底 {response.ResultPolishPityCount}/8）"
                        : "词条处理已完成";
                _polishAllowBound = false;
                _reforgeAllowBound = false;
                Render();
            }
        }
        private void ChangePage(EquipmentPage page)
        {
            _page = page; _modal = Modal.None; _notice = string.Empty; _scroll.Clear();
            _craft.ClearSelection(); _forge.Close(); Render();
            if (_craft.Rules == null && !_craft.LoadingRules) _craft.RefreshRules();
        }
        private void CloseModal()
        {
            _modal = Modal.None; _craft.ClearSelection(); Render();
            if (_page == EquipmentPage.Craft && _recipeConfig != 0 && !_craft.Pending) _craft.SelectRecipe(_recipeConfig);
        }
        private void SelectEnhancement(ulong itemId) { _selectedItem = itemId; _forge.Open(itemId); Render(); }

        private void Render()
        {
            if (!IsVisible || _legacyForge.IsVisible) return;
            if (_rendering) { _renderAgain = true; return; }
            _rendering = true;
            try
            {
                do
                {
                    _renderAgain = false;
                    string focus = EventSystem.current?.currentSelectedGameObject?.name;
                    foreach (var scroll in _frame.GetComponentsInChildren<ScrollRect>()) _scroll[scroll.name] = scroll.verticalNormalizedPosition;
                    Clear(_body); Clear(_modalLayer);
                    string[] names = { "装备", "打造", "强化", "法宝", "打磨" };
                    for (int i = 0; i < 4; ++i)
                    {
                        var page = (EquipmentPage)i;
                        Button(_body, "EquipmentTab_" + i, names[i], 180 + i * 365, 150, 355,
                            () => ChangePage(page), _page == page);
                    }
                    Button(_body, "EquipmentTab_4", names[4], 2660, 135, 355,
                        () => ChangePage(EquipmentPage.Polish), _page == EquipmentPage.Polish);
                    string heading = _page == EquipmentPage.Craft ? "装备打造" :
                        _page == EquipmentPage.Enhance ? "装备强化" :
                        _page == EquipmentPage.Polish ? "装备打磨" : names[(int)_page];
                    Label(_body, "EquipmentHeading", heading, 1680, 65, 960, 130, 68, color: Jade);
                    Button(_body, "EquipmentRefresh", "刷新", 3040, 135, 355, Refresh, enabled: !_craft.LoadingRules && !_craft.Busy);
                    Button(_body, "EquipmentClose", "关闭", 3420, 135, 355, Hide);
                    switch (_page)
                    {
                        case EquipmentPage.Overview: RenderOverview(); break;
                        case EquipmentPage.Craft: RenderCraft(); break;
                        case EquipmentPage.Enhance:
                            EquipmentEnhancementView.Render(_body, _client, _forge, SelectEnhancement, WriteBlocked,
                                onRequestEnhanceBindConfirm: OpenEnhanceBindConfirm,
                                onOpenUnseal: OpenUnsealConfirm,
                                onOpenInherit: OpenInheritEnhance,
                                onOpenUpgradeTier: OpenUpgradeTier);
                            break;
                        case EquipmentPage.Artifacts: RenderArtifacts(); break;
                        case EquipmentPage.Polish: RenderPolish(); break;
                    }
                    string status = _craft.Pending ? "正在确认结果。关闭页面不会取消操作，请恢复原操作。" :
                        _client?.HasPendingMaterialReward == true ? "正在确认材料领取。关闭页面不会取消领取，请确认上次结果。" :
                        _craft.Busy ? "正在处理装备…" : _craft.LoadingPreview ? "正在读取成品预览…" :
                        !string.IsNullOrEmpty(_craft.Error) ? _craft.Error :
                        !string.IsNullOrEmpty(_client?.EquipmentError) ? _client.EquipmentError :
                        !string.IsNullOrEmpty(_client?.BagError) ? _client.BagError : _notice;
                    Label(_body, "EquipmentStatus", status, 200, 1490, 2950, 65, 36, color: _craft.Pending ? Jade : Danger);
                    if (_craft.Pending) Button(_body, "EquipmentRecover", "确认上次结果", 3130, 1360, 620, _craft.Recover, true, !_craft.Busy);
                    else if (_client?.HasPendingMaterialReward == true)
                        Button(_body, "EquipmentRecover", "确认领取结果", 3130, 1360, 620, () => _client.RetryPendingMaterialReward(), true, !_client.BusyMaterialReward);
                    if (_modal != Modal.None) RenderModal();
                    _surfaceInput.interactable = _modal == Modal.None; _surfaceInput.blocksRaycasts = _modal == Modal.None;
                    _modalLayer.gameObject.SetActive(_modal != Modal.None);
                    var active = _modal == Modal.None ? _body : _modalLayer;
                    _input.Controls = active.GetComponentsInChildren<Selectable>().Where(s => s.IsActive() && s.IsInteractable()).ToList();
                    foreach (var scroll in _frame.GetComponentsInChildren<ScrollRect>())
                        if (_scroll.TryGetValue(scroll.name, out var value)) scroll.verticalNormalizedPosition = value;
                    var selected = _input.Controls.FirstOrDefault(s => s.name == focus) ?? _input.Controls.FirstOrDefault();
                    EventSystem.current?.SetSelectedGameObject(selected?.gameObject);
                } while (_renderAgain);
            }
            finally { _rendering = false; }
        }

        private void TextLines(UnityEngine.Transform parent, string name, IEnumerable<string> lines, float x, float y, float w, float h, float font = 42)
        {
            // Each complete line wraps and contributes measured preferred height; long artifact restrictions remain scrollable.
            var list = lines.ToList();
            var content = Scroll(parent, name, x, y, w, h, h);
            float top = 0;
            foreach (var value in list)
            {
                var label = Label(content, name + "Line" + top, value, 8, top, w - 52, 80, font, true);
                float height = Mathf.Max(65, label.GetPreferredValues(value, w - 52, 0).y + 18);
                label.rectTransform.sizeDelta = new Vector2(w - 52, height); top += height;
            }
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(h, top));
        }

        public void Dispose()
        {
            if (_disposed) return;
            Hide(); BindSeason(null); Bind(null, null); _disposed = true;
            _craft.Changed -= Render; _craft.Applied -= Applied; _craft.AppliedReceipt -= AppliedCraftReceipt; _forge.Changed -= Render;
            _legacyForge.Dispose(); _craft.Dispose(); _forge.Dispose();
            if (_root != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject); else UnityEngine.Object.DestroyImmediate(_root.gameObject); }
        }
    }
}
