using System.Collections;
using System;
using FairyGUI;
using MmorpgClient.Game;
using MmorpgClient.Net;
using MmorpgClient.UI.Screens;
using MmorpgClient.UI.Ugui;
using MmorpgClient.World.Tianyong;
using UnityEngine;

namespace MmorpgClient.UI
{
    /// <summary>
    /// Single entry point for the production client. Normally serialized in
    /// the AppRoot prefab, with auto-spawn retained for empty test scenes.
    /// Bootstraps the selected UI runtime,
    /// owns the long-lived <see cref="GameClient"/> + <see cref="GatewayHttpClient"/>
    /// and drives the screen stack via <see cref="Router"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppBootstrap : MonoBehaviour
    {
        public const string GameObjectName = "[MmorpgClient]";

        private static AppBootstrap _instance;

        [SerializeField]
        [Tooltip("Use the native Unity uGUI migration. Disable only to compare the legacy FairyGUI implementation.")]
        private bool useUgui = true;

        [Header("Persistent scene rig")]
        [SerializeField] private Camera worldCamera;
        [SerializeField] private Light directionalSun;
        [SerializeField] private TianyongMapConfig mapConfig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            // The sandbox owns its own camera, light and offline player. A
            // production client root would cover it with login UI and make
            // scene tests depend on network state.
            if (FindAnyObjectByType<TianyongSandboxBootstrap>() != null) return;
            if (FindAnyObjectByType<AppBootstrap>() != null) return;
            var go = new GameObject(GameObjectName);
            go.AddComponent<AppBootstrap>();
        }

        public static AppBootstrap Instance => _instance;

        public SessionModel Session { get; private set; }
        public GameClient   GameClient { get; private set; }
        public GatewayHttpClient Gateway { get; private set; }
        public ScreenRouter Router { get; private set; }
        public QdaoUguiRuntime Ugui { get; private set; }
        public TianyongMapRuntime WorldMap { get; private set; }
        public bool QdaoPackageLoaded { get; private set; }

        private GComponent _root;
        private GComponent _host;
        private Action<string> _gameLogHandler;
        private UnityEngine.Transform _actorWorldRoot;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                var duplicateRoot = transform.root;
                if (_instance.transform.root == duplicateRoot) Destroy(this);
                else Destroy(duplicateRoot.gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(transform.root.gameObject);
            EnsureSceneRig();

            if (mapConfig == null) mapConfig = TianyongMapConfig.LoadDefault();

            Session    = new SessionModel();  // gateway URL / last account come from ClientSettings (PlayerPrefs)
            Gateway    = new GatewayHttpClient(Session.GatewayBaseUrl);
            GameClient = new GameClient(Session.GatewayBaseUrl);
            GameClient.World.SetRootParent(_actorWorldRoot);
            GameClient.CoroutineRunner = Run; // RedirectToGateNotify 重连流程需要宿主协程
            _gameLogHandler = s => Debug.Log("[GameClient] " + s);
            GameClient.OnLog += _gameLogHandler;
            WorldMap = GetComponent<TianyongMapRuntime>();
            if (WorldMap == null) WorldMap = gameObject.AddComponent<TianyongMapRuntime>();
            WorldMap.Initialize(GameClient, worldCamera, directionalSun, mapConfig);

            if (useUgui)
            {
                Debug.Log("[AppBootstrap] Awake (uGUI migration mode)");
                try
                {
                    Ugui = QdaoUguiRuntime.Create(this);
                    // 选角/建角界面:挂接 GameClient.PlayerChooser,EnterZone 管线
                    // 在 TCP Login 后弹出(见 RoleFlowUi)
                    MmorpgClient.UI.Ugui.Role.RoleFlowUi.Attach(this);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    Debug.LogError(
                        "[AppBootstrap] Native uGUI bootstrap failed. " +
                        "Destroying the incomplete persistent root; legacy fallback is disabled.");
                    enabled = false;
                    // Do not leave a disabled DontDestroyOnLoad singleton that
                    // blocks a clean bootstrap after a scene reload. OnDestroy
                    // owns the normal client/event cleanup and clears _instance.
                    Destroy(transform.root.gameObject);
                    return;
                }
            }

            Debug.Log("[AppBootstrap] Awake (FairyGUI compatibility mode)");
            EnsureFairyGUIStage();
            ExcludeFairyGuiLayerFromWorldCameras();
            TryLoadUiPackage();

            BuildRoot();
            Router = new ScreenRouter(this, _host);
            GRoot.inst.onSizeChanged.Add(OnRootResize);

            // Boot directly into the V3 login flow. The router stack is
            // LoginV3Screen → ServersV3Screen → SceneV3Screen — see those
            // classes for the layout contracts with the FairyGUI components.
            Router.Show<LoginV3Screen>();
        }

        private void Update()
        {
            GameClient?.Tick();
            GameClient?.World?.Tick();
            Ugui?.Tick(Time.deltaTime);
            Router?.Tick(Time.deltaTime);
        }

        public Coroutine Run(IEnumerator routine) => StartCoroutine(routine);

        // ───────────────────────────────────────────────────────────

        private void EnsureFairyGUIStage()
        {
            // Touching Stage.inst lazily creates the FairyGUI stage if absent.
            // Touch GRoot.inst to ensure the root container exists.
            _ = Stage.inst;
            _ = GRoot.inst;

            // Default font for ALL text nodes that don't explicitly set a font.
            // FairyGUI looks up the first name via Resources.Load<Font>("Fonts/<name>")
            // — we ship Assets/Resources/Fonts/SimKai.ttf so this resolves on every
            // platform, regardless of what fonts the user has installed. Subsequent
            // names act as graceful fallbacks if the bundled .ttf is ever stripped.
            //
            // This must be set BEFORE any text-bearing UIPackage is loaded; once a
            // GTextField has captured the old default it won't re-read this value.
            UIConfig.defaultFont = Theme.BodyFontName;

            // Without a UIContentScaler, GRoot reports raw pixel size, so the
            // qdao screens authored at 2560x1080 get resized to the window's
            // pixel dimensions while their child art keeps the design-space
            // positions/sizes -> wrong placement and wildly wrong scale.
            // Configure a scaler so 2560x1080 maps consistently to the window.
            var stageGo = Stage.inst.gameObject;
            var scaler = stageGo.GetComponent<UIContentScaler>();
            if (scaler == null)
                scaler = stageGo.AddComponent<UIContentScaler>();
            scaler.scaleMode = UIContentScaler.ScaleMode.ScaleWithScreenSize;
            scaler.designResolutionX = (int)Theme.Art.ReferenceWidth;
            scaler.designResolutionY = (int)Theme.Art.ReferenceHeight;

            // The qdao art is authored as one fixed 2560x1080 widescreen
            // composition. Always matching width preserves the whole frame in
            // narrower Unity Game views instead of cropping the right side.
            // Extra vertical space is acceptable letterbox room; losing the
            // character and right panel is not.
            scaler.screenMatchMode = UIContentScaler.ScreenMatchMode.MatchWidth;
            scaler.ApplyChange();
            GRoot.inst.ApplyContentScaleFactor();

            // Sharpening pass — combats the residual blur you get whenever the
            // window's pixel size is not an integer multiple of the design
            // resolution (1920x1080 vs 2560x1080 = 0.75x, 4K = 1.5x, etc.):
            //
            // 1. pixelPerfect on GRoot snaps every child's final transform to
            //    integer pixels, so sprite edges land on texel boundaries
            //    instead of being bilinear-blended across two screen pixels.
            // 2. 4x MSAA on the global QualitySettings smooths the residual
            //    sub-pixel jitter on rotated / non-axis-aligned art (badges).
            //    The Very-High/Ultra quality levels in ProjectSettings have
            //    been bumped to antiAliasing=4 so this is now the asset
            //    default, not just a runtime override.
            // 3. anisoLevel=ForceEnable lets the atlas's aniso=2 setting
            //    actually take effect at runtime.
            // 4. mipmap bias -0.5 (also set on the atlas .meta) keeps the
            //    sampler one mip-level sharper than Unity's auto-choice
            //    when the UI is shrunk by a fractional factor.
            GRoot.inst.displayObject.pixelPerfect = true;
            QualitySettings.antiAliasing = Mathf.Max(QualitySettings.antiAliasing, 4);
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            QualitySettings.globalTextureMipmapLimit = 0;
        }

        private void TryLoadUiPackage()
        {
            if (UIPackage.GetByName(Theme.UiPackageName) != null) return;
            try
            {
                UIPackage.AddPackage(Theme.UiPackagePath);
                QdaoPackageLoaded = true;
                Debug.Log($"[AppBootstrap] loaded FairyGUI package: {Theme.UiPackagePath}");
            }
            catch (Exception ex)
            {
                QdaoPackageLoaded = false;
                Debug.LogWarning($"[AppBootstrap] FairyGUI package not found, fallback to code UI. path={Theme.UiPackagePath}, err={ex.Message}");
            }
        }

        private void BuildRoot()
        {
            // UIContentScaler (configured in EnsureFairyGUIStage) already maps
            // the 2560x1080 design space to the actual window with a single
            // uniform scale + letterbox via MatchWidth. We MUST NOT
            // apply a second SetScale on _root, otherwise every sprite goes
            // through two non-integer scales and lands on sub-pixel offsets,
            // which is exactly what makes the entire UI look blurry.
            //
            // _root / _host are now 1:1 children of GRoot, sized to the
            // design canvas. GRoot.width/height already report design units
            // (2560 x 1080-ish, varying with window aspect ratio), so screens
            // can lay themselves out in design coordinates with no scaling
            // applied here.
            _root = new GComponent();
            _root.SetSize(GRoot.inst.width, GRoot.inst.height);
            GRoot.inst.AddChild(_root);

            _host = new GComponent();
            _host.SetSize(GRoot.inst.width, GRoot.inst.height);
            _root.AddChild(_host);

            FitRootToScreen();
        }

        private void OnRootResize()
        {
            FitRootToScreen();
            Router?.OnRootResize();
        }

        /// <summary>
        /// Resize the design canvas to whatever GRoot currently reports.
        /// UIContentScaler handles the actual pixel scaling — we only keep
        /// _root / _host the same size as GRoot so screens fill it cleanly.
        /// No SetScale here on purpose (see BuildRoot for the rationale).
        /// </summary>
        private void FitRootToScreen()
        {
            if (_root == null) return;
            _root.SetSize(GRoot.inst.width, GRoot.inst.height);
            _root.SetXY(0f, 0f);
            if (_host != null)
                _host.SetSize(GRoot.inst.width, GRoot.inst.height);
        }

        private void EnsureSceneRig()
        {
            if (worldCamera == null)
                worldCamera = GetComponentInChildren<Camera>(true) ?? Camera.main;
            if (worldCamera == null)
            {
                var camGo = new GameObject("[MainCamera]");
                camGo.tag = "MainCamera";
                camGo.transform.SetParent(transform, false);
                worldCamera = camGo.AddComponent<Camera>();
                worldCamera.clearFlags = CameraClearFlags.SolidColor;
                worldCamera.backgroundColor = new Color(0.20f, 0.23f, 0.19f);
                worldCamera.transform.position = new UnityEngine.Vector3(0f, 4f, -10f);
                worldCamera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
                camGo.AddComponent<AudioListener>();
            }
            else
            {
                AdoptIntoPersistentRoot(worldCamera.transform);
                if (FindAnyObjectByType<AudioListener>() == null)
                    worldCamera.gameObject.AddComponent<AudioListener>();
            }

            if (directionalSun == null)
            {
                foreach (var light in GetComponentsInChildren<Light>(true))
                {
                    if (light.type != LightType.Directional) continue;
                    directionalSun = light;
                    break;
                }
            }
            if (directionalSun == null)
            {
                foreach (var light in FindObjectsByType<Light>())
                {
                    if (light.type != LightType.Directional) continue;
                    directionalSun = light;
                    break;
                }
            }
            if (directionalSun == null)
            {
                var lightGo = new GameObject("[DirLight]");
                lightGo.transform.SetParent(transform, false);
                directionalSun = lightGo.AddComponent<Light>();
                directionalSun.type = LightType.Directional;
                directionalSun.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(40f, 30f, 0f);
            }
            else
            {
                AdoptIntoPersistentRoot(directionalSun.transform);
            }

            _actorWorldRoot = transform.Find("[ActorWorld]");
            if (_actorWorldRoot == null)
            {
                var existing = GameObject.Find("[ActorWorld]");
                _actorWorldRoot = existing != null ? existing.transform : null;
            }
            if (_actorWorldRoot == null)
            {
                var actorRootObject = new GameObject("[ActorWorld]");
                actorRootObject.transform.SetParent(transform, false);
                _actorWorldRoot = actorRootObject.transform;
            }
            else
            {
                AdoptIntoPersistentRoot(_actorWorldRoot);
            }
        }

        private void AdoptIntoPersistentRoot(UnityEngine.Transform child)
        {
            if (child == null || child == transform || child.IsChildOf(transform)) return;
            child.SetParent(transform, true);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;

            StopAllCoroutines();
            if (_root != null)
            {
                GRoot.inst.onSizeChanged.Remove(OnRootResize);
                _root.Dispose();
                _root = null;
                _host = null;
            }

            if (GameClient != null)
            {
                GameClient.CoroutineRunner = null;
                if (_gameLogHandler != null) GameClient.OnLog -= _gameLogHandler;
                GameClient.Disconnect();
            }

            _gameLogHandler = null;
            WorldMap = null;
            Ugui = null;
            Router = null;
            GameClient = null;
            Gateway = null;
            Session = null;
            _actorWorldRoot = null;
        }

        private static void ExcludeFairyGuiLayerFromWorldCameras()
        {
            int uiLayer = LayerMask.NameToLayer(StageCamera.LayerName);
            if (uiLayer < 0) return;

            int uiMask = 1 << uiLayer;
            foreach (var camera in FindObjectsByType<Camera>())
            {
                if (camera == null || camera.GetComponent<StageCamera>() != null) continue;
                camera.cullingMask &= ~uiMask;
            }
        }
    }
}
