using System;
using System.Collections.Generic;
using System.Globalization;
using Match;
using MmorpgClient.Game.BattleScripts;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.BattleScripts
{
    /// <summary>
    /// 战斗剧本窗口(服务端 docs/design/battle-script.md §7)。左边是本人的剧本(审核状态、删除、选用),右边是玩法选择、
    /// 该玩法当前的选用、官方剧本,以及「粘贴 protobuf JSON 导入」的提交框。
    /// 窗口只渲染 <see cref="BattleScriptClient"/> 的快照、发出意图;请求、代次与隔离都在 BattleScriptClient 里。
    /// 选用按「玩法」记:这里固定选「任意副本」(battle_config_id = 0),按副本细分的选用由服务端支持、界面暂不出。
    /// </summary>
    public sealed class BattleScriptWindow
    {
        /// <summary>可选用剧本的玩法:MatchMode 数值与显示名(服务端 proto/match/match_service.proto MatchMode)。</summary>
        public static readonly IReadOnlyList<(uint Mode, string Label)> PlayModes = new[]
        {
            (0u, "全部玩法"),
            ((uint)MatchMode.PveSolo, "单人副本"),
            ((uint)MatchMode.PveTeam, "组队副本"),
            ((uint)MatchMode._1V1, "论剑 1v1"),
            ((uint)MatchMode.PvpTeam, "组队论剑"),
            ((uint)MatchMode.PvpChallenge, "切磋"),
        };

        public const int MyScriptRows = 10;
        public event Action RefreshRequested;
        public event Action<ulong, uint> DeleteRequested;               // script_id, revision
        public event Action<uint, ulong> ChooseOwnRequested;            // match_mode, script_id
        public event Action<uint, uint> ChooseOfficialRequested;        // match_mode, official script_id
        public event Action<uint> ClearChoiceRequested;                 // match_mode
        public event Action<global::BattleScript> SubmitRequested;
        public RectTransform Root => _root;
        public bool IsVisible => _root != null && _root.gameObject.activeSelf;
        public uint SelectedMode { get; private set; }

        private readonly RectTransform _root, _mine, _official, _modes;
        private readonly TMP_Text _status, _detail, _choice, _mineHeading;
        private readonly TMP_InputField _import;
        private readonly Button _refresh, _clear, _submit;
        private BattleScriptClient _client;

        public BattleScriptWindow(Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("BattleScriptWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("BattleScriptFrame", _root, 1640, 924);
            Art(frame, "main_frame", 0, 0, 1640, 924);
            Art(frame, "title_plate", 62, -21, 480, 114);
            Text(frame, "战斗剧本", 100, -7, 400, 84, 49, Cream, alignment: TextAlignmentOptions.Center);
            Text(frame, "挂机、托管或超时时按剧本出招 · 手动出手不受影响", 590, 40, 860, 58, 26, Muted);
            var close = Button(frame, "", 1516, 28, 76, 76, Hide, key: "close");
            close.name = "CloseBattleScript";

            // 左:本人的剧本
            Art(frame, "content_panel", 56, 132, 760, 680);
            _mineHeading = Text(frame, "我的剧本", 84, 144, 700, 46, 28, Gold);
            _mine = QdaoUguiFactory.CreateRect("MyScriptRows", frame, 72, 196, 728, 600);

            // 右:玩法、当前选用、官方剧本、导入
            Art(frame, "content_panel", 836, 132, 748, 680);
            _modes = QdaoUguiFactory.CreateRect("PlayModes", frame, 852, 144, 716, 120);
            _choice = Text(frame, "", 860, 272, 520, 46, 25, Muted);
            _clear = Button(frame, "清除选用", 1390, 268, 170, 54, () => ClearChoiceRequested?.Invoke(SelectedMode), fontSize: 24);
            _clear.name = "BattleScriptClearChoice";
            Text(frame, "官方剧本", 860, 330, 300, 42, 27, Gold);
            _official = QdaoUguiFactory.CreateRect("OfficialScriptRows", frame, 852, 376, 716, 170);
            Text(frame, "导入剧本(粘贴 JSON)", 860, 556, 500, 42, 27, Gold);
            _import = QdaoUguiFactory.CreateInputField("BattleScriptImport", frame, 860, 604, 700, 140, "在这里粘贴剧本的 JSON …", 4096);
            _import.lineType = TMP_InputField.LineType.MultiLineNewline;
            _import.textComponent.textWrappingMode = TextWrappingModes.Normal;
            _import.textComponent.alignment = TextAlignmentOptions.TopLeft;
            _import.textComponent.fontSize = 20;
            _import.textComponent.richText = false;
            _submit = Button(frame, "提交为新剧本", 1360, 752, 200, 56, Submit, primary: true, fontSize: 24);
            _submit.name = "BattleScriptSubmit";

            _status = Text(frame, "", 76, 826, 1060, 46, 25, Muted);
            _detail = Text(frame, "", 76, 870, 1300, 40, 20, Muted);
            _refresh = Button(frame, "刷新", 1390, 826, 190, 70, () => RefreshRequested?.Invoke(), fontSize: 28);
            _refresh.name = "BattleScriptRefresh";
        }

        public void Show(BattleScriptClient client)
        {
            _client = client;
            _root.gameObject.SetActive(true);
            Render();
        }

        public void Hide() => _root.gameObject.SetActive(false);

        public void SelectMode(uint mode)
        {
            SelectedMode = mode;
            Render();
        }

        /// <summary>按 BattleScriptClient 的当前快照重画(由 BattleScriptUiRoot 在 Changed 时调用)。</summary>
        public void Render()
        {
            var list = _client?.List;
            bool busy = _client?.Busy == true;
            RenderModes(busy);
            RenderMine(list, busy);
            RenderOfficial(list, busy);
            RenderChoice(list);
            _clear.interactable = !busy && _client?.ChoiceFor(SelectedMode, 0) != null;
            _submit.interactable = !busy && list != null;
            _refresh.interactable = !busy;
            _status.text = _client?.Status ?? "";
            _detail.text = _client?.InvalidDetail ?? "";
        }

        private void RenderModes(bool busy)
        {
            Clear(_modes);
            for (int i = 0; i < PlayModes.Count; i++)
            {
                var (mode, label) = PlayModes[i];
                float x = (i % 3) * 240;
                float y = (i / 3) * 62;
                bool selected = mode == SelectedMode;
                var button = Button(_modes, label, x, y, 228, 54, () => SelectMode(mode),
                    key: selected ? "tab_selected" : "tab_normal", fontSize: 24);
                button.name = "BattleScriptMode_" + mode.ToString(CultureInfo.InvariantCulture);
                button.interactable = !busy;
            }
        }

        private void RenderMine(ListBattleScriptsResponse list, bool busy)
        {
            Clear(_mine);
            uint max = list?.MaxScripts ?? 0;
            _mineHeading.text = list == null ? "我的剧本"
                : "我的剧本 (" + list.MyScripts.Count.ToString(CultureInfo.InvariantCulture) + " / " + max.ToString(CultureInfo.InvariantCulture) + ")";
            if (list == null) return;
            for (int i = 0; i < list.MyScripts.Count && i < MyScriptRows; i++)
            {
                var view = list.MyScripts[i];
                float y = i * 60;
                bool chosen = IsChosenOwn(view.ScriptId);
                var row = Art(_mine, chosen ? "tab_selected" : "tab_normal", 0, y, 728, 54);
                row.name = "MyScript_" + view.ScriptId.ToString(CultureInfo.InvariantCulture);
                Text(_mine, BattleScriptClient.DisplayName(view.Content), 14, y + 4, 300, 46, 24, chosen ? Cream : Ink);
                Text(_mine, BattleScriptClient.DescribeReview(view), 318, y + 4, 230, 46, 20, Muted);
                ulong scriptId = view.ScriptId;
                uint revision = view.Revision;
                var choose = Button(_mine, "选用", 552, y + 4, 80, 46, () => ChooseOwnRequested?.Invoke(SelectedMode, scriptId), fontSize: 20);
                choose.name = "ChooseOwn_" + scriptId.ToString(CultureInfo.InvariantCulture);
                choose.interactable = !busy && view.ApprovedRevision != 0;
                var delete = Button(_mine, "删除", 640, y + 4, 80, 46, () => DeleteRequested?.Invoke(scriptId, revision), fontSize: 20);
                delete.name = "DeleteOwn_" + scriptId.ToString(CultureInfo.InvariantCulture);
                delete.interactable = !busy;
            }
        }

        private void RenderOfficial(ListBattleScriptsResponse list, bool busy)
        {
            Clear(_official);
            if (list == null) return;
            for (int i = 0; i < list.OfficialScripts.Count && i < 3; i++)
            {
                var script = list.OfficialScripts[i];
                float y = i * 56;
                bool chosen = IsChosenOfficial(script.ScriptId);
                var row = Art(_official, chosen ? "tab_selected" : "tab_normal", 0, y, 716, 50);
                row.name = "OfficialScript_" + script.ScriptId.ToString(CultureInfo.InvariantCulture);
                Text(_official, BattleScriptClient.DisplayName(script), 14, y + 2, 560, 46, 24, chosen ? Cream : Ink);
                uint scriptId = script.ScriptId;
                var choose = Button(_official, "选用", 612, y + 2, 96, 46, () => ChooseOfficialRequested?.Invoke(SelectedMode, scriptId), fontSize: 20);
                choose.name = "ChooseOfficial_" + scriptId.ToString(CultureInfo.InvariantCulture);
                choose.interactable = !busy;
            }
        }

        private void RenderChoice(ListBattleScriptsResponse list)
        {
            var choice = _client?.ChoiceFor(SelectedMode, 0);
            if (list == null) { _choice.text = ""; return; }
            if (choice == null) { _choice.text = "当前选用:无(用官方默认或不带剧本)"; return; }
            _choice.text = "当前选用:" + DescribeChoice(list, choice);
        }

        /// <summary>选用条目的显示文字:官方剧本按 id 找名字,本人剧本按 id 找名字,找不到显示编号</summary>
        public static string DescribeChoice(ListBattleScriptsResponse list, BattleScriptLoadoutEntry choice)
        {
            if (choice.Origin == eBattleScriptOrigin.BattleScriptOriginOfficial)
            {
                foreach (var script in list.OfficialScripts)
                    if (script.ScriptId == choice.OfficialScriptId) return "官方 · " + BattleScriptClient.DisplayName(script);
                return "官方 · 编号 " + choice.OfficialScriptId.ToString(CultureInfo.InvariantCulture);
            }
            foreach (var view in list.MyScripts)
                if (view.ScriptId == choice.PlayerScriptId) return "我的 · " + BattleScriptClient.DisplayName(view.Content);
            return "我的 · 编号 " + choice.PlayerScriptId.ToString(CultureInfo.InvariantCulture);
        }

        private bool IsChosenOwn(ulong scriptId)
        {
            var choice = _client?.ChoiceFor(SelectedMode, 0);
            return choice != null && choice.Origin == eBattleScriptOrigin.BattleScriptOriginPlayer && choice.PlayerScriptId == scriptId;
        }

        private bool IsChosenOfficial(uint scriptId)
        {
            var choice = _client?.ChoiceFor(SelectedMode, 0);
            return choice != null && choice.Origin == eBattleScriptOrigin.BattleScriptOriginOfficial && choice.OfficialScriptId == scriptId;
        }

        private void Submit()
        {
            if (!BattleScriptClient.TryParseScript(_import.text, out var script, out var error))
            {
                _status.text = error;
                _detail.text = "";
                return;
            }
            SubmitRequested?.Invoke(script);
        }
    }
}
