using System.Collections.Generic;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Pet
{
    /// <summary>Local art gallery. Selection never sends a summon request or changes server pet identity.</summary>
    public sealed class PetArchivePreview : MonoBehaviour
    {
        private Image _body;
        private TMP_Text _title, _status;
        private readonly Dictionary<string, UiTextButton> _actions = new();
        private QdaoActionResources.Lease _clip;
        private string _id, _direction = "E", _action;
        private float _elapsed;
        public string SelectedId => _id;
        public string ActiveAction => _action;
        public Sprite CurrentSprite => _body != null ? _body.sprite : null;

        public void Build()
        {
            var root = (RectTransform)transform;
            var plate = root.gameObject.AddComponent<Image>();
            plate.color = PetUiStyle.WindowPaper;
            plate.raycastTarget = true;
            QdaoUguiFactory.CreateText("GalleryTitle", root, 36f, 20f, 1240f, 52f,
                "宝宝图鉴 · 本地外观预览", 38f, PetUiStyle.FieldLabel);
            var close = Button("CloseGallery", 1730f, 20f, 150f, 52f, "关闭");
            close.Button.onClick.AddListener(() => gameObject.SetActive(false));
            int index = 0;
            foreach (var entry in QdaoPetCatalog.Entries)
            {
                if (entry == null) continue;
                var id = entry.id;
                int row = index / 4, col = index % 4;
                var item = Button("Pet_" + id, 36f + col * 238f, 94f + row * 132f, 222f, 116f, entry.displayName);
                item.Label.rectTransform.anchoredPosition = new Vector2(42f, 0f);
                item.Label.rectTransform.sizeDelta = new Vector2(164f, 116f);
                item.Label.fontSize = 26f;
                var portrait = QdaoUguiFactory.CreateImage("Portrait", item.Rect, 3f, 18f, 80f, 80f, QdaoPetCatalog.LoadPortrait(id));
                portrait.preserveAspect = true;
                item.Button.onClick.AddListener(() => Select(id));
                index++;
            }
            _title = QdaoUguiFactory.CreateText("SelectedPet", root, 1030f, 94f, 780f, 48f,
                string.Empty, 36f, PetUiStyle.FieldLabel, TextAlignmentOptions.Center);
            _body = QdaoUguiFactory.CreateImage("PetPreviewBody", root, 1110f, 140f, 620f, 500f, null);
            _body.preserveAspect = true;
            _body.raycastTarget = false;
            var east = Button("FaceEast", 1140f, 644f, 240f, 58f, "朝右 E");
            var west = Button("FaceWest", 1420f, 644f, 240f, 58f, "朝左 W");
            east.Button.onClick.AddListener(() => Face("E"));
            west.Button.onClick.AddListener(() => Face("W"));
            string[] actions = { "idle", "attack", "hit", "cast" };
            string[] labels = { "待机", "普攻", "受击", "施法" };
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                var button = Button("Preview_" + action, 1028f + i * 204f, 716f, 190f, 60f, labels[i]);
                button.Button.onClick.AddListener(() => Play(action));
                _actions.Add(action, button);
            }
            _status = QdaoUguiFactory.CreateText("DeliveryStatus", root, 36f, 782f, 1810f, 42f,
                string.Empty, 24f, PetUiStyle.HintText);
            if (QdaoPetCatalog.Entries.Count > 0) _id = QdaoPetCatalog.Entries[0].id;
            gameObject.SetActive(false);
        }

        private UiTextButton Button(string name, float x, float y, float w, float h, string label)
            => BattleUiWidgets.CreateTextButton(name, transform, x, y, w, h, label, 28f,
                PetUiStyle.ActionPlate, PetUiStyle.ActionText);

        public void Select(string id)
        {
            var entry = QdaoPetCatalog.Find(id);
            if (entry == null) return;
            Clear();
            _id = id;
            if (_title != null) _title.text = entry.displayName;
            RefreshActions();
            Play("idle");
        }

        private void Face(string direction)
        {
            Clear();
            _direction = direction;
            RefreshActions();
            Play("idle");
        }

        private void RefreshActions()
        {
            var ready = new List<string>();
            var missing = new List<string>();
            foreach (var item in _actions)
            {
                bool available = QdaoPetCatalog.HasAction(_id, item.Key, _direction);
                item.Value.SetInteractable(available);
                (available ? ready : missing).Add(item.Value.Label.text);
            }
            if (_status != null) _status.text = "本地预览，不改变服务器宝宝。  已交付：" + string.Join("、", ready) +
                (missing.Count > 0 ? "   待交付：" + string.Join("、", missing) : string.Empty);
        }

        public bool Play(string action)
        {
            var next = QdaoPetCatalog.Acquire(_id, action, _direction);
            if (next == null) return false;
            var previous = _clip;
            _clip = next;
            _action = action;
            _elapsed = 0f;
            _body.sprite = next.Frames[0];
            _body.enabled = true;
            previous?.Dispose();
            return true;
        }

        private void Update()
        {
            if (_clip == null) return;
            _elapsed += Time.unscaledDeltaTime;
            if (_action != "idle" && _elapsed >= _clip.DurationSeconds) Play("idle");
            if (_clip != null) _body.sprite = _clip.FrameAt(_elapsed, _action == "idle");
        }

        private void OnEnable() { if (_body != null) Select(_id); }
        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();
        private void Clear()
        {
            if (_body != null) { _body.sprite = null; _body.enabled = false; }
            var previous = _clip;
            _clip = null;
            _action = null;
            previous?.Dispose();
        }
    }
}
