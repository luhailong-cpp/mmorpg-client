using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MmorpgClient.App;
using MmorpgClient.Game;
using MmorpgClient.UI.Ugui.Role;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MmorpgClient.Tests.PlayMode
{
    /// <summary>Real role buttons with disconnected account fixtures. Never network acceptance.</summary>
    public sealed partial class QdaoRoleIdentityPlayModeTests
    {
        [UnityTest]
        public IEnumerator AppearanceUi_UsesOriginalChooserForCreationAndSavedCardSelection()
        {
            var host = new GameObject("DisconnectedRoleUiDriverFixture");
            var ui = host.AddComponent<RoleFlowUi>();
            var logs = new List<string>();
            var errors = new List<string>();
            string directory = CaptureDirectory();
            try
            {
                typeof(RoleFlowUi).GetMethod("BuildCanvas", Hidden).Invoke(ui, null);
                var original = Original(ui);
                var options = new DevAutoPilot.Options { AppearanceId = First, RoleClass = 1, RoleGender = 1, ShotDir = directory };
                var driver = new DevRoleUiDriver(ui, ui, original, options, logs.Add, errors.Add,
                    scope: "disconnected_ui_fixture", disconnectedCapture: () => CaptureDriverFixture(ui.GetComponentInChildren<Canvas>()));
                var creation = new GameClient.PlayerChoice();
                yield return driver.Choose(1, Array.Empty<AccountSimplePlayer>(), creation);
                Assert.That(errors, Is.Empty, string.Join("; ", errors));
                Assert.That(creation.CreateNew, Is.True);
                Assert.That(creation.AppearanceId, Is.EqualTo(First));
                Assert.That(creation.ClassId, Is.EqualTo(1u));
                Assert.That(creation.Gender, Is.EqualTo(1u));
                Assert.That(logs.Any(line => line.StartsWith("appearance_ui action=create ")), Is.True);

                var roles = new[]
                {
                    new AccountSimplePlayer { PlayerId = 101, ClassId = 1, Gender = 1, AppearanceId = First },
                    new AccountSimplePlayer { PlayerId = 102, ClassId = 1, Gender = 1, AppearanceId = Second }
                };
                options.AppearanceId = Second;
                options.RequireAppearanceRole = true;
                var selection = new GameClient.PlayerChoice();
                yield return driver.Choose(1, roles, selection);
                Assert.That(errors, Is.Empty, string.Join("; ", errors));
                Assert.That(selection.CreateNew, Is.False);
                Assert.That(selection.SelectedPlayerId, Is.EqualTo(102UL));
                Assert.That(logs.Any(line => line.StartsWith("appearance_ui action=select ") && line.Contains("player_id=102")), Is.True);
                var pngs = Directory.GetFiles(directory, "*.png");
                Assert.That(pngs.Length, Is.EqualTo(2));
                foreach (string png in pngs)
                {
                    Assert.That(new FileInfo(png).Length, Is.GreaterThan(100));
                    string record = File.ReadAllText(png + ".json");
                    Assert.That(record, Does.Contain("disconnected_ui_fixture"));
                    Assert.That(record, Does.Contain("\"uiChoiceVerified\": true"));
                    Assert.That(record, Does.Contain("programmatic_unity_pointer_events"));
                }
            }
            finally { UnityEngine.Object.Destroy(host); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator AppearanceUi_StrictRelogMissingRoleCannotCreateOrReportSuccess()
        {
            var host = new GameObject("DisconnectedMissingRoleUiDriverFixture");
            var ui = host.AddComponent<RoleFlowUi>();
            var logs = new List<string>();
            var errors = new List<string>();
            string directory = CaptureDirectory();
            try
            {
                typeof(RoleFlowUi).GetMethod("BuildCanvas", Hidden).Invoke(ui, null);
                var options = new DevAutoPilot.Options
                    { AppearanceId = First, RoleClass = 1, RoleGender = 1, ShotDir = directory, RequireAppearanceRole = true };
                var driver = new DevRoleUiDriver(ui, ui, Original(ui), options, logs.Add, errors.Add,
                    scope: "disconnected_ui_fixture");
                var choice = new GameClient.PlayerChoice();
                yield return driver.Choose(1, Array.Empty<AccountSimplePlayer>(), choice);
                Assert.That(errors.Count, Is.EqualTo(1));
                Assert.That(logs.Any(line => line.StartsWith("appearance_ui action=")), Is.False);
                Assert.That(choice.CreateNew, Is.False);
                Assert.That(choice.SelectedPlayerId, Is.Zero);
                Assert.That(choice.AppearanceId, Is.Null);
                Assert.That(Directory.Exists(directory), Is.False);
            }
            finally { UnityEngine.Object.Destroy(host); }
            yield return null;
        }

        private static Func<uint, IReadOnlyList<AccountSimplePlayer>, GameClient.PlayerChoice, IEnumerator> Original(RoleFlowUi ui)
            => (Func<uint, IReadOnlyList<AccountSimplePlayer>, GameClient.PlayerChoice, IEnumerator>)Delegate.CreateDelegate(
                typeof(Func<uint, IReadOnlyList<AccountSimplePlayer>, GameClient.PlayerChoice, IEnumerator>),
                ui, typeof(RoleFlowUi).GetMethod("Choose", Hidden));

        private static string CaptureDirectory()
        {
            string root = Environment.GetEnvironmentVariable("QDAO_IDENTITY_CAPTURE_DIR");
            if (string.IsNullOrEmpty(root)) root = Path.Combine(Application.temporaryCachePath, "qdao-identity-ui");
            return Path.Combine(root, "disconnected-driver-" + Guid.NewGuid().ToString("N"));
        }

        private static Texture2D CaptureDriverFixture(Canvas canvas)
        {
            var mode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            float distance = canvas.planeDistance;
            var active = RenderTexture.active;
            var host = new GameObject("DisconnectedDriverCaptureCamera");
            var target = new RenderTexture(1920, 810, 24);
            var image = new Texture2D(1920, 810, TextureFormat.RGB24, false);
            try
            {
                var camera = host.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                return image;
            }
            catch { UnityEngine.Object.Destroy(image); throw; }
            finally
            {
                canvas.renderMode = mode;
                canvas.worldCamera = previousCamera;
                canvas.planeDistance = distance;
                RenderTexture.active = active;
                target.Release();
                UnityEngine.Object.Destroy(target);
                UnityEngine.Object.Destroy(host);
                Canvas.ForceUpdateCanvases();
            }
        }
    }
}
