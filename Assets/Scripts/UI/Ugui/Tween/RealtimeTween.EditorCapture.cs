#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MmorpgClient.UI.Ugui.Tweening
{
    public static partial class RealtimeTween
    {
        private static List<RealtimeTweener> s_editorOriginalTweens;
        private static RealtimeTweenPauseSnapshot s_editorPause;

        public static void BeginEditorCapture()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Editor capture requires Edit Mode.");
            if (s_editorOriginalTweens != null) throw new InvalidOperationException("An editor capture is already active.");
            s_editorOriginalTweens = new List<RealtimeTweener>(Active);
            s_editorPause = PauseSnapshot();
        }

        public static void AdvanceEditorCapture(float deltaTime)
        {
            if (s_editorOriginalTweens == null || Application.isPlaying) return;
            UpdateAll(deltaTime, deltaTime);
        }

        public static void EndEditorCapture()
        {
            if (s_editorOriginalTweens == null) return;
            // Stop only tweens created by this disconnected preview.
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                if (s_editorOriginalTweens.Contains(Active[i])) continue;
                Active[i]?.Kill();
                Active.RemoveAt(i);
            }
            s_editorOriginalTweens = null;
            s_editorPause?.Resume();
            s_editorPause = null;
        }
    }
}
#endif
