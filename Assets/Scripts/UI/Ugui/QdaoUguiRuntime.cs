using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui
{
    /// <summary>Creates the persistent Unity Canvas used by the uGUI migration.</summary>
    public sealed class QdaoUguiRuntime
    {
        private const string PrefabResourcePath = "UI/Ugui/Prefabs/QdaoServerSelect";

        private readonly GameObject _root;
        private readonly QdaoServerSelectView _view;

        private QdaoUguiRuntime(GameObject root, QdaoServerSelectView view)
        {
            _root = root;
            _view = view;
        }

        public static QdaoUguiRuntime Create(AppBootstrap app)
        {
            var createdEventSystem = EnsureEventSystem(app.transform);
            GameObject root = null;
            try
            {
                root = new GameObject(
                    "[QdaoUgui]",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                root.transform.SetParent(app.transform, false);

                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                canvas.pixelPerfect = true;

                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(QdaoUguiTheme.DesignWidth, QdaoUguiTheme.DesignHeight);
                // Expand uses the smaller scale factor, so the complete 2560x1080
                // interaction canvas remains visible on 16:9, 21:9 and 4K views.
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.referencePixelsPerUnit = 100f;

                var prefab = Resources.Load<GameObject>(PrefabResourcePath);
                if (prefab == null)
                    throw new FileNotFoundException(
                        "The baked qdao uGUI prefab is required in production. " +
                        "Run MMORPG/UI/Build qdao uGUI vertical slice before Play Mode.",
                        $"Resources/{PrefabResourcePath}.prefab");

                var instance = Object.Instantiate(prefab, root.transform, false);
                var view = instance.GetComponent<QdaoServerSelectView>();
                if (view == null)
                    throw new MissingReferenceException(
                        $"Resources/{PrefabResourcePath}.prefab has no QdaoServerSelectView component.");
                view.name = "QdaoServerSelectUgui";

                view.Initialize(app);
                Debug.Log("[AppBootstrap] uGUI server-select vertical slice active");
                return new QdaoUguiRuntime(root, view);
            }
            catch
            {
                DisableAndDestroy(root);
                DisableAndDestroy(createdEventSystem);
                throw;
            }
        }

        public void Tick(float deltaTime)
        {
            if (_root != null && _view != null)
                _view.Tick(deltaTime);
        }

        private static GameObject EnsureEventSystem(UnityEngine.Transform parent)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return null;

            var eventSystemObject = new GameObject(
                "[EventSystem]",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(parent, false);
            return eventSystemObject;
        }

        private static void DisableAndDestroy(GameObject target)
        {
            if (target == null)
                return;

            target.SetActive(false);
            Object.Destroy(target);
        }
    }
}
