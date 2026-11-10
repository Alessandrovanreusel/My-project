// THROWAWAY SPIKE CODE (Story 1.14). Never referenced by game code. See README.md in this folder.
using System;
using System.Collections.Generic;
using CameraGame.Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CameraGame.Spikes.RecordingSpike
{
    /// <summary>
    /// Candidate (B), replay recording. Records at 30 Hz what the camera did and what the event actor
    /// did (transform, liveness, per-layer Animator state), then re-renders it from a playback camera with
    /// a GHOST puppet - a stripped copy of the drunk's visual that is not pooled, has no EventActor, no
    /// NavMeshAgent and no AudioSource - so the live game keeps running untouched (Story 1.18's AC).
    /// Audio is PCM from the listener tap (re-spatialising from a recorded viewpoint would need a second
    /// AudioListener, which Unity does not allow).
    /// </summary>
    public sealed class ReplayTrack
    {
        public struct Layer { public int hash; public float normT; public bool inTrans; public int nextHash; public float nextNormT; public float transNormT; public float transDur; }
        public struct Tick
        {
            public Vector3 camPos; public Quaternion camRot; public float fov;
            public bool active; public Vector3 pos; public Quaternion rot; public Layer[] layers;
            public int repeats;
        }

        public readonly Tick[] Ticks;
        public int Count;
        public int Layers;
        public Tick this[int i] => Ticks[i];

        public ReplayTrack(int capacity, int layers)
        {
            Layers = layers;
            Ticks = new Tick[capacity];
            for (int i = 0; i < capacity; i++) Ticks[i].layers = new Layer[layers];
        }

        /// <summary>Bytes one tick would take in a packed binary format (what a memory card would store).</summary>
        public int BytesPerTick => 12 + 16 + 4 + 1 + 12 + 16 + Layers * (4 + 4 + 1 + 4 + 4 + 4 + 4) + 1;

        public void Reset() => Count = 0;

        public bool Record(Camera cam, EventActor actor, Animator anim, int repeats)
        {
            if (Count >= Ticks.Length) return false;
            ref Tick t = ref Ticks[Count++];
            t.camPos = cam.transform.position; t.camRot = cam.transform.rotation; t.fov = cam.fieldOfView;
            t.repeats = repeats;
            t.active = actor != null && actor.isActiveAndEnabled;
            if (t.active)
            {
                t.pos = actor.transform.position; t.rot = actor.transform.rotation;
                for (int l = 0; l < Layers && anim != null; l++)
                {
                    var s = anim.GetCurrentAnimatorStateInfo(l);
                    ref Layer L = ref t.layers[l];
                    L.hash = s.fullPathHash; L.normT = s.normalizedTime;
                    L.inTrans = anim.IsInTransition(l);
                    if (L.inTrans)
                    {
                        var n = anim.GetNextAnimatorStateInfo(l);
                        var tr = anim.GetAnimatorTransitionInfo(l);
                        L.nextHash = n.fullPathHash; L.nextNormT = n.normalizedTime;
                        L.transNormT = tr.normalizedTime; L.transDur = tr.duration;
                    }
                }
            }
            return true;
        }
    }

    public sealed class ReplayPlayer : IDisposable
    {
        public readonly Camera PlayCam;
        public readonly RenderTexture Target;
        public GameObject Ghost { get; private set; }
        public Animator GhostAnim { get; private set; }
        public SkinnedMeshRenderer[] GhostSkins { get; private set; } = new SkinnedMeshRenderer[0];
        public string BuildNote = "";

        private Renderer[] _ghostRenderers = new Renderer[0];
        private EventManager _manager;
        private readonly List<Renderer> _liveScratch = new List<Renderer>();

        public ReplayPlayer(Camera main, EventManager manager, int w, int h)
        {
            _manager = manager;
            var go = new GameObject("RecordingSpike_PlaybackCamera");
            PlayCam = go.AddComponent<Camera>();
            PlayCam.CopyFrom(main);
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            var mainData = main.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = mainData.renderPostProcessing;
            data.antialiasing = mainData.antialiasing;
            Target = new RenderTexture(w, h, 24, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB) { name = "RecordingSpike_Replay" };
            Target.Create();
            PlayCam.targetTexture = Target;
            PlayCam.enabled = false;
            RenderPipelineManager.beginCameraRendering += OnBegin;
        }

        /// <summary>Copy the live actor's visual into an inactive parent (so no Awake runs), strip every
        /// behaviour that would make it part of the live game, then activate it.</summary>
        public void BuildGhost(EventActor live)
        {
            if (Ghost != null) return;
            var holder = new GameObject("RecordingSpike_GhostHolder");
            holder.SetActive(false);
            var copy = UnityEngine.Object.Instantiate(live.gameObject, holder.transform);
            copy.name = "RecordingSpike_Ghost";
            int stripped = 0;
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) { UnityEngine.Object.DestroyImmediate(mb); stripped++; }
            foreach (var ag in copy.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) { UnityEngine.Object.DestroyImmediate(ag); stripped++; }
            foreach (var au in copy.GetComponentsInChildren<AudioSource>(true)) { UnityEngine.Object.DestroyImmediate(au); stripped++; }
            foreach (var co in copy.GetComponentsInChildren<Collider>(true)) { UnityEngine.Object.DestroyImmediate(co); stripped++; }
            copy.transform.SetParent(null, true);
            UnityEngine.Object.Destroy(holder);
            Ghost = copy;
            GhostAnim = copy.GetComponentInChildren<Animator>(true);
            if (GhostAnim != null) { GhostAnim.applyRootMotion = false; GhostAnim.speed = 0f; GhostAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            _ghostRenderers = copy.GetComponentsInChildren<Renderer>(true);
            GhostSkins = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            BuildNote = $"ghost built from live actor: {stripped} live components stripped, {_ghostRenderers.Length} renderers, animator={(GhostAnim != null)}";
            Ghost.SetActive(true);
            SetGhostVisible(false);
        }

        public void Apply(ReplayTrack.Tick t)
        {
            PlayCam.transform.SetPositionAndRotation(t.camPos, t.camRot);
            PlayCam.fieldOfView = t.fov;
            if (Ghost == null) return;
            if (Ghost.activeSelf != t.active) Ghost.SetActive(t.active);
            if (!t.active) return;
            Ghost.transform.SetPositionAndRotation(t.pos, t.rot);
            if (GhostAnim == null) return;
            for (int l = 0; l < t.layers.Length; l++)
            {
                var L = t.layers[l];
                GhostAnim.Play(L.hash, l, L.normT);
                if (L.inTrans)
                {
                    GhostAnim.Update(0f);
                    GhostAnim.CrossFade(L.nextHash, L.transDur, l, L.nextNormT, L.transNormT);
                }
            }
            GhostAnim.Update(0f);
        }

        private void SetGhostVisible(bool v) { foreach (var r in _ghostRenderers) if (r != null) r.forceRenderingOff = !v; }

        private void SetLiveVisible(bool v)
        {
            if (_manager == null) return;
            foreach (var a in _manager.ActiveActors)
            {
                if (a == null) continue;
                _liveScratch.Clear();
                a.GetComponentsInChildren(true, _liveScratch);
                foreach (var r in _liveScratch) r.forceRenderingOff = !v;
            }
        }

        // The ghost exists only for the playback camera; the live drunk exists only for everyone else.
        // No layers are added (they are project settings): renderers are toggled per camera instead.
        private void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            bool play = cam == PlayCam;
            SetGhostVisible(play);
            SetLiveVisible(!play);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            SetLiveVisible(true);
            if (Ghost != null) UnityEngine.Object.Destroy(Ghost);
            if (PlayCam != null) UnityEngine.Object.Destroy(PlayCam.gameObject);
            if (Target != null) { Target.Release(); UnityEngine.Object.Destroy(Target); }
        }
    }
}
