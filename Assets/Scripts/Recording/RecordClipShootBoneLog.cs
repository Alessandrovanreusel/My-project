#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using CameraGame.Events;

namespace CameraGame.Recording
{
    /// <summary>Rig-only (Story 1.15, Phase D): the live drunk's bone positions on every frame of a clip, read AFTER the
    /// recorder sampled (order 10002), keyed by frame, so a ghost posed from a tick can be compared bone by bone.</summary>
    [DefaultExecutionOrder(ClipRecorder.ExecutionOrder + 2)]
    public class RecordClipShootBoneLog : MonoBehaviour
    {
        private readonly Dictionary<int, Vector3[]> _byFrame = new Dictionary<int, Vector3[]>();
        private EventManager _manager;
        private bool _on;
        private EventActor _actor;
        private Transform[] _bones;

        public void Begin(EventManager manager) { _manager = manager; _byFrame.Clear(); _on = true; }
        public void End() { _on = false; }
        public Vector3[] At(int frame) => _byFrame.TryGetValue(frame, out Vector3[] v) ? v : null;

        private void LateUpdate()
        {
            if (!_on || _manager == null) return;
            IReadOnlyList<EventActor> a = _manager.ActiveActors;
            EventActor actor = a.Count > 0 ? a[0] : null;
            if (actor == null || !actor.isActiveAndEnabled) return;
            if (actor != _actor)
            {
                _actor = actor;
                var smr = actor.GetComponentInChildren<SkinnedMeshRenderer>(true);
                _bones = smr != null ? smr.bones : new Transform[0];
            }
            var p = new Vector3[_bones.Length];
            for (int i = 0; i < _bones.Length; i++) p[i] = _bones[i].position;
            _byFrame[Time.frameCount] = p;
        }
    }
}
#endif
