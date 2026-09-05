using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MmorpgClient.UI.Ugui.Battle
{
    /// <summary>命令环的七个命令(顺序即环上从正上方起顺时针的排布)。</summary>
    public enum BattleCommand
    {
        Attack,
        Spell,
        Defend,
        Item,
        Summon,
        Flee,
        Auto,
    }

    /// <summary>自动战斗中的三键。</summary>
    public enum AutoBattleCommand
    {
        Character,
        Pet,
        CancelAuto,
    }

    /// <summary>
    /// 右下问道式命令环(turn-battle-presentation.md §4):
    ///  - UI/command_ring.png 做底,攻击/法术/防御/道具/召唤/逃跑/自动 七键圆形排布(BattleHudLogic.RingPosition);
    ///  - 召唤预留置灰;PVP 逃跑置灰;不可行动时除"自动"外全部置灰;
    ///  - 自动战斗中整环换成"角色 / 宠物 / 取消自动"三键(金框方形)。
    /// </summary>
    public sealed class BattleCommandRing
    {
        public const float CenterX = 2290f;
        public const float CenterY = 850f;
        public const float Radius = 160f;
        public const float RingSize = 470f;
        public const float ButtonSize = 108f;
        public const float AutoButtonSize = 120f;

        /// <summary>自动三键的顶边 y 与键距(设计坐标)。</summary>
        public const float AutoKeysTop = 920f;
        public const float AutoKeyGap = 16f;
        public const int AutoKeyCount = 3;

        private static readonly string[] Labels = { "攻击", "法术", "防御", "道具", "召唤", "逃跑", "自动" };

        /// <summary>命令环第 index 键的矩形(设计坐标,y 向下)。</summary>
        public static Rect ButtonRect(int index)
        {
            var offset = BattleHudLogic.RingPosition(index, BattleHudLogic.CommandCount, Radius);
            return new Rect(CenterX + offset.x - ButtonSize * 0.5f, CenterY + offset.y - ButtonSize * 0.5f, ButtonSize, ButtonSize);
        }

        /// <summary>自动战斗三键第 index 键的矩形(设计坐标,y 向下;右缘留 40)。</summary>
        public static Rect AutoKeyRect(int index)
        {
            float x0 = QdaoUguiTheme.DesignWidth - 40f - AutoKeyCount * AutoButtonSize - (AutoKeyCount - 1) * AutoKeyGap;
            return new Rect(x0 + index * (AutoButtonSize + AutoKeyGap), AutoKeysTop, AutoButtonSize, AutoButtonSize);
        }

        private readonly RectTransform _ringRoot;
        private readonly RectTransform _autoRoot;
        private readonly List<UiTextButton> _buttons = new List<UiTextButton>();
        private readonly List<UiTextButton> _autoButtons = new List<UiTextButton>();
        private bool _visible = true;
        private bool _autoMode;

        public bool AutoMode => _autoMode;
        public bool IsVisible => _visible;

        public BattleCommandRing(UnityEngine.Transform parent, Action<BattleCommand> onCommand, Action<AutoBattleCommand> onAutoCommand)
        {
            _ringRoot = QdaoUguiFactory.CreateRect("CommandRing", parent, CenterX - RingSize * 0.5f, CenterY - RingSize * 0.5f, RingSize, RingSize);
            var ringSprite = BattleArtCatalog.LoadUiSprite("command_ring");
            var baseImage = QdaoUguiFactory.CreateImage("RingBase", _ringRoot, 0f, 0f, RingSize, RingSize, ringSprite ?? BattleArtCatalog.CircleSprite);
            baseImage.preserveAspect = true;
            if (ringSprite == null) baseImage.color = new Color(0.16f, 0.12f, 0.08f, 0.75f);

            var plateSprite = BattleArtCatalog.CircleSprite;
            for (int i = 0; i < Labels.Length; i++)
            {
                // 环根左上在 (CenterX − RingSize/2, CenterY − RingSize/2):把设计坐标矩形换成环内局部坐标
                var rect = ButtonRect(i);
                float x = rect.x - (CenterX - RingSize * 0.5f);
                float y = rect.y - (CenterY - RingSize * 0.5f);
                var command = (BattleCommand)i;
                var button = BattleUiWidgets.CreateTextButton($"Cmd_{command}", _ringRoot, x, y, ButtonSize, ButtonSize,
                    Labels[i], 26f, command == BattleCommand.Attack ? BattleUiStyle.ButtonPlateAccent : BattleUiStyle.ButtonPlate,
                    BattleUiStyle.ButtonText);
                button.Plate.sprite = plateSprite;
                button.Plate.preserveAspect = true;
                button.Label.fontStyle = FontStyles.Bold;
                button.Button.onClick.AddListener(() => onCommand?.Invoke(command));
                _buttons.Add(button);
            }

            // 自动战斗三键(金框方形),横排在右下
            _autoRoot = QdaoUguiFactory.CreateRect("AutoKeys", parent, 0f, 0f, QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
            string[] autoLabels = { "角色", "宠物", "取消自动" };
            for (int i = 0; i < autoLabels.Length; i++)
            {
                var command = (AutoBattleCommand)i;
                var rect = AutoKeyRect(i);
                float x = rect.x;
                var frame = BattleUiWidgets.CreatePanel($"AutoFrame_{command}", _autoRoot, x - 4f, rect.y - 4f, AutoButtonSize + 8f, AutoButtonSize + 8f,
                    new Color(0.93f, 0.76f, 0.32f, 0.95f), false);
                var button = BattleUiWidgets.CreateTextButton($"Auto_{command}", _autoRoot, x, rect.y, AutoButtonSize, AutoButtonSize,
                    autoLabels[i], 24f, command == AutoBattleCommand.CancelAuto ? BattleUiStyle.ButtonPlateAccent : BattleUiStyle.ButtonPlate,
                    BattleUiStyle.ButtonText);
                BattleRoundCounter.ApplyNineSlice(button.Plate, "button_9slice");
                button.Label.fontStyle = FontStyles.Bold;
                button.Button.onClick.AddListener(() => onAutoCommand?.Invoke(command));
                frame.raycastTarget = false; // 金框先建即在按钮之下,只做底
                _autoButtons.Add(button);
            }
            _autoRoot.gameObject.SetActive(false);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            ApplyVisibility();
        }

        /// <summary>自动战斗中:环隐藏、三键显示。</summary>
        public void SetAutoMode(bool autoMode)
        {
            _autoMode = autoMode;
            ApplyVisibility();
        }

        /// <summary>
        /// 可用态:canAct 影响攻击/法术/防御/道具;canFlee 单独控制逃跑;召唤恒灰;
        /// autoInteractable 控制"自动"键,autoOn 决定其文案。
        /// </summary>
        public void SetState(bool canAct, bool canFlee, bool autoInteractable, bool autoOn)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                var command = (BattleCommand)i;
                bool interactable;
                switch (command)
                {
                    case BattleCommand.Summon: interactable = false; break;
                    case BattleCommand.Flee: interactable = canAct && canFlee; break;
                    case BattleCommand.Auto: interactable = autoInteractable; break;
                    default: interactable = canAct; break;
                }
                _buttons[i].SetInteractable(interactable);
            }
            _buttons[(int)BattleCommand.Auto].SetText(autoOn ? "自动:开" : "自动");
        }

        public void SetAutoKeysInteractable(bool interactable)
        {
            foreach (var button in _autoButtons) button.SetInteractable(interactable);
        }

        private void ApplyVisibility()
        {
            bool ring = _visible && !_autoMode;
            bool auto = _visible && _autoMode;
            if (_ringRoot != null && _ringRoot.gameObject.activeSelf != ring) _ringRoot.gameObject.SetActive(ring);
            if (_autoRoot != null && _autoRoot.gameObject.activeSelf != auto) _autoRoot.gameObject.SetActive(auto);
        }
    }
}
