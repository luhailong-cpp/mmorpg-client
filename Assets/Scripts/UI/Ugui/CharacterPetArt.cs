using UnityEngine;

/// <summary>人物和宝宝窗口共用交接包中的控件和技能画芯。</summary>
public static class CharacterPetArt
{
    private const string Folder = "UI/Ugui/CharacterPet/";
    private static Font _font;
    public static Font Font => _font != null ? _font : (_font = Resources.Load<Font>("Fonts/SimKai"));

    public static Sprite Get(string key) =>
        string.IsNullOrEmpty(key) ? null : Resources.Load<Sprite>(Folder + "Controls/" + key);

    public static Sprite Skill(uint tableId, int size = 128)
    {
        var key = SkillPath(tableId, size);
        return key == null ? null : Resources.Load<Sprite>(key);
    }

    public static string SkillPath(uint tableId, int size = 128)
    {
        string id = CharacterPetIconIds.Find(tableId);
        return id == null ? null : Folder + "Icons/" + (size <= 64 ? 64 : size <= 128 ? 128 : 256) + "/" + id;
    }
}
