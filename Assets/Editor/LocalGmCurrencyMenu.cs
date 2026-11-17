#if UNITY_EDITOR
using UnityEditor;

/// <summary>旧编辑器菜单仅保留入口说明；加银两统一走游戏内唯一的在途请求状态。</summary>
public static class LocalGmCurrencyMenu
{
    [MenuItem("MMORPG/GM/银两测试说明")]
    public static void ShowInstructions()
    {
        EditorUtility.DisplayDialog("本地银两测试",
            "请在 Unity Play 或 Development 测试包中登录本机测试服。\n" +
            "面板默认隐藏。进入角色后按 F10 临时打开，再按 F10 收起。\n" +
            "打开后，点击游戏画面顶部的“本地 GM：增加 10 万银两”。\n" +
            "编辑器菜单不发送加钱请求；普通发布包不包含测试按钮。", "确定");
    }
}
#endif
