#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Imports the production skill icons and captures their existing skill book and battle views.</summary>
public static class SkillIconStyleVerification
{
    public static void Capture()
    {
        SkillUiVerification.RequireEditMode();
        string output = Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Docs/VerificationEvidence/skill-icons-style-20261021"));
        Directory.CreateDirectory(output);
        var report = new Report { unityVersion = Application.unityVersion };
        var icons = new List<Icon>();
        for (int classId = 1; classId <= 4; classId++)
        for (int index = 1; index <= 8; index++)
        {
            int id = 1000 + classId * 100 + index;
            string path = "Assets/Resources/UI/Skills/" + id + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var sprite = Resources.Load<Sprite>("UI/Skills/" + id);
            if (sprite == null || sprite.rect.width <= 0 || sprite.rect.width != sprite.rect.height)
                throw new InvalidOperationException("Skill icon did not import as a square Sprite: " + path);
            icons.Add(new Icon { id = id, guid = AssetDatabase.AssetPathToGUID(path),
                width = sprite.rect.width, height = sprite.rect.height });
        }
        report.icons = icons.ToArray();
        var screenshots = new List<string>();
        foreach (int width in new[] { 1920, 2560 })
        using (var preview = new SkillUiPreviewSession(width, 1080))
        {
            for (int classIndex = 0; classIndex < 4; classIndex++)
            {
                preview.Load((SkillUiFixture)classIndex);
                preview.Render();
                string name = "class" + (classIndex + 1) + "-" + width + "x1080.png";
                preview.Save(Path.Combine(output, name));
                screenshots.Add(name);
            }
            preview.LoadBattle();
            preview.Render();
            string battle = "battle-" + width + "x1080.png";
            preview.Save(Path.Combine(output, battle));
            screenshots.Add(battle);
        }
        report.screenshots = screenshots.ToArray();
        report.completedAtUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(Path.Combine(output, "native-import-capture.json"), JsonUtility.ToJson(report, true));
        Debug.Log("SKILL_ICON_STYLE_OK|icons=" + icons.Count + "|screenshots=" + screenshots.Count + "|" + output);
    }

    [Serializable] private sealed class Icon { public int id; public string guid; public float width, height; }
    [Serializable] private sealed class Report
    {
        public string status = "passed", completedAtUtc, unityVersion;
        public bool layoutPreviewOnly = true, liveServerVerified = false;
        public Icon[] icons;
        public string[] screenshots;
    }
}
#endif
