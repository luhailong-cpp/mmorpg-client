using UnityEngine;

namespace MmorpgClient.World.Tianyong
{
    public enum TianyongMaterialSlot
    {
        Grass,
        Stone,
        Water,
        Timber,
        Roof,
        Neutral,
    }

    /// <summary>
    /// Serialized runtime settings and shader/material references for the
    /// Tianyong map. Geometry coordinates remain in TianyongMapDefinition so
    /// navigation and rendering consume the same immutable layout.
    /// </summary>
    [CreateAssetMenu(fileName = "TianyongMapConfig", menuName = "MMORPG/World/Tianyong Map Config")]
    public sealed class TianyongMapConfig : ScriptableObject
    {
        public const string ResourcesPath = "World/Tianyong/TianyongMapConfig";

        [Header("Scene")]
        [SerializeField, Min(1)] private uint sceneConfigId = TianyongMapDefinition.DefaultSceneConfigId;
        [SerializeField] private TianyongTheme initialTheme = TianyongTheme.City;
        [SerializeField, Range(1, 8)] private int visibleChunkRadius = 3;

        [Header("Painted main city")]
        [Tooltip("City theme renders the painted city artwork as the ground instead of the generated 3D town (colliders and navigation stay active).")]
        [SerializeField] private bool paintedCityGround = true;
        [SerializeField, Min(1f)] private float cameraZoomMin = 5f;
        [SerializeField, Min(1f)] private float cameraZoomMax = 30f;
        [Tooltip("27 keeps the 6144 px painted city at roughly one source texel per screen pixel at 1080p. Closer defaults magnify and blur the artwork.")]
        [SerializeField, Min(1f)] private float cameraZoomDefault = 27f;

        [Header("Player")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 9f;
        [SerializeField, Min(0.5f)] private float playerHeight = 1.8f;
        [SerializeField, Min(0.1f)] private float playerRadius = 0.38f;
        [SerializeField, Min(0f)] private float playerStepOffset = 0.35f;
        [SerializeField] private GameObject debugPlayerPrefab;

        [Header("Base materials")]
        [SerializeField] private Material grassMaterial;
        [SerializeField] private Material stoneMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private Material timberMaterial;
        [SerializeField] private Material roofMaterial;
        [SerializeField] private Material neutralMaterial;

        public uint SceneConfigId => sceneConfigId;
        public TianyongTheme InitialTheme => initialTheme;
        public int VisibleChunkRadius => visibleChunkRadius;
        public bool PaintedCityGround => paintedCityGround;

        // Zoom getters normalize because assets serialized before these fields
        // existed deserialize them as 0; OnValidate only runs in the editor.
        public float CameraZoomMin => cameraZoomMin >= 1f ? cameraZoomMin : 5f;
        public float CameraZoomMax => Mathf.Max(CameraZoomMin, cameraZoomMax >= 1f ? cameraZoomMax : 30f);
        public float CameraZoomDefault => Mathf.Clamp(
            cameraZoomDefault >= 1f ? cameraZoomDefault : 27f, CameraZoomMin, CameraZoomMax);

        public float MoveSpeed => moveSpeed;
        public float PlayerHeight => playerHeight;
        public float PlayerRadius => playerRadius;
        public float PlayerStepOffset => playerStepOffset;
        public GameObject DebugPlayerPrefab => debugPlayerPrefab;

        public static TianyongMapConfig LoadDefault()
            => Resources.Load<TianyongMapConfig>(ResourcesPath);

        public Material GetBaseMaterial(TianyongMaterialSlot slot)
            => slot switch
            {
                TianyongMaterialSlot.Grass => grassMaterial,
                TianyongMaterialSlot.Stone => stoneMaterial,
                TianyongMaterialSlot.Water => waterMaterial,
                TianyongMaterialSlot.Timber => timberMaterial,
                TianyongMaterialSlot.Roof => roofMaterial,
                _ => neutralMaterial,
            };

        private void OnValidate()
        {
            if (sceneConfigId == 0) sceneConfigId = TianyongMapDefinition.DefaultSceneConfigId;
            visibleChunkRadius = Mathf.Clamp(visibleChunkRadius, 1, 8);
            cameraZoomMin = Mathf.Max(1f, cameraZoomMin);
            cameraZoomMax = Mathf.Max(cameraZoomMin, cameraZoomMax);
            cameraZoomDefault = Mathf.Clamp(cameraZoomDefault, cameraZoomMin, cameraZoomMax);
            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            playerHeight = Mathf.Max(0.5f, playerHeight);
            playerRadius = Mathf.Clamp(playerRadius, 0.1f, playerHeight * 0.49f);
            playerStepOffset = Mathf.Clamp(playerStepOffset, 0f, playerHeight * 0.49f);
        }
    }
}
