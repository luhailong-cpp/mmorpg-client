#if UNITY_EDITOR
using System;
using MmorpgClient.World.CurrentMaps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MmorpgClient.Editor
{
    /// <summary>在本次独立启动的编辑器中打开正式 Bootstrap 并进入真实联机 Play。</summary>
    public static class FestivalMapLocalTestEntry
    {
        public static void OpenBootstrap()
        {
            if (!FestivalMapLocalTestPolicy.IsRequested(Environment.GetCommandLineArgs()))
                throw new InvalidOperationException("请使用本机节庆联调启动脚本及显式 -festivalMapLocalTest。");
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("编辑器存在未保存场景，未覆盖；请保存后重新启动本机联调。");
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity", OpenSceneMode.Single);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.delayCall += () =>
            {
                Debug.Log("[FestivalMapLocalTest] 进入正式 Bootstrap Play；左上面板可切换9图及已交付外观。");
                EditorApplication.EnterPlaymode();
            };
        }
    }
}
#endif
