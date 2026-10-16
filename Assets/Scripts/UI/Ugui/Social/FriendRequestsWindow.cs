using System;
using System.Globalization;
using MmorpgClient.Game.Social;
using MmorpgClient.World.Tianyong;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static MmorpgClient.UI.Ugui.Social.SocialUiArt;

namespace MmorpgClient.UI.Ugui.Social
{
    /// <summary>真实好友申请入口；不使用预览人物，不在客户端裁定关系。</summary>
    public sealed class FriendRequestsWindow : IDisposable
    {
        public const int PageSize = 5;
        public bool IsVisible => _root.gameObject.activeSelf;
        public RectTransform Root => _root;
        private readonly RectTransform _root, _rows;
        private readonly TMP_InputField _target;
        private readonly TMP_Text _status, _page;
        private readonly Button _refresh, _add, _previous, _next;
        private FriendClient _client;
        private GameObject _returnFocus;
        private int _pageIndex;

        public FriendRequestsWindow(UnityEngine.Transform parent)
        {
            _root = QdaoUguiFactory.CreateStretch("FriendRequestsWindow", parent, Vector4.zero);
            _root.gameObject.SetActive(false);
            var shade = _root.gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .11f, .08f, .76f); shade.raycastTarget = true;
            _root.gameObject.AddComponent<GameplayInputBlocker>();
            var frame = QdaoUguiFactory.CreateCenteredRect("FriendRequestsFrame", _root, 1800, 890);
            Art(frame, "window_frame", 0, 0, 1800, 890);
            Text(frame, "好 友 申 请", 100, 42, 1200, 80, 48, Jade, heading: true);
            Button(frame, "FriendRequestsClose", "关闭", 1530, 50, 180, 62, Hide);
            Text(frame, "添加道友", 100, 146, 190, 62, 30, Jade);
            _target = Input(frame, "FriendTargetPlayerId", "输入玩家编号", "", 310, 146, 760, 66, limit: 20);
            _target.contentType = TMP_InputField.ContentType.IntegerNumber;
            _add = Button(frame, "FriendAdd", "发送申请", 1100, 146, 270, 66, Add, true);
            _refresh = Button(frame, "FriendRequestsRefresh", "刷新申请", 1400, 146, 310, 66, () => _client?.RefreshPending());
            _rows = QdaoUguiFactory.CreateRect("FriendRequestRows", frame, 100, 244, 1610, 480);
            _previous = Button(frame, "FriendRequestsPrevious", "上一页", 100, 746, 200, 60, () => Move(-1));
            _page = Text(frame, "", 330, 748, 900, 60, 26, Muted, align: TextAlignmentOptions.Center);
            _next = Button(frame, "FriendRequestsNext", "下一页", 1500, 746, 210, 60, () => Move(1));
            _status = Text(frame, "请进入角色后处理好友申请。", 100, 817, 1610, 50, 24, Muted,
                name: "FriendRequestsStatus");
        }

        public void Bind(FriendClient client)
        {
            if (_client != null) _client.Changed -= Render;
            Hide(); _client = client; _pageIndex = 0; _target.SetTextWithoutNotify("");
            if (_client != null) _client.Changed += Render;
        }

        public void Show()
        {
            if (!IsVisible) _returnFocus = EventSystem.current?.currentSelectedGameObject;
            _root.gameObject.SetActive(true); _root.SetAsLastSibling(); _pageIndex = 0;
            Render(); _client?.RefreshPending();
            EventSystem.current?.SetSelectedGameObject(_target.gameObject);
        }

        public void Hide()
        {
            bool visible = IsVisible; _root.gameObject.SetActive(false);
            if (visible) EventSystem.current?.SetSelectedGameObject(
                _returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : null);
            _returnFocus = null;
        }

        public void ResetSession() { Hide(); _pageIndex = 0; _target.SetTextWithoutNotify(""); Clear(_rows); }

        private void Add()
        {
            if (!ulong.TryParse(_target.text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong target) || target == 0)
            { _status.text = "请输入有效的玩家编号。"; return; }
            _client?.AddFriend(target);
        }

        private void Move(int delta) { _pageIndex += delta; Render(); }

        private void Render()
        {
            if (!IsVisible) return;
            Clear(_rows);
            int count = _client?.Pending.Count ?? 0, pages = Math.Max(1, (count + PageSize - 1) / PageSize);
            _pageIndex = Math.Max(0, Math.Min(_pageIndex, pages - 1));
            _status.text = _client?.Status ?? "请进入角色后处理好友申请。";
            _page.text = $"第 {_pageIndex + 1} / {pages} 页 · 共 {count} 份申请";
            bool available = _client != null && !_client.Busy;
            _add.interactable = _refresh.interactable = available;
            _previous.interactable = _pageIndex > 0;
            _next.interactable = _pageIndex + 1 < pages;
            if (count == 0)
            {
                Text(_rows, _client?.Busy == true ? "正在读取服务器申请…" : "暂无待处理申请，请刷新查看。",
                    40, 120, 1500, 130, 34, Muted, align: TextAlignmentOptions.Center);
                return;
            }
            for (int row = 0, index = _pageIndex * PageSize; row < PageSize && index < count; ++row, ++index)
            {
                var request = _client.Pending[index].Clone();
                float y = row * 94;
                Text(_rows, "道友 " + request.FromPlayerId, 10, y + 2, 850, 46, 28, Jade);
                Text(_rows, request.RequestId == 0 ? "申请编号缺失，请刷新" : "待处理好友申请", 10, y + 48,
                    850, 36, 20, Muted);
                bool actionable = available && _client.HasFreshPending && request.RequestId != 0;
                Button(_rows, "AcceptFriend_" + request.RequestId, "同意", 1080, y + 16, 220, 62,
                    () => _client?.AcceptFriend(request.RequestId), true, enabled: actionable);
                Button(_rows, "RejectFriend_" + request.RequestId, "拒绝", 1330, y + 16, 220, 62,
                    () => _client?.RejectFriend(request.RequestId), enabled: actionable);
                Line(_rows, 0, y + 90, 1580);
            }
        }

        public void Dispose()
        {
            if (_client != null) _client.Changed -= Render;
            _client = null; Hide();
        }
    }
}
