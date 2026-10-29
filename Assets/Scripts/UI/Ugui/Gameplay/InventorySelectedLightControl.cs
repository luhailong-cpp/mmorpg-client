using System;
using UnityEngine;
using UnityEngine.UI;

namespace MmorpgClient.UI.Ugui.Gameplay
{
    /// <summary>One shared material for the existing, unpacked 192 x 147 selected-slot sprite.</summary>
    internal static class InventorySelectedLightControl
    {
        private const string ShaderResource = "UI/Ugui/GameplayFidelity20261016/InventorySelectedLightControl";
        private static Material _material;
        private static bool _attempted, _packingWarningLogged;

        internal static void Apply(Image image)
        {
            if (image == null) return;
            // The control uses the whole standalone texture. Atlas UVs need a separate mapping.
            if (image.sprite != null && image.sprite.packed)
            {
                if (!_packingWarningLogged)
                {
                    _packingWarningLogged = true;
                    Debug.LogWarning("[InventorySelectedLightControl] Packed sprite: retaining its original material.");
                }
                return;
            }
            var material = SharedMaterial();
            if (material != null) image.material = material;
        }

        private static Material SharedMaterial()
        {
            if (_material != null || _attempted) return _material;
            _attempted = true;
            try
            {
                var shader = Resources.Load<Shader>(ShaderResource);
                if (shader != null && shader.isSupported)
                {
                    _material = new Material(shader)
                    {
                        name = "InventorySelectedLightControl (shared runtime)",
                        hideFlags = HideFlags.DontSave
                    };
                    _material.SetVector("_SourceSize", new Vector4(192f, 147f, 0f, 0f));
                    return _material;
                }
                Debug.LogWarning("[InventorySelectedLightControl] Shader unavailable; retaining the original material.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[InventorySelectedLightControl] Retaining the original material: " + exception.Message);
            }
            return null;
        }
    }
}
