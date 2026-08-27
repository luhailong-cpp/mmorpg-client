using UnityEngine;
using UnityEngine.Rendering;

namespace MmorpgClient.World.Tianyong
{
    public static class TianyongLighting
    {
        public static void Apply(TianyongTheme theme, Light directionalLight)
        {
            var night = theme == TianyongTheme.Lantern;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = night
                ? new Color(0.16f, 0.22f, 0.36f)
                : theme == TianyongTheme.Snow
                    ? new Color(0.78f, 0.84f, 0.88f)
                    : new Color(0.60f, 0.68f, 0.62f);

            if (directionalLight == null) return;
            directionalLight.type = LightType.Directional;
            directionalLight.color = night
                ? new Color(0.42f, 0.55f, 0.86f)
                : new Color(1f, 0.91f, 0.72f);
            directionalLight.intensity = night ? 0.55f : 1.15f;
            directionalLight.transform.rotation = Quaternion.Euler(48f, -34f, 0f);
        }
    }
}
