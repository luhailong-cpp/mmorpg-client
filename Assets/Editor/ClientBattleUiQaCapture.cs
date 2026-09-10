#if UNITY_EDITOR
using System;
using System.IO;
using MmorpgClient.Game.Battle;
using MmorpgClient.UI.Ugui;
using MmorpgClient.UI.Ugui.Battle;
using MmorpgClient.UI.Ugui.Tweening;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Disconnected captures of production battle controls for the official Unity MCP.</summary>
public static class ClientBattleUiQaCapture
{
    private const int Width = 2560;
    private const int Height = 1080;
    private const int CaptureLayer = 31;
    private static string OutputDirectory => ClientUiRefreshVerification.OutputDirectory;
    private static readonly string[] Names =
    {
        "08-battle-native", "09-queue-native", "10-spectate-native", "11-result-native",
    };

    private static Scene s_previousScene;
    private static Scene s_captureScene;
    private static GameObject s_root;
    private static RectTransform s_canvasRoot;
    private static RectTransform s_page;
    private static Camera s_camera;
    private static RenderTexture s_renderTarget;
    private static Texture2D s_readback;
    private static BattleScreen s_battle;
    private static BattleQueuePanel s_queue;
    private static SpectatePanel s_spectate;
    private static BattleResultPanel s_result;
    private static int s_step;
    private static double s_lastUpdate;
    private static float s_stageAge;
    private static bool s_clockActive;

    public static bool IsRunning { get; private set; }
    public static string Status { get; private set; } = "idle";

    public static void CaptureAll() => StartCaptureAll();

    public static void StartCaptureAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("ClientBattleUiQaCapture requires Edit Mode; Play Mode was not changed.");
        if (IsRunning) throw new InvalidOperationException("A battle UI capture is already running.");
        IsRunning = true;
        Status = "starting";
        try
        {
            Directory.CreateDirectory(OutputDirectory);
            s_previousScene = SceneManager.GetActiveScene();
            s_captureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(s_captureScene);
            s_root = new GameObject("[BattleUiQaCapture]") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(s_root, s_captureScene);
            var cameraObject = new GameObject("CaptureCamera") { hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(s_root.transform, false);
            cameraObject.transform.localPosition = new UnityEngine.Vector3(0f, 0f, -10f);
            s_camera = cameraObject.AddComponent<Camera>();
            s_camera.enabled = false;
            s_camera.orthographic = true;
            s_camera.orthographicSize = 5f;
            s_camera.nearClipPlane = 0.1f;
            s_camera.farClipPlane = 100f;
            s_camera.cullingMask = 1 << CaptureLayer;
            s_camera.clearFlags = CameraClearFlags.SolidColor;
            s_camera.backgroundColor = QdaoRefreshArt.Jade;
            s_renderTarget = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            s_renderTarget.name = "BattleUiQaRenderTarget";
            s_renderTarget.Create();
            s_camera.targetTexture = s_renderTarget;
            s_readback = new Texture2D(Width, Height, TextureFormat.RGB24, false, false);

            var canvasObject = new GameObject("CaptureCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(s_root.transform, false);
            s_canvasRoot = (RectTransform)canvasObject.transform;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = s_camera;
            canvas.planeDistance = 2f;
            canvas.sortingOrder = 500;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Width, Height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;
            s_step = 0;
            PreparePage();
            s_lastUpdate = EditorApplication.timeSinceStartup;
            EditorApplication.update += Advance;
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.playModeStateChanged += HandlePlayModeChange;
            Debug.Log("BATTLE_QA_STARTED|EditMode|offline|temporary-scene");
        }
        catch
        {
            Status = "failed during setup";
            Cleanup();
            throw;
        }
    }

    public static void Cancel()
    {
        if (!IsRunning) return;
        Status = "cancelled";
        Cleanup();
    }

    private static void HandlePlayModeChange(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) Cancel();
    }

    private static void PreparePage()
    {
        ClearPage();
        RealtimeTween.BeginEditorCapture();
        s_clockActive = true;
        s_page = QdaoUguiFactory.CreateCenteredRect(Names[s_step], s_canvasRoot, Width, Height);
        var background = s_step == 1 || s_step == 2
            ? QdaoRefreshArt.Load("sanctuary_background") : BattleUiStyle.ResolveArenaBackground();
        if (background != null)
            QdaoUguiFactory.CreateAspectFillImage("Background", s_page, background,
                background.rect.width / background.rect.height);

        switch (s_step)
        {
            case 0:
                s_battle = new BattleScreen(null, s_page);
                s_battle.Open(CreateBattleState(), 10001UL);
                s_battle.OnPhaseChanged(BattlePhase.WaitingAction);
                s_battle.Tick();
                break;
            case 1:
                s_queue = new BattleQueuePanel(null, s_page);
                s_queue.Show();
                s_queue.SetQueueing(true);
                s_queue.ApplyStatus(new Match.GetQueueStatusResponse
                {
                    State = Match.QueueState.Queued, QueuedSeconds = 12, EstimatedWaitSeconds = 30,
                });
                s_queue.Tick();
                break;
            case 2:
                s_spectate = new SpectatePanel(null, s_page);
                var list = new Match.ListWatchableBattlesResponse();
                for (int i = 0; i < 3; i++)
                {
                    var summary = new Match.BattleWatchSummary
                    {
                        BattleId = (ulong)(990001 + i),
                        Mode = i == 0 ? Match.MatchMode._5V5 : i == 1 ? Match.MatchMode._1V1 : Match.MatchMode.PveTeam,
                        CreatedAtMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (ulong)(90000 + i * 45000),
                    };
                    summary.PlayerNames.Add("青云道友");
                    summary.PlayerNames.Add("清铃");
                    list.Battles.Add(summary);
                }
                s_spectate.ApplyList(list);
                s_spectate.Show();
                break;
            case 3:
                BattleUiWidgets.CreateStretchPanel("ModalDim", s_page, BattleUiStyle.ModalDim);
                s_result = new BattleResultPanel(s_page, () => { });
                s_result.Show(new BattleEndS2C
                {
                    BattleId = 990001UL,
                    Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                    Settlement = new BattleSettlementData
                    {
                        BattleId = 990001UL, PlayerId = 10001UL, PlayerTeamIndex = 0u,
                        Outcome = eBattleOutcome.BattleOutcomeSideAWin,
                        TotalRounds = 3u, ExpGain = 12800UL, GoldGain = 3460UL,
                        Health = 1500UL, Mana = 480UL,
                        ItemsGained = { new BattleItemEntry { ItemTableId = 2001u, Count = 3u } },
                    },
                }, 0u);
                break;
        }
        foreach (var child in s_root.GetComponentsInChildren<UnityEngine.Transform>(true))
            child.gameObject.layer = CaptureLayer;
        foreach (var canvas in s_root.GetComponentsInChildren<Canvas>(true))
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = s_camera;
            canvas.planeDistance = 2f;
        }
        s_stageAge = 0f;
        Status = "waiting for " + Names[s_step];
        Canvas.ForceUpdateCanvases();
    }

    private static BattleStateS2C CreateBattleState()
    {
        var state = new BattleStateS2C
        {
            BattleId = 990001UL, RoundIndex = 3u,
            ActionDeadlineMs = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000UL,
            Outcome = eBattleOutcome.BattleOutcomeOngoing,
        };
        for (uint team = 0; team < 2; team++)
        for (uint slot = 0; slot < 5; slot++)
        {
            var actor = new BattleActorState
            {
                ActorId = 10001UL + team * 100UL + slot,
                ActorType = eBattleActorType.BattleActorTypePlayer,
                TeamIndex = team,
                Name = (team == 0 ? "青云道友" : "赤霞道友") + (slot + 1),
                Level = 28u, FormationSlot = slot,
                MaxHealth = 1800UL, MaxMana = 600UL,
                Attributes = new BaseAttributesComp
                {
                    Health = 1500UL + slot * 40UL, Mana = 480UL,
                    Speed = 300UL - slot * 15UL,
                },
                SkillTableIds = { 1101u, 1207u, 1305u },
            };
            state.Actors.Add(actor);
        }
        state.PendingActorIds.Add(10001UL);
        return state;
    }

    private static void Advance()
    {
        if (!IsRunning) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Cancel(); return; }
        try
        {
            double now = EditorApplication.timeSinceStartup;
            // Count animation time, rather than blocking the editor for a sleep.
            float delta = Mathf.Clamp((float)(now - s_lastUpdate), 0f, 0.1f);
            s_lastUpdate = now;
            RealtimeTween.AdvanceEditorCapture(delta);
            s_battle?.Tick();
            s_queue?.Tick();
            s_stageAge += delta;
            EditorApplication.QueuePlayerLoopUpdate();
            if (s_stageAge < 2f) return;
            CapturePage();
            s_step++;
            if (s_step == Names.Length)
            {
                Status = "complete: 4 disconnected production UI captures";
                Cleanup();
                Debug.Log("BATTLE_QA_COMPLETE|COUNT=4|NETWORK=disconnected|SCENE=restored");
            }
            else PreparePage();
        }
        catch (Exception exception)
        {
            Status = "failed: " + exception.Message;
            Debug.LogException(exception);
            Cleanup();
        }
    }

    private static void CapturePage()
    {
        Canvas.ForceUpdateCanvases();
        foreach (var text in s_root.GetComponentsInChildren<TMP_Text>(true))
            text.ForceMeshUpdate(true, true);
        Canvas.ForceUpdateCanvases();
        var previous = RenderTexture.active;
        try
        {
            s_camera.Render();
            RenderTexture.active = s_renderTarget;
            s_readback.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
            s_readback.Apply(false, false);
            string path = Path.Combine(OutputDirectory, Names[s_step] + ".png");
            File.WriteAllBytes(path, s_readback.EncodeToPNG());
            Debug.Log("CAPTURE_OK|" + path);
        }
        finally { RenderTexture.active = previous; }
    }

    private static void ClearPage()
    {
        // Production Close uses delayed Destroy; Edit Mode instead tears down the
        // entire isolated canvas after removing only its newly created tweens.
        if (s_clockActive)
        {
            RealtimeTween.EndEditorCapture();
            s_clockActive = false;
        }
        if (s_page != null) UnityEngine.Object.DestroyImmediate(s_page.gameObject);
        s_page = null;
        s_battle = null;
        s_queue = null;
        s_spectate = null;
        s_result = null;
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Advance;
        AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
        EditorApplication.playModeStateChanged -= HandlePlayModeChange;
        ClearPage();
        if (s_camera != null) s_camera.targetTexture = null;
        if (s_readback != null) UnityEngine.Object.DestroyImmediate(s_readback);
        s_readback = null;
        if (s_renderTarget != null)
        {
            s_renderTarget.Release();
            UnityEngine.Object.DestroyImmediate(s_renderTarget);
        }
        s_renderTarget = null;
        if (s_previousScene.IsValid() && s_previousScene.isLoaded)
            SceneManager.SetActiveScene(s_previousScene);
        if (s_captureScene.IsValid() && s_captureScene.isLoaded)
            EditorSceneManager.CloseScene(s_captureScene, true);
        s_root = null;
        s_camera = null;
        s_canvasRoot = null;
        IsRunning = false;
    }
}
#endif
