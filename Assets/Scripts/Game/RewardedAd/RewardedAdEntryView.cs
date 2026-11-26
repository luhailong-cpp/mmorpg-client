using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.Game.RewardedAd
{
    /// <summary>
    /// 零拒审激励视频广告入口与二次确认窗视图：
    /// 1. 未同时满足「服务端开启 + 宿主支持 + 广告就绪 + 有余量 + 非战斗」时整块 SetActive(false) 彻底隐藏（防 Apple 2.1 空按钮拒审）；
    /// 2. 二次确认窗提供同等显眼的「观看广告领取」与「关闭」按钮及 [广告] 标识（符合抖音小游戏合规规范）；
    /// 3. 严格对称订阅/退订事件，帧循环仅调用 RewardedAdClient.Tick() 推进断线重连补领。
    /// </summary>
    public sealed class RewardedAdEntryView : MonoBehaviour
    {
        [SerializeField] private GameObject _entryRoot;
        [SerializeField] private Button _entryButton;
        [SerializeField] private Image _entryVideoIcon;
        [SerializeField] private TMP_Text _entryLabel;

        [SerializeField] private GameObject _confirmDialogRoot;
        [SerializeField] private Image _dialogRewardIcon;
        [SerializeField] private Image _balanceBindGoldIcon;
        [SerializeField] private TMP_Text _dialogTitleText;
        [SerializeField] private TMP_Text _dialogBodyText;
        [SerializeField] private TMP_Text _balanceText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Button _confirmWatchButton;
        [SerializeField] private TMP_Text _confirmWatchLabel;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private TMP_Text _cancelLabel;

        private RewardedAdClient _client;
        private bool _subscribed;
        private bool _iconsApplied;

        public void Bind(RewardedAdClient client)
        {
            Unsubscribe();
            _client = client;
            Subscribe();
            Refresh();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (_confirmDialogRoot != null) _confirmDialogRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Update()
        {
            _client?.Tick();
        }

        public void OpenConfirmDialog()
        {
            if (_client == null || _confirmDialogRoot == null) return;
            var state = _client.Snapshot();
            if (!state.ShouldShowEntry)
            {
                _confirmDialogRoot.SetActive(false);
                return;
            }
            _confirmDialogRoot.SetActive(true);
            Refresh();
        }

        public void CloseConfirmDialog()
        {
            if (_confirmDialogRoot != null) _confirmDialogRoot.SetActive(false);
        }

        private void OnConfirmWatchClicked()
        {
            if (_client == null) return;
            if (_client.StartWatch())
            {
                CloseConfirmDialog();
            }
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (_client != null) _client.Changed += Refresh;
            if (_entryButton != null) _entryButton.onClick.AddListener(OpenConfirmDialog);
            if (_confirmWatchButton != null) _confirmWatchButton.onClick.AddListener(OnConfirmWatchClicked);
            if (_cancelButton != null) _cancelButton.onClick.AddListener(CloseConfirmDialog);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_client != null) _client.Changed -= Refresh;
            if (_entryButton != null) _entryButton.onClick.RemoveListener(OpenConfirmDialog);
            if (_confirmWatchButton != null) _confirmWatchButton.onClick.RemoveListener(OnConfirmWatchClicked);
            if (_cancelButton != null) _cancelButton.onClick.RemoveListener(CloseConfirmDialog);
            _subscribed = false;
        }

        private void Refresh()
        {
            if (_client == null)
            {
                if (_entryRoot != null) _entryRoot.SetActive(false);
                if (_confirmDialogRoot != null) _confirmDialogRoot.SetActive(false);
                return;
            }

            var state = _client.Snapshot();
            if (state.ShouldShowEntry) ApplyIcons();
            if (_entryRoot != null) _entryRoot.SetActive(state.ShouldShowEntry);
            if (_entryLabel != null)
            {
                _entryLabel.text = $"{RewardedAdModel.VideoBadgeText} {RewardedAdModel.EntryButtonText}";
            }
            if (!state.ShouldShowEntry && _confirmDialogRoot != null && _confirmDialogRoot.activeSelf)
            {
                _confirmDialogRoot.SetActive(false);
            }

            if (_dialogTitleText != null) _dialogTitleText.text = RewardedAdModel.DialogTitleText;
            if (_dialogBodyText != null)
            {
                _dialogBodyText.text = RewardedAdModel.BuildDialogBody(
                    state.RewardAmount, state.CurrencyType, state.RemainingToday, state.DailyLimit);
            }
            if (_balanceText != null)
            {
                _balanceText.text = RewardedAdModel.BuildBalanceLine(state.BindGoldBalance, state.GoldBalance);
            }
            if (_statusText != null) _statusText.text = _client.Status;
            if (_confirmWatchButton != null) _confirmWatchButton.interactable = state.CanClickWatch;
            if (_confirmWatchLabel != null) _confirmWatchLabel.text = RewardedAdModel.BuildCooldownButtonText(state.CooldownRemainingSec);
            if (_cancelButton != null) _cancelButton.interactable = true;
            if (_cancelLabel != null) _cancelLabel.text = RewardedAdModel.CancelButtonText;
        }

        private void ApplyIcons()
        {
            if (_iconsApplied) return;
            _iconsApplied = true;
            ApplyOptionalIcon(_entryVideoIcon, RewardedAdModel.VideoIconResourcePath);
            ApplyOptionalIcon(_dialogRewardIcon, MmorpgClient.Game.Common.CurrencyLabels.BindGoldIconResourcePath);
            ApplyOptionalIcon(_balanceBindGoldIcon, MmorpgClient.Game.Common.CurrencyLabels.BindGoldIconResourcePath);
        }

        private static void ApplyOptionalIcon(Image target, string resourcePath)
        {
            if (target == null || target.sprite != null || string.IsNullOrEmpty(resourcePath)) return;
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite == null) return;
            target.sprite = sprite;
            target.preserveAspect = true;
            target.color = Color.white;
        }
    }
}
