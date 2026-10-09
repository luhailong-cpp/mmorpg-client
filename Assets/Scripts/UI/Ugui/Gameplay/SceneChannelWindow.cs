using System;
using System.Collections.Generic;
using System.Text;
using MmorpgClient.Game.WorldTravel;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Gameplay.GameplayUiArt;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>
    /// 线路面板一次渲染要用的全部数据。只是一份快照:窗口不持有网络对象,
    /// 宿主在数据层每次变化后整份重给(<see cref="SceneChannelUiRoot.BuildView"/>)。
    /// 默认值(全空)渲染成「线路信息尚未就绪」。
    /// </summary>
    public struct SceneChannelPanelView
    {
        /// <summary>当前地图的线路,按线号升序;null 视为空。</summary>
        public IReadOnlyList<SceneChannelLine> Lines;
        /// <summary>
        /// 判定「能不能切」的全部状态。逐行用 <see cref="SceneChannelModels.Evaluate"/> 算,
        /// 与数据层发请求前的校验是同一条规则,界面不另写一套。
        /// </summary>
        public SceneChannelSwitchContext Context;
        /// <summary>列线尚无结论(在等回包,或因最小间隔排着队)。</summary>
        public bool Loading;
        /// <summary>在途切线的目标;不在途为 0。</summary>
        public ulong SwitchTargetSceneId;
        /// <summary>数据层的状态行(切线进度 / 结论、列线失败、本地拒绝);"" = 没有要说的。</summary>
        public string Status;
        /// <summary><see cref="Status"/> 是不是一条失败 / 不可用提示。</summary>
        public bool StatusIsError;
    }

    /// <summary>
    /// 线路面板:列出当前地图的分线,点一条就发出「切到这条线」的意图。
    /// 只读数据 + 意图事件:不碰网络,不做本地假成功;一行能不能点完全由传入的视图决定,
    /// 点击时按同一条规则再核一遍(两次渲染之间冷却倒计时可能已经走完或刚开始)。
    /// 版式(设计面 2560×1080,左上原点):居中纸面主框,线路区两列 × 八行、先左后右再换行,超过十六条分页。
    /// </summary>
    public sealed class SceneChannelWindow
    {
        public const int Columns = 2;
        public const int RowsPerColumn = 8;
        public const int LinesPerPage = Columns * RowsPerColumn;

        // 测试与离线截图按名字找控件。
        public const string LineButtonPrefix = "SceneChannelLine_";
        public const string LoadLabelPrefix = "SceneChannelLoad_";
        public const string RefreshButtonName = "SceneChannelRefresh";
        public const string CloseButtonName = "SceneChannelClose";
        public const string PreviousPageButtonName = "SceneChannelPreviousPage";
        public const string NextPageButtonName = "SceneChannelNextPage";
        public const string CurrentTagName = "SceneChannelCurrentTag";
        public const string EmptyLabelName = "SceneChannelEmpty";

        public const string TitleText = "选择线路";
        public const string CurrentTagText = "当前";
        public const string SwitchingRowText = "切换中…";
        public const string LoadingText = "正在获取线路…";
        public const string NoDirectoryText = "当前场景暂无线路信息。";
        public const string SingleLineHint = "当前地图只有一条线路。";
        public const string PickHint = "点击流畅或繁忙的线路即可切换。";
        public const string RefreshText = "刷新";
        public const string RefreshingText = "获取中…";

        private const float FrameWidth = 1760, FrameHeight = 924;
        private const float ListX = 100, ListY = 183;
        private const float RowWidth = 760, RowHeight = 66, RowPitch = 71, ColumnGap = 40;
        private const float ListWidth = RowWidth * Columns + ColumnGap * (Columns - 1);
        private const float ListHeight = RowPitch * (RowsPerColumn - 1) + RowHeight;
        private const float FooterY = 798;

        // 流畅 / 繁忙 / 爆满三色与选服界面一致:QdaoServerSelectView 的 DotSmooth / DotBusy / DotFull
        // (那边是 private,这里复制数值);回收中 / 暂不可用 / 未知用灰色。
        private static readonly Color LoadSmooth = QdaoUguiTheme.Html("#3F9B4E");
        private static readonly Color LoadBusy = QdaoUguiTheme.Html("#D98E1B");
        private static readonly Color LoadFull = QdaoUguiTheme.Html("#C23B22");
        private static readonly Color LoadOff = QdaoUguiTheme.Html("#7F7A70");
        // 与背包 / 任务窗的错误行同色(GameplayWindow)。
        private static readonly Color ErrorInk = QdaoUguiTheme.Html("#9A442D");

        public event Action<ulong> SwitchRequested;
        public event Action RefreshRequested;
        public event Action Closed;

        public bool IsVisible => _root.gameObject.activeSelf;
        /// <summary>当前页(从 0 起);每次渲染按总页数收拢。</summary>
        public int PageIndex { get; private set; }
        public int PageCount { get; private set; } = 1;
        /// <summary>底部状态行此刻显示的文字。</summary>
        public string StatusText => _status.text;
        /// <summary>底部状态行此刻是不是按失败提示着色。</summary>
        public bool StatusIsError { get; private set; }

        private readonly RectTransform _root, _list;
        private readonly TMP_Text _current, _status, _pageLabel, _refreshLabel;
        private readonly Button _previous, _next, _refresh;
        private SceneChannelPanelView _view;
        // 列表区上一次画出来的内容的指纹(见 DescribeList);null = 还没画过。指纹没变就不重建行。
        private string _listKey;
        // 状态行上一次显示的冷却秒数:倒计时每帧喂进来,只有整秒变了才重写文字。
        private int _cooldownSecondsShown;

        public SceneChannelWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("SceneChannelWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .10f, .08f, .68f);
            shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("SceneChannelFrame", _root, FrameWidth, FrameHeight);
            Art(frame, "main_frame", 0, 0, FrameWidth, FrameHeight);
            Art(frame, "title_plate", 62, -21, 480, 114);
            Text(frame, TitleText, 100, -7, 400, 84, 49, Cream, alignment: TextAlignmentOptions.Center);
            _current = Text(frame, "", 604, 40, 760, 58, 30, Muted);
            _current.name = "SceneChannelCurrent";

            Art(frame, "content_panel", 56, 150, FrameWidth - 112, 632);
            _list = QdaoUguiFactory.CreateRect("SceneChannelLines", frame, ListX, ListY, ListWidth, ListHeight);

            // 底栏是固定槽位:状态行、翻页、刷新、关闭;渲染只改它们的文字与可用性,不重建。
            _status = Text(frame, "", 72, FooterY, 760, 64, 28, Muted);
            _status.name = "SceneChannelStatus";
            _previous = Button(frame, "上一页", 860, FooterY + 4, 150, 56, () => MovePage(-1), fontSize: 26);
            _previous.name = PreviousPageButtonName;
            _pageLabel = Text(frame, "", 1014, FooterY + 4, 110, 56, 26, Muted, alignment: TextAlignmentOptions.Center);
            _pageLabel.name = "SceneChannelPage";
            _next = Button(frame, "下一页", 1128, FooterY + 4, 150, 56, () => MovePage(1), fontSize: 26);
            _next.name = NextPageButtonName;
            _refresh = Button(frame, RefreshText, 1296, FooterY, 190, 64,
                () => { if (CanRefresh) RefreshRequested?.Invoke(); });
            _refresh.name = RefreshButtonName;
            _refreshLabel = _refresh.GetComponentInChildren<TMP_Text>(true);
            var close = Button(frame, "关闭", 1498, FooterY, 190, 64, Hide);
            close.name = CloseButtonName;
            Pagination(_previous, _next, _pageLabel, 0, 1);
        }

        /// <summary>
        /// 换一份视图;只在窗口可见时重画,隐藏时只记下,下次 <see cref="Show"/> 再画。
        /// 重画时底栏(状态行、刷新、翻页)每次都更新,行按钮只在列表区的内容真的变了时才重建。
        /// </summary>
        public void SetView(SceneChannelPanelView view)
        {
            _view = view;
            if (IsVisible) Render();
        }

        /// <summary>
        /// 宿主每帧喂进来的本地冷却剩余秒数。倒计时走动不会让数据层发变化通知,状态行的秒数靠它刷新;
        /// 只有「在不在冷却」翻转时才整份重画(各行的可点状态跟着变),平时最多改一行文字。
        /// </summary>
        public void SetCooldown(float remainingSeconds)
        {
            bool wasCooling = _view.Context.CooldownRemainingSeconds > 0f;
            bool cooling = remainingSeconds > 0f;   // 非数按不在冷却算
            _view.Context.CooldownRemainingSeconds = cooling ? remainingSeconds : 0f;
            if (!IsVisible) return;
            if (wasCooling != cooling) Render();
            else if (cooling && SceneChannelModels.CooldownSecondsCeil(remainingSeconds) != _cooldownSecondsShown)
                RefreshStatus();
        }

        /// <summary>打开面板;从关着到打开时翻到当前线所在的那一页。</summary>
        public void Show()
        {
            if (!IsVisible) PageIndex = PageOfCurrentLine();
            _root.gameObject.SetActive(true);
            Render();
        }

        public void Hide()
        {
            if (!IsVisible) return;
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        // ── 文案(纯函数,测试直接调)────────────────────────────────────────

        /// <summary>
        /// 底部状态行。自上而下第一条命中:
        ///  1. 切线在途 → 数据层的进度文字;
        ///  2. 整张面板置灰的原因里,宿主或服务端此刻给出的那几条(战斗 / 传送 / 队伍成员 / 服务端关闭 / 冷却)
        ///     → 对应说明(冷却带剩余秒数)。它们是此刻的事实,排在留着的上一条提示之前;
        ///  3. 数据层留着的失败提示(切线失败、该线已满、线路获取不到 …);
        ///  4. 尚未就绪 → 列表空着时不重复说(列表区的占位文字已经说明),否则给一句说明;
        ///  5. 数据层留着的其它文字(已切换到 N线);
        ///  6. 默认提示。
        /// </summary>
        public static string DescribeStatus(SceneChannelPanelView view, out bool isError)
        {
            isError = false;
            var context = view.Context;
            string status = view.Status ?? "";
            int lineCount = view.Lines?.Count ?? 0;
            if (context.SwitchPending)
            {
                if (status.Length == 0) return SceneChannelModels.DescribeBlock(SceneChannelBlockReason.Switching);
                isError = view.StatusIsError;
                return status;
            }
            var global = SceneChannelModels.EvaluateGlobal(context);
            switch (global)
            {
                case SceneChannelBlockReason.InBattle:
                case SceneChannelBlockReason.Travelling:
                case SceneChannelBlockReason.TeamMember:
                case SceneChannelBlockReason.SwitchDisabled:
                case SceneChannelBlockReason.CoolingDown:
                    return SceneChannelModels.DescribeBlock(global, context.CooldownRemainingSeconds);
            }
            if (status.Length != 0 && view.StatusIsError)
            {
                isError = true;
                return status;
            }
            if (global == SceneChannelBlockReason.NotReady)
                return lineCount == 0 ? "" : SceneChannelModels.DescribeBlock(SceneChannelBlockReason.NotReady);
            if (status.Length != 0) return status;
            return lineCount <= 1 ? SingleLineHint : PickHint;
        }

        /// <summary>没有任何线路可列时,列表区中央的占位文字。</summary>
        public static string DescribeEmpty(SceneChannelPanelView view)
        {
            if (view.Loading) return LoadingText;
            if (!view.Context.Connected) return SceneChannelModels.DescribeBlock(SceneChannelBlockReason.NotReady);
            // 服务端答复里没有目录(副本 / 镜像 / 目录未发布),或这次没取到(失败原因在状态行)。
            return NoDirectoryText;
        }

        // ── 渲染 ────────────────────────────────────────────────────────────

        private bool CanRefresh => _view.Context.Connected && !_view.Loading && !_view.Context.SwitchPending;

        private int PageOfCurrentLine()
        {
            var lines = _view.Lines;
            if (lines == null) return 0;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i] != null && lines[i].IsCurrent) return i / LinesPerPage;
            return 0;
        }

        private void MovePage(int delta)
        {
            PageIndex += delta;
            Render();
        }

        private void Render()
        {
            var lines = _view.Lines ?? SceneChannelModels.NoLines;
            var context = _view.Context;
            uint current = SceneChannelModels.CurrentChannelNo(lines);
            _current.text = !context.HasDirectory ? ""
                : "当前：" + (current != 0 ? SceneChannelModels.LineName(current) : "未知");

            PageCount = Math.Max(1, (lines.Count + LinesPerPage - 1) / LinesPerPage);
            PageIndex = Mathf.Clamp(PageIndex, 0, PageCount - 1);
            Pagination(_previous, _next, _pageLabel, PageIndex, PageCount);

            // 面板开着时每 5 秒刷新一次目录,每次刷新数据层发两次变化通知(发出请求、目录到达),多数时候变的只是
            // 底栏的「刷新 / 获取中…」和状态行。行按钮若跟着销毁重建,按下与抬起之间撞上的那次点击就丢了
            // (uGUI 要求按下和抬起落在同一个对象上),悬停高亮也会闪。所以列表区的内容没变就不动它。
            string listKey = DescribeList(lines, context);
            if (listKey != _listKey)
            {
                _listKey = listKey;
                Clear(_list);
                if (lines.Count == 0)
                {
                    var empty = Text(_list, DescribeEmpty(_view), 0, (ListHeight - 80) / 2, ListWidth, 80, 38, Muted,
                        alignment: TextAlignmentOptions.Center);
                    empty.name = EmptyLabelName;
                }
                else
                {
                    int start = PageIndex * LinesPerPage;
                    int count = Math.Min(LinesPerPage, lines.Count - start);
                    for (int slot = 0; slot < count; slot++)
                        if (lines[start + slot] != null) BuildRow(lines[start + slot], slot, context);
                }
            }

            _refresh.interactable = CanRefresh;
            _refreshLabel.text = _view.Loading ? RefreshingText : RefreshText;
            RefreshStatus();
        }

        /// <summary>
        /// 列表区此刻该画成什么样的指纹:没有线路时是占位文字;否则是页码加当前页每一行的
        /// (scene_id、线号、状态、是否所在线、能不能点、是不是切线目标)—— <see cref="BuildRow"/> 用到的输入都在里面
        /// (给 BuildRow 加新的输入时这里要跟着加)。人数不显示,所以不算。两次渲染指纹相同就不必重建行。
        /// </summary>
        private string DescribeList(IReadOnlyList<SceneChannelLine> lines, SceneChannelSwitchContext context)
        {
            // 占位文字是中文句子,下面的行指纹以数字开头,两者不会相等。
            if (lines.Count == 0) return DescribeEmpty(_view);
            int start = PageIndex * LinesPerPage;
            int count = Math.Min(LinesPerPage, lines.Count - start);
            var key = new StringBuilder(24 * count + 8);
            key.Append(PageIndex);
            for (int slot = 0; slot < count; slot++)
            {
                var line = lines[start + slot];
                key.Append('|');
                if (line == null) continue;
                key.Append(line.SceneId).Append(',').Append(line.ChannelNo).Append(',').Append((int)line.Load)
                    .Append(line.IsCurrent ? 'c' : '-')
                    .Append(IsSelectable(line, context) ? 's' : '-')
                    .Append(IsSwitchTarget(line, context) ? 't' : '-');
            }
            return key.ToString();
        }

        private static bool IsSelectable(SceneChannelLine line, SceneChannelSwitchContext context) =>
            SceneChannelModels.Evaluate(line, context) == SceneChannelBlockReason.None;

        private bool IsSwitchTarget(SceneChannelLine line, SceneChannelSwitchContext context) =>
            context.SwitchPending && line.SceneId == _view.SwitchTargetSceneId;

        private void BuildRow(SceneChannelLine line, int slot, SceneChannelSwitchContext context)
        {
            bool selectable = IsSelectable(line, context);
            bool switching = IsSwitchTarget(line, context);
            ulong sceneId = line.SceneId;
            float x = slot % Columns * (RowWidth + ColumnGap);
            float y = slot / Columns * RowPitch;
            var button = Button(_list, SceneChannelModels.LineName(line.ChannelNo), x, y, RowWidth, RowHeight,
                () => RequestSwitch(sceneId), enabled: selectable, key: line.IsCurrent ? "tab_selected" : "tab_normal",
                fontSize: 32);
            button.name = LineButtonPrefix + line.ChannelNo;
            // 行比底图原稿(高 114)矮:按高度等比缩小九宫格的边(只有中段横向拉伸),两端的云头和对勾才不会被压扁。
            // 做法同 QdaoRefreshArt.Skin。缩小后两端纹饰各占约 48,行内文字从 64 起排。
            var art = (Image)button.targetGraphic;
            art.pixelsPerUnitMultiplier = Mathf.Max(1f, art.sprite.rect.height / RowHeight);

            // 工厂给的是居中的按钮文字;行里左边放线名、右边放状态,把它挪到左侧。
            var name = button.GetComponentInChildren<TMP_Text>(true);
            name.rectTransform.anchoredPosition = new Vector2(64, 0);
            name.rectTransform.sizeDelta = new Vector2(170, RowHeight);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            if (line.IsCurrent)
            {
                // 所在线不可点,但要醒目:不让「不可用」的灰色盖在选中态底图上。
                var colors = button.colors;
                colors.disabledColor = Color.white;
                button.colors = colors;
                Text(button.transform, CurrentTagText, 250, 0, 110, RowHeight, 26, Cream).name = CurrentTagName;
            }
            else if (!selectable)
            {
                name.color = Muted;
            }

            // 状态:色点 + 文字。选中态底图是深玉色,绿字压在上面看不清,所在线的状态字改用米白,颜色留给色点。
            Color loadColor = LoadColor(line.Load);
            var dot = QdaoUguiFactory.CreateImage("LoadDot", button.transform, 520, (RowHeight - 18) / 2, 18, 18,
                QdaoUguiTheme.RequireSprite(QdaoUguiTheme.StatusDotSpritePath));
            dot.color = loadColor;
            var load = Text(button.transform, switching ? SwitchingRowText : SceneChannelModels.LoadText(line.Load),
                546, 0, 160, RowHeight, 28, line.IsCurrent ? Cream : switching ? Gold : loadColor);
            load.name = LoadLabelPrefix + line.ChannelNo;
        }

        private void RequestSwitch(ulong sceneId)
        {
            // 点击时按当前视图再核一遍:这条线可能已不在列表里,或刚进入冷却。
            var line = SceneChannelModels.FindLine(_view.Lines, sceneId);
            if (SceneChannelModels.Evaluate(line, _view.Context) != SceneChannelBlockReason.None) return;
            SwitchRequested?.Invoke(sceneId);
        }

        private void RefreshStatus()
        {
            string text = DescribeStatus(_view, out bool isError);
            StatusIsError = isError;
            _status.text = text;
            _status.color = isError ? ErrorInk : Muted;
            _cooldownSecondsShown = SceneChannelModels.CooldownSecondsCeil(_view.Context.CooldownRemainingSeconds);
        }

        private static Color LoadColor(SceneChannelLoad load)
        {
            switch (load)
            {
                case SceneChannelLoad.Smooth: return LoadSmooth;
                case SceneChannelLoad.Busy: return LoadBusy;
                case SceneChannelLoad.Full: return LoadFull;
                default: return LoadOff;
            }
        }

        // 写法同 TeamWindow.Pagination:只有一页时翻页控件整组隐藏。
        private static void Pagination(Button previous, Button next, TMP_Text label, int page, int pages)
        {
            previous.interactable = page > 0;
            next.interactable = page + 1 < pages;
            previous.gameObject.SetActive(pages > 1);
            next.gameObject.SetActive(pages > 1);
            label.gameObject.SetActive(pages > 1);
            label.text = (page + 1) + " / " + pages;
        }
    }
}
