using MmorpgClient.Game.Pet;
using UnityEngine;

namespace MmorpgClient.World
{
    using Vector3 = UnityEngine.Vector3;

    /// <summary>
    /// Local cosmetic companion driven by the server's active PetId. The scene protocol has no pet AOI actor;
    /// this visual never enters ActorWorld's entity map or invents remote pet ownership.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QdaoPetCompanion : MonoBehaviour
    {
        private ActorWorld _world;
        private PetClient _pets;
        private QdaoPetSpriteAnimator _visual;
        private ulong _activePetId;
        private string _facing = "E";
        public ulong ActivePetId => _activePetId;
        public string PetIdentity => _visual != null && _visual.gameObject.activeSelf ? _visual.PetIdentity : null;

        public void Bind(ActorWorld world, PetClient pets)
        {
            Hide();
            if (_world != null) _world.OnLocalPlayerChanged -= LocalPlayerChanged;
            _world = world;
            _pets = pets;
            if (_world != null) _world.OnLocalPlayerChanged += LocalPlayerChanged;
        }
        private void LocalPlayerChanged(ActorView actor) => Hide();

        internal static PetInfo ActivePet(PetListInfo list)
        {
            if (list == null || list.ActivePetId == 0) return null;
            foreach (var pet in list.Pets)
                if (pet.PetId == list.ActivePetId) return pet;
            return null;
        }

        private void LateUpdate()
        {
            var pet = ActivePet(_pets?.Pets);
            var entry = pet != null ? QdaoPetCatalog.Resolve(pet.PetTableId, pet.ModelId) : null;
            if (entry == null || _world == null || !_world.HasLocalPlayer ||
                !_world.Actors.TryGetValue(_world.LocalEntity, out var actor) || actor.Go == null)
            { Hide(); return; }
            if (_visual == null)
            {
                var go = new GameObject("LocalActivePet");
                go.transform.SetParent(_world.Root, false);
                _visual = go.AddComponent<QdaoPetSpriteAnimator>();
            }
            var camera = Camera.main;
            var right = camera != null ? camera.transform.right : Vector3.right;
            right.y = 0f;
            var target = actor.Go.transform.position - right.normalized * 3.2f + Vector3.up * .1f;
            bool changed = _activePetId != pet.PetId || _visual.PetIdentity != entry.id || !_visual.gameObject.activeSelf;
            _visual.gameObject.SetActive(true);
            _visual.SetIdentity(entry.id);
            if (changed) _visual.transform.position = target;
            var delta = target - _visual.transform.position;
            if (delta.sqrMagnitude > 900f) _visual.transform.position = target;
            else _visual.transform.position = Vector3.MoveTowards(_visual.transform.position, target, Time.deltaTime * 12f);
            if (Mathf.Abs(Vector3.Dot(delta, right)) > .03f) _facing = Vector3.Dot(delta, right) < 0f ? "W" : "E";
            _visual.Play(delta.sqrMagnitude > .02f ? "run" : "idle", _facing);
            _activePetId = pet.PetId;
        }

        private void Hide()
        {
            _activePetId = 0;
            if (_visual != null) _visual.gameObject.SetActive(false);
        }
        private void OnDisable() => Hide();
        private void OnDestroy()
        {
            if (_world != null) _world.OnLocalPlayerChanged -= LocalPlayerChanged;
            if (_visual != null)
            {
                if (Application.isPlaying) Destroy(_visual.gameObject);
                else DestroyImmediate(_visual.gameObject);
            }
            _visual = null;
            _world = null;
            _pets = null;
        }
    }
}
