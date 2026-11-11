#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CameraGame.Events;
using CameraGame.PhotoMode;

namespace CameraGame.Player
{
    /// <summary>One spot of the Story 2.1 scale study, in TODAY's world coordinates (option C).</summary>
    [System.Serializable]
    public class ScaleSpot
    {
        public string name, kind, objectName;
        public Vector3 anchor, stand, look, drunk;
        public float witnessYaw, witnessNear;
    }

    [System.Serializable]
    public class ScaleSpotList { public ScaleSpot[] spots; }

    /// <summary>
    /// The Story 2.1 scale-study runner. The editor half (<c>ScaleStudyRig</c>) opens the real town, STAGES an
    /// option in the unsaved scene (characters scaled, or the town scaled, or nothing) and enters play mode;
    /// this half then drives the real game and writes what it produced.
    ///
    /// Modes:
    ///   measure  — the starting point, read back in play mode with the Animator running (Task 1a).
    ///   photos   — from the player's eye at each spot (the REAL ThirdPersonCamera places the camera), plus an
    ///              orthographic witness frame whose labelled heights are measured, never assumed (AC1/AC4).
    ///   crossing — walks/sprints fixed routes with the real controller through the real input path (AC1).
    ///   movement — no-input control, walk, sprint, jump, kerb, slab edge, and 10 raises/10 lowers (AC3).
    ///   cue      — listens to the drunk from the game's OWN audio output at a sweep of distances (AC3).
    ///
    /// Input is queued as Keyboard state events, which travel PlayerInput (Send Messages) → OnMove/OnSprint/
    /// OnJump exactly as a key press does. Yaw is the one thing the rig sets directly (it would come from the
    /// mouse); the private input fields are never written.
    ///
    /// Runtime assembly behind #if UNITY_EDITOR — an editor-assembly MonoBehaviour is "missing" on a scene
    /// object in play mode (CLAUDE.md trap).
    /// </summary>
    public class ScaleStudyRunner : MonoBehaviour
    {
        [HideInInspector] public string mode = "photos";
        [HideInInspector] public string option = "C";
        [HideInInspector] public string speedLabel = "today";
        [HideInInspector] public string outputDir;
        [HideInInspector] public string spotsJson;
        [HideInInspector] public float worldScale = 1f;         // (B): the town's scale; spot positions × this
        [HideInInspector] public float drunkArmatureScale = 0.01f;
        [HideInInspector] public GameObject drunkPrefab;
        [HideInInspector] public float[] cueDistances;
        [HideInInspector] public float cueHoldSeconds = 1.5f;
        [HideInInspector] public int cueSweeps = 4;
        [HideInInspector] public float nearClipOverride = -1f;   // staging "Old": ThirdPersonCamera.Start sets the config's near clip

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const int W = 960, H = 540;

        private readonly StringBuilder _log = new StringBuilder();
        private GameObject _player;
        private CharacterController _cc;
        private ThirdPersonController _tpc;
        private ThirdPersonCamera _chase;
        private Camera _cam;
        private EventManager _manager;
        private PhotoModeController _photo;
        private ScaleSpot[] _spots;

        // put back on the way out
        private bool _prevRunInBackground;
        private InputSettings.EditorInputBehaviorInPlayMode _prevEditorInput;
        private InputSettings.BackgroundBehavior _prevBackground;
        private float _prevTimeScale;

        private static string F(float v, string fmt = "0.###") => v.ToString(fmt, Inv);
        private static string V(Vector3 v) => $"[{F(v.x)}, {F(v.y)}, {F(v.z)}]";

        private IEnumerator Start()
        {
            _prevRunInBackground = Application.runInBackground;
            _prevTimeScale = Time.timeScale;
            _prevEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            _prevBackground = InputSystem.settings.backgroundBehavior;
            Application.runInBackground = true;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            try
            {
                Directory.CreateDirectory(outputDir);
                _player = GameObject.Find("main characters");
                _cc = _player != null ? _player.GetComponent<CharacterController>() : null;
                _tpc = _player != null ? _player.GetComponent<ThirdPersonController>() : null;
                _cam = Camera.main;
                _chase = _cam != null ? _cam.GetComponent<ThirdPersonCamera>() : null;
                _manager = FindFirstObjectByType<EventManager>(FindObjectsInactive.Include);
                _photo = FindFirstObjectByType<PhotoModeController>(FindObjectsInactive.Include);
                _spots = string.IsNullOrEmpty(spotsJson) ? new ScaleSpot[0]
                       : JsonUtility.FromJson<ScaleSpotList>(spotsJson).spots;

                _log.AppendLine($"SCALE STUDY — mode {mode}, option {option}, speeds {speedLabel}, town scale {F(worldScale)}");
                _log.AppendLine($"started {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}; unity {Application.unityVersion}");
                if (_player == null || _cc == null || _cam == null)
                {
                    _log.AppendLine("RIG BROKEN: no player / controller / main camera in this scene.");
                    yield break;
                }
                if (nearClipOverride > 0f) { yield return null; _cam.nearClipPlane = nearClipOverride; }
                LogPlayer("at start");

                switch (mode)
                {
                    case "measure": yield return Measure(); break;
                    case "photos": yield return Photos(); break;
                    case "crossing": yield return Crossing(); break;
                    case "movement": yield return Movement(); break;
                    case "cue": yield return Cue(); break;
                    case "film": yield return Film(); break;
                    default: _log.AppendLine("unknown mode " + mode); break;
                }
            }
            finally
            {
                Keys();
                InputSystem.settings.editorInputBehaviorInPlayMode = _prevEditorInput;
                InputSystem.settings.backgroundBehavior = _prevBackground;
                Application.runInBackground = _prevRunInBackground;
                Time.timeScale = _prevTimeScale;
                _log.AppendLine("restored: runInBackground, input settings, timeScale");
                try { File.WriteAllText(Path.Combine(outputDir, "log.txt"), _log.ToString()); }
                catch (IOException e) { Debug.LogError("[ScaleStudy] could not write log: " + e.Message); }
                Debug.Log("[ScaleStudy] DONE " + mode + " " + option + " → " + outputDir);
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            }
        }

        // =============================================================================================
        // helpers
        // =============================================================================================

        private void LogPlayer(string when)
        {
            _log.AppendLine($"player {when}: pos {V(_player.transform.position)} CC h {F(_cc.height)} r {F(_cc.radius)} " +
                            $"c {V(_cc.center)} skin {F(_cc.skinWidth)} step {F(_cc.stepOffset)} | walk {F(Field("walkSpeed"))} " +
                            $"sprint {F(Field("sprintSpeed"))} jump {F(Field("jumpHeight"))} g {F(Field("gravity"))} | " +
                            $"cam near {F(_cam.nearClipPlane)} far {F(_cam.farClipPlane)} fov {F(_cam.fieldOfView)}");
        }

        /// <summary>Reads a serialized tuning field of the controller (whatever the code calls it), so every
        /// number the rig compares against is the one the game is actually running on.</summary>
        private float Field(string name)
        {
            if (_tpc == null) return float.NaN;
            FieldInfo f = typeof(ThirdPersonController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null && f.FieldType == typeof(float)) return (float)f.GetValue(_tpc);
            // Since ADR-5 the tuning lives in PlayerConfig (same field names), read through the controller.
            PlayerConfig cfg = _tpc.Config;
            FieldInfo cf = typeof(PlayerConfig).GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (cf != null) return cfg != null ? (float)cf.GetValue(cfg) : float.NaN;
            PropertyInfo p = typeof(ThirdPersonController).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return p != null ? System.Convert.ToSingle(p.GetValue(_tpc), Inv) : float.NaN;
        }

        private static void Keys(params Key[] keys)
        {
            if (Keyboard.current == null) return;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        }

        private Vector3 S(Vector3 p) => p * worldScale;

        private bool IsMine(Collider c) =>
            c != null && ((_player != null && c.transform.IsChildOf(_player.transform)) || c.GetComponentInParent<EventActor>() != null);

        /// <summary>Highest surface under a point, ignoring the player and actors.</summary>
        private float GroundY(Vector3 p, float from = float.NaN)
        {
            float top = float.IsNaN(from) ? 400f * worldScale : from;
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, top, p.z), Vector3.down, top + 300f * worldScale,
                                                   ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
                if (!IsMine(h.collider) && h.point.y > best) best = h.point.y;
            return float.IsNegativeInfinity(best) ? p.y : best;
        }

        private static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>Moves the player through the CharacterController's own rules: disabled for the jump, so
        /// collision does not drag it back, and re-enabled so the next Move() starts clean.</summary>
        private void Teleport(Vector3 at, float yaw, float groundFrom = float.NaN)
        {
            float y = GroundY(at, groundFrom) + 0.02f;
            _cc.enabled = false;
            _player.transform.SetPositionAndRotation(new Vector3(at.x, y, at.z), Quaternion.Euler(0f, yaw, 0f));
            _cc.enabled = true;
            Physics.SyncTransforms();
        }

        private IEnumerator Settle(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        private static Texture2D Read(RenderTexture rt)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        /// <summary>Renders a camera into an offscreen target WITHOUT moving it — call after the frame's
        /// LateUpdate, so the pose is exactly the one the game set.</summary>
        private static void RenderTo(Camera c, string path, out float lumaStd)
        {
            RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            RenderTexture prev = c.targetTexture;
            c.targetTexture = rt;
            c.ResetAspect();
            c.Render();
            c.targetTexture = prev;
            c.ResetAspect();
            Texture2D tex = Read(rt);
            RenderTexture.ReleaseTemporary(rt);

            // Uniform or white = the camera did not render (lessons). Measured, not eyeballed.
            Color32[] px = tex.GetPixels32();
            double s = 0, s2 = 0;
            for (int i = 0; i < px.Length; i += 7)
            {
                double l = 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b;
                s += l; s2 += l * l;
            }
            double n = px.Length / 7.0, mean = s / n;
            lumaStd = (float)System.Math.Sqrt(System.Math.Max(0, s2 / n - mean * mean));

            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        // =============================================================================================
        // reference-object metrics (world-space, measured on the spot's own instance)
        // =============================================================================================

        private Renderer FindRenderer(string name)
        {
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.name == name) return r;
            return null;
        }

        /// <summary>Casts horizontally from <paramref name="origin"/> along <paramref name="dir"/>; NaN on a miss.</summary>
        private float HitDistance(Vector3 origin, Vector3 dir, float max, Renderer only = null)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, max, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            foreach (RaycastHit h in hits)
                if (!IsMine(h.collider) && (only == null || h.collider.GetComponent<Renderer>() == only) && h.distance < best)
                    best = h.distance;
            return float.IsPositiveInfinity(best) ? float.NaN : best;
        }

        /// <summary>Every renderer sharing the object's name prefix (up to its first '_') near the anchor.</summary>
        private Bounds UnionBounds(ScaleSpot sp, out List<Renderer> parts)
        {
            parts = new List<Renderer>();
            string prefix = sp.objectName.Contains("_") ? sp.objectName.Substring(0, sp.objectName.IndexOf('_') + 1) : sp.objectName;
            Vector3 a = S(sp.anchor);
            Bounds u = default;
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith(prefix)) continue;
                Vector3 c = r.bounds.center; c.y = a.y;
                if (Vector3.Distance(c, a) > 8f * worldScale) continue;
                if (parts.Count == 0) u = r.bounds; else u.Encapsulate(r.bounds);
                parts.Add(r);
            }
            return u;
        }

        /// <summary>All numbers in metres in THIS world (so option B reports the scaled town).</summary>
        private Dictionary<string, float> MeasureSpot(ScaleSpot sp)
        {
            var m = new Dictionary<string, float>();
            float s = worldScale;
            Renderer r = FindRenderer(sp.objectName);
            if (r == null) { m["missing"] = 1; return m; }
            Bounds b = r.bounds;
            m["bounds_min_y"] = b.min.y; m["bounds_max_y"] = b.max.y;
            m["bounds_size_x"] = b.size.x; m["bounds_size_y"] = b.size.y; m["bounds_size_z"] = b.size.z;
            Vector3 a = S(sp.anchor);

            switch (sp.kind)
            {
                case "car":
                {
                    // The car is several renderers (one per material, "Cube.006_M5_0", "Cube.006_M8_0", …): take
                    // their union, then the roof as the 90th percentile of a downward ray grid over it — the
                    // light bar is a few % of the footprint and must not be read as the roof.
                    Bounds u = UnionBounds(sp, out List<Renderer> parts);
                    float g = GroundY(S(sp.stand));
                    var tops = new List<float>();
                    for (float x = u.min.x; x <= u.max.x; x += 0.1f * s)
                        for (float z = u.min.z; z <= u.max.z; z += 0.1f * s)
                            if (Physics.Raycast(new Vector3(x, u.max.y + 2f * s, z), Vector3.down, out RaycastHit h, 10f * s)
                                && parts.Contains(h.collider.GetComponent<Renderer>()))
                                tops.Add(h.point.y);
                    tops.Sort();
                    m["ground"] = g;
                    m["car_roof"] = (tops.Count > 0 ? tops[(int)(tops.Count * 0.9f)] : u.max.y) - g;
                    m["car_top_incl_lightbar"] = u.max.y - g;
                    m["car_length"] = Mathf.Max(u.size.x, u.size.z);
                    m["car_width"] = Mathf.Min(u.size.x, u.size.z);
                    m["car_parts"] = parts.Count;
                    break;
                }
                case "door":
                {
                    // deck = the surface he stands on at the threshold; head = the lowest height above it where
                    // the facade comes FORWARD again (the door panel/frame is recessed behind the wall).
                    float deck = GroundY(new Vector3(a.x, a.y + 2.5f * s, a.z - 0.6f * s), a.y + 2.5f * s);
                    float ground = GroundY(S(sp.stand));
                    Vector3 o = new Vector3(a.x, 0f, a.z - 1.1f * s);
                    float panel = HitDistance(new Vector3(o.x, deck + 1.0f * s, o.z), Vector3.forward, 4f * s, r);
                    float head = float.NaN;
                    for (float y = deck + 1.0f * s; y < deck + 6f * s; y += 0.005f * s)
                    {
                        float d = HitDistance(new Vector3(o.x, y, o.z), Vector3.forward, 4f * s, r);
                        if (!float.IsNaN(d) && d < panel - 0.05f * s) { head = y; break; }
                    }
                    // width: scan along x at mid-door height for the recess
                    float left = float.NaN, right = float.NaN;
                    for (float x = -1.5f * s; x <= 1.5f * s; x += 0.01f * s)
                    {
                        float d = HitDistance(new Vector3(o.x + x, deck + 1.0f * s, o.z), Vector3.forward, 4f * s, r);
                        bool recess = !float.IsNaN(d) && d >= panel - 0.06f * s;
                        if (recess && float.IsNaN(left)) left = x;
                        if (recess) right = x;
                    }
                    m["deck"] = deck; m["ground"] = ground;
                    m["door_head_above_deck"] = head - deck;
                    m["door_head_above_ground"] = head - ground;
                    m["door_head_y"] = head;
                    m["door_width"] = right - left;
                    break;
                }
                case "tree":
                {
                    // ground UNDER the canopy: cast from just above the trunk's foot, never from the sky
                    float g = GroundY(a + new Vector3(0.8f * s, 0f, 0f), a.y + 0.6f * s);
                    float low = float.PositiveInfinity;
                    foreach (float rad in new[] { 0.5f, 0.8f, 1.1f, 1.4f })
                        for (int i = 0; i < 16; i++)
                        {
                            Vector3 d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * (rad * s);
                            Vector3 o = new Vector3(a.x + d.x, GroundY(a + d, a.y + 0.6f * s) + 0.02f * s, a.z + d.z);
                            if (Physics.Raycast(o, Vector3.up, out RaycastHit h, 12f * s, ~0, QueryTriggerInteraction.Ignore)
                                && h.collider.GetComponent<Renderer>() == r)
                                low = Mathf.Min(low, h.point.y);
                        }
                    m["ground"] = g;
                    m["canopy_low"] = low - g;
                    m["tree_top"] = b.max.y - g;
                    break;
                }
                case "street":
                {
                    // road width: the flat top of the road slab, scanned across at the anchor's x
                    float roadY = float.NaN, zA = float.NaN, zB = float.NaN, gN = float.NaN, gS = float.NaN;
                    for (float dz = -15f * s; dz <= 12f * s; dz += 0.02f * s)
                    {
                        Vector3 p = new Vector3(a.x, 0f, a.z + dz);
                        float y = GroundY(p, 60f * s);
                        RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, 60f * s, p.z), Vector3.down, 120f * s);
                        bool onRoad = false;
                        foreach (RaycastHit h in hits)
                            if (h.collider.name.StartsWith(sp.objectName) && Mathf.Abs(h.point.y - y) < 0.01f * s) onRoad = true;
                        if (onRoad)
                        {
                            if (float.IsNaN(zA)) { zA = p.z; roadY = y; }
                            zB = p.z;
                        }
                        else if (float.IsNaN(zA)) gS = y;
                        else if (float.IsNaN(gN)) gN = y;
                    }
                    m["road_surface_y"] = roadY;
                    m["street_width"] = zB - zA;
                    m["slab_edge_south"] = roadY - gS;
                    m["slab_edge_north"] = roadY - gN;
                    m["ground"] = roadY;
                    break;
                }
                case "house":
                    break;
            }

            if (sp.kind == "door")
            {
                // the house the door belongs to: eave from a wall profile away from the porch, ridge/top from a
                // downward line scan along the house's north-south centre line (the chimney is reported separately).
                float g = m["ground"];
                Vector3 wallO = new Vector3(a.x - 6f * s, 0f, a.z - 4f * s);
                float wall = HitDistance(new Vector3(wallO.x, g + 1.5f * s, wallO.z), Vector3.forward, 12f * s, r);
                float eave = float.NaN;
                for (float y = g + 1.5f * s; y < g + 15f * s; y += 0.01f * s)
                {
                    float d = HitDistance(new Vector3(wallO.x, y, wallO.z), Vector3.forward, 12f * s, r);
                    if (float.IsNaN(d) || d > wall + 0.25f * s) { eave = y; break; }
                }
                float ridge = float.NegativeInfinity;
                for (float z = b.min.z; z <= b.max.z; z += 0.05f * s)
                    if (Physics.Raycast(new Vector3(wallO.x, b.max.y + 5f * s, z), Vector3.down, out RaycastHit h, 30f * s)
                        && h.collider.GetComponent<Renderer>() == r)
                        ridge = Mathf.Max(ridge, h.point.y);
                m["eave"] = eave - g;
                m["ridge"] = ridge - g;
                m["house_top_incl_chimney"] = b.max.y - g;
            }
            return m;
        }

        // =============================================================================================
        // measure (Task 1a)
        // =============================================================================================

        private IEnumerator Measure()
        {
            // let the player land
            float t = 0f;
            while (!_cc.isGrounded && t < 8f) { t += Time.deltaTime; yield return null; }
            yield return Settle(0.5f);
            yield return new WaitForEndOfFrame();

            var j = new StringBuilder();
            j.AppendLine("{");
            Vector3 feet = _player.transform.position;
            Vector3 camRel = _cam.transform.position - feet;
            float bodyMin = float.PositiveInfinity, bodyMax = float.NegativeInfinity, eyes = float.NaN;
            var eyeYs = new List<float>();
            foreach (Renderer r in _player.GetComponentsInChildren<Renderer>(false))
            {
                bodyMin = Mathf.Min(bodyMin, r.bounds.min.y); bodyMax = Mathf.Max(bodyMax, r.bounds.max.y);
                if (r.name == "Sphere" || r.name == "Sphere.001") eyeYs.Add(r.bounds.center.y);
            }
            if (eyeYs.Count > 0) { eyes = 0f; foreach (float e in eyeYs) eyes += e; eyes /= eyeYs.Count; }
            Vector3 fwd = _player.transform.forward;
            j.AppendLine($"  \"player\": {{ \"feet\": {V(feet)}, \"grounded\": {(_cc.isGrounded ? "true" : "false")}, " +
                         $"\"cc_height\": {F(_cc.height)}, \"cc_radius\": {F(_cc.radius)}, \"cc_center\": {V(_cc.center)}, " +
                         $"\"cc_skin\": {F(_cc.skinWidth)}, \"cc_step_effective\": {F(_cc.stepOffset)}, \"cc_slope\": {F(_cc.slopeLimit)}, " +
                         $"\"model_min_above_feet\": {F(bodyMin - feet.y)}, \"model_top_above_feet\": {F(bodyMax - feet.y)}, " +
                         $"\"eye_spheres_above_feet\": {F(eyes - feet.y)}, " +
                         $"\"camera_above_feet\": {F(camRel.y)}, \"camera_forward_of_feet\": {F(Vector3.Dot(camRel, fwd))}, " +
                         $"\"camera_near\": {F(_cam.nearClipPlane)}, \"camera_far\": {F(_cam.farClipPlane)}, " +
                         $"\"walk\": {F(Field("walkSpeed"))}, \"sprint\": {F(Field("sprintSpeed"))}, \"jump\": {F(Field("jumpHeight"))}, \"gravity\": {F(Field("gravity"))} }},");

            // the drunk: live, animated, from the pool
            t = 0f;
            while ((_manager == null || _manager.ActiveActors.Count == 0) && t < 20f) { t += Time.deltaTime; yield return null; }
            EventActor actor = _manager != null && _manager.ActiveActors.Count > 0 ? _manager.ActiveActors[0] : null;
            if (actor == null) { j.AppendLine("  \"drunk\": null,"); }
            else
            {
                EventPhase phase = EventPhase.Spawn;
                actor.PhaseChanged += p => phase = p;
                var idle = new List<float[]>(); var walk = new List<float[]>();
                Transform head = FindBone(actor.transform, "HeadTop_End") ?? FindBone(actor.transform, "Head");
                Transform lf = FindBone(actor.transform, "LeftToeBase") ?? FindBone(actor.transform, "LeftFoot");
                Transform rf = FindBone(actor.transform, "RightToeBase") ?? FindBone(actor.transform, "RightFoot");
                t = 0f;
                while (t < 14f && actor.isActiveAndEnabled)
                {
                    yield return new WaitForEndOfFrame();
                    Bounds b = actor.Bounds;
                    float root = actor.transform.position.y;
                    float[] row = { b.size.y, b.min.y - root, b.max.y - root,
                                    head != null ? head.position.y - root : float.NaN,
                                    lf != null ? lf.position.y - root : float.NaN, rf != null ? rf.position.y - root : float.NaN,
                                    b.size.x, b.size.z };
                    if (phase == EventPhase.Spawn) idle.Add(row); else if (phase == EventPhase.Build) walk.Add(row);
                    t += Time.deltaTime;
                }
                var agent = actor.GetComponent<NavMeshAgent>();
                Transform arm = actor.transform.Find("Armature");
                j.AppendLine($"  \"drunk\": {{ \"armature_scale\": {F(arm != null ? arm.localScale.x : float.NaN, "0.#####")}, " +
                             $"\"agent_radius\": {F(agent.radius)}, \"agent_height\": {F(agent.height)}, \"agent_speed\": {F(agent.speed)}, " +
                             $"\"head_bone\": \"{(head != null ? head.name : "none")}\", \"foot_bones\": \"{(lf != null ? lf.name : "none")}/{(rf != null ? rf.name : "none")}\",");
                j.AppendLine($"    \"idle\": {Stats(idle)},");
                j.AppendLine($"    \"walk\": {Stats(walk)} }},");
            }

            // the reference objects
            j.AppendLine("  \"objects\": {");
            for (int i = 0; i < _spots.Length; i++)
            {
                Dictionary<string, float> m = MeasureSpot(_spots[i]);
                j.Append($"    \"{_spots[i].name}\": {{ \"object\": \"{_spots[i].objectName}\"");
                foreach (var kv in m) j.Append($", \"{kv.Key}\": {F(kv.Value)}");
                j.AppendLine(i < _spots.Length - 1 ? " }," : " }");
            }
            j.AppendLine("  },");
            var s0 = NavMesh.GetSettingsByID(0);
            j.AppendLine($"  \"navmesh_agent0\": {{ \"radius\": {F(s0.agentRadius)}, \"height\": {F(s0.agentHeight)}, \"climb\": {F(s0.agentClimb)}, \"slope\": {F(s0.agentSlope)} }}");
            j.AppendLine("}");
            File.WriteAllText(Path.Combine(outputDir, "start.json"), j.ToString());
            _log.AppendLine(j.ToString());
        }

        private static Transform FindBone(Transform root, string suffix)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith(suffix)) return t;
            return null;
        }

        private static string Stats(List<float[]> rows)
        {
            if (rows.Count == 0) return "null";
            string[] names = { "bounds_height", "bounds_min_rel_root", "bounds_top_rel_root", "head_bone_rel_root",
                               "left_toe_rel_root", "right_toe_rel_root", "bounds_x", "bounds_z" };
            var sb = new StringBuilder("{ \"frames\": " + rows.Count);
            for (int c = 0; c < names.Length; c++)
            {
                var col = new List<float>();
                foreach (float[] r in rows) if (!float.IsNaN(r[c])) col.Add(r[c]);
                if (col.Count == 0) continue;
                col.Sort();
                sb.Append($", \"{names[c]}\": {{ \"min\": {F(col[0])}, \"median\": {F(col[col.Count / 2])}, \"max\": {F(col[col.Count - 1])} }}");
            }
            return sb.Append(" }").ToString();
        }

        // =============================================================================================
        // photos (Tasks 2.4 / 3 / 9)
        // =============================================================================================

        private IEnumerator Photos()
        {
            // An empty street: no live drunk (the manager is off and nothing has spawned), only the posed one.
            if (_manager != null)
            {
                _manager.enabled = false;
                foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false);
            }

            // Instantiated under an INACTIVE holder so EventActor.Awake never runs (it is a prop, not an event),
            // then switched off and released into the world.
            var holder = new GameObject("PosedDrunkHolder (rig)");
            holder.SetActive(false);
            GameObject drunk = Instantiate(drunkPrefab, holder.transform);
            drunk.name = "PosedDrunk (rig)";
            foreach (var c in drunk.GetComponents<MonoBehaviour>()) c.enabled = false;
            drunk.transform.SetParent(null, false);
            Destroy(holder);
            var ag = drunk.GetComponent<NavMeshAgent>(); if (ag != null) ag.enabled = false;
            foreach (var au in drunk.GetComponentsInChildren<AudioSource>()) au.enabled = false;
            Transform arm = drunk.transform.Find("Armature");
            if (arm != null) arm.localScale = Vector3.one * drunkArmatureScale;
            var anim = drunk.GetComponent<Animator>();
            anim.Play("DrunkIdle", 0, 0.25f);
            Renderer[] drunkRenderers = drunk.GetComponentsInChildren<Renderer>();

            var witnessGo = new GameObject("WitnessCam (rig)");
            Camera wc = witnessGo.AddComponent<Camera>();
            wc.orthographic = true;
            wc.enabled = false;
            wc.clearFlags = CameraClearFlags.Skybox;

            var meta = new StringBuilder("{\n  \"option\": \"" + option + "\", \"town_scale\": " + F(worldScale) + ",\n  \"spots\": [\n");
            for (int i = 0; i < _spots.Length; i++)
            {
                ScaleSpot sp = _spots[i];
                Vector3 stand = S(sp.stand), look = S(sp.look), dpos = S(sp.drunk);
                float yaw = YawTo(stand, look);

                float dg = GroundY(dpos, dpos.y + 3f * worldScale);
                drunk.transform.SetPositionAndRotation(new Vector3(dpos.x, dg, dpos.z),
                                                       Quaternion.Euler(0f, YawTo(dpos, stand), 0f));
                float from = stand.y + 3f * worldScale;    // from just above the spot, never from the sky (canopies)
                Teleport(stand, yaw, from);
                yield return Settle(1.0f);              // land, let the animator pose him, let the chase camera settle
                Teleport(stand, yaw, from);             // in case the landing slid
                yield return Settle(0.4f);
                if (sp.kind == "wall")
                {
                    // Pressed against the wall through the REAL input path: hold W into it, then let go.
                    Keys(Key.W);
                    yield return Settle(1.0f);
                    Keys();
                    yield return Settle(0.4f);
                }
                yield return new WaitForEndOfFrame();   // AFTER ThirdPersonCamera.LateUpdate: the game's own pose

                Vector3 feet = _player.transform.position;
                Vector3 camPos = _cam.transform.position;
                float eye = camPos.y - feet.y;
                RenderTo(_cam, Path.Combine(outputDir, $"eye_{sp.name}.png"), out float stdEye);

                Bounds db = drunkRenderers[0].bounds;
                foreach (Renderer r in drunkRenderers) db.Encapsulate(r.bounds);
                float playerTop = float.NegativeInfinity;
                foreach (Renderer r in _player.GetComponentsInChildren<Renderer>(false)) playerTop = Mathf.Max(playerTop, r.bounds.max.y);

                Dictionary<string, float> m = MeasureSpot(sp);
                if (sp.kind == "wall")
                {
                    float toWall = HitDistance(camPos, _cam.transform.forward, 10f * worldScale);
                    m["camera_to_wall"] = toWall;
                    m["near_clip"] = _cam.nearClipPlane;
                    m["near_plane_past_wall"] = _cam.nearClipPlane - toWall;   // > 0: the near plane is inside the wall
                    m["capsule_radius"] = _cc.radius;
                    m["ground"] = feet.y;
                }
                float ground = m.ContainsKey("ground") ? m["ground"] : feet.y;

                // --- witness: orthographic, pitch 0, from the side, foreground cut by the near plane ---------
                Vector3 dir = Quaternion.Euler(0f, sp.witnessYaw, 0f) * Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, dir);
                Vector3 anchor = S(sp.anchor);
                Vector3 centre = (feet + db.center + anchor) / 3f;
                float along0 = Mathf.Min(Vector3.Dot(feet, dir), Mathf.Min(Vector3.Dot(db.center, dir), Vector3.Dot(anchor, dir)));
                float lowY = Mathf.Min(Mathf.Min(feet.y, db.min.y), ground);
                Renderer objR = FindRenderer(sp.objectName);
                float objTop = sp.kind == "car" ? UnionBounds(sp, out _).max.y
                             : objR != null && sp.kind != "street" ? objR.bounds.max.y : ground;
                float topY = Mathf.Max(Mathf.Max(playerTop, db.max.y), Mathf.Max(objTop, camPos.y));
                float[] xs = { Vector3.Dot(feet, right), Vector3.Dot(db.center, right), Vector3.Dot(anchor, right) };
                float xMin = Mathf.Min(xs[0], Mathf.Min(xs[1], xs[2])), xMax = Mathf.Max(xs[0], Mathf.Max(xs[1], xs[2]));
                if (sp.kind == "street") { xMin -= 8f * worldScale; xMax += 8f * worldScale; }
                if (sp.kind == "car") { xMin -= 2.5f * worldScale; xMax += 1f * worldScale; }
                float span = topY - lowY;
                float halfH = Mathf.Max(span * 0.62f, ((xMax - xMin) * 0.5f + 1.5f * worldScale) / (W / (float)H));
                float camDist = 200f * worldScale;
                Vector3 mid = right * ((xMin + xMax) * 0.5f) + Vector3.up * (lowY + span * 0.5f);
                Vector3 camAt = mid + dir * (along0 - camDist);     // right/up/dir are orthonormal
                wc.transform.SetPositionAndRotation(camAt, Quaternion.LookRotation(dir, Vector3.up));
                wc.orthographicSize = halfH;
                wc.nearClipPlane = camDist - sp.witnessNear * worldScale;
                wc.farClipPlane = camDist + 400f * worldScale;
                RenderTo(wc, Path.Combine(outputDir, $"witness_{sp.name}_raw.png"), out float stdW);

                // projected heights for the annotator (orthographic + pitch 0 → a height is one screen row)
                var lines = new List<KeyValuePair<string, float>>
                {
                    new KeyValuePair<string, float>("feet", feet.y),
                    new KeyValuePair<string, float>("eye (camera)", camPos.y),
                    new KeyValuePair<string, float>("player top", playerTop),
                    new KeyValuePair<string, float>("drunk top", db.max.y),
                };
                foreach (var kv in m)
                {
                    if (kv.Key == "car_roof") lines.Add(new KeyValuePair<string, float>("car roof", ground + kv.Value));
                    if (kv.Key == "door_head_y") lines.Add(new KeyValuePair<string, float>("door head", kv.Value));
                    if (kv.Key == "deck") lines.Add(new KeyValuePair<string, float>("porch deck", kv.Value));
                    if (kv.Key == "canopy_low") lines.Add(new KeyValuePair<string, float>("canopy low", ground + kv.Value));
                    if (kv.Key == "tree_top") lines.Add(new KeyValuePair<string, float>("tree top", ground + kv.Value));
                    if (kv.Key == "eave") lines.Add(new KeyValuePair<string, float>("eave", ground + kv.Value));
                    if (kv.Key == "ridge") lines.Add(new KeyValuePair<string, float>("ridge", ground + kv.Value));
                    if (kv.Key == "road_surface_y") lines.Add(new KeyValuePair<string, float>("road surface", kv.Value));
                }

                meta.Append($"    {{ \"name\": \"{sp.name}\", \"kind\": \"{sp.kind}\", \"object\": \"{sp.objectName}\", " +
                            $"\"stand\": {V(stand)}, \"yaw\": {F(yaw)}, \"feet\": {V(feet)}, \"camera\": {V(camPos)}, " +
                            $"\"eye_height\": {F(eye)}, \"camera_forward_of_feet\": {F(Vector3.Dot(camPos - feet, _player.transform.forward))}, " +
                            $"\"player_height\": {F(playerTop - feet.y)}, \"drunk_height\": {F(db.size.y)}, \"drunk_feet\": {F(db.min.y)}, " +
                            $"\"eye_luma_std\": {F(stdEye)}, \"witness_luma_std\": {F(stdW)}, \"metrics\": {{");
                bool first = true;
                foreach (var kv in m) { meta.Append($"{(first ? "" : ", ")}\"{kv.Key}\": {F(kv.Value)}"); first = false; }
                meta.Append("}, \"witness\": { \"lines\": [");
                for (int k = 0; k < lines.Count; k++)
                {
                    Vector3 sp3 = wc.WorldToScreenPoint(new Vector3(camAt.x, lines[k].Value, camAt.z) + dir * camDist);
                    meta.Append($"{(k > 0 ? ", " : "")}{{ \"label\": \"{lines[k].Key}\", \"y_world\": {F(lines[k].Value)}, " +
                                $"\"above_feet\": {F(lines[k].Value - feet.y)}, \"above_ground\": {F(lines[k].Value - ground)}, \"y_px\": {F(H - sp3.y, "0.#")} }}");
                }
                Vector3 pPx = wc.WorldToScreenPoint(feet), dPx = wc.WorldToScreenPoint(db.center), oPx = wc.WorldToScreenPoint(anchor);
                meta.Append($"], \"x_px\": {{ \"player\": {F(pPx.x, "0.#")}, \"drunk\": {F(dPx.x, "0.#")}, \"object\": {F(oPx.x, "0.#")} }} }} }}");
                meta.AppendLine(i < _spots.Length - 1 ? "," : "");
                _log.AppendLine($"{sp.name}: eye {F(eye)} drunk h {F(db.size.y)} player h {F(playerTop - feet.y)} eye std {F(stdEye)} witness std {F(stdW)}");
            }
            meta.AppendLine("  ]\n}");
            File.WriteAllText(Path.Combine(outputDir, "photos.json"), meta.ToString());
            Destroy(witnessGo);
            Destroy(drunk);
        }

        // =============================================================================================
        // crossing (Task 4.1)
        // =============================================================================================

        private struct Route { public string Name; public Vector3 From, To; public bool FromSpawn; public Vector3[] Via; }

        private IEnumerator Crossing()
        {
            if (_manager != null) _manager.enabled = false;
            var routes = new[]
            {
                // Spawn sits in a 1.07 m gap between a police-tape barrier and the police car; the straight line to
                // the route runs through pine Circle.002. The waypoints are the path a player walks (clear for a
                // 0.3 m capsule, edit-mode capsule scan); the 0.95 m giant does not fit the first gap at all.
                new Route { Name = "spawn_to_route_mid", FromSpawn = true, To = new Vector3(30f, 0f, 19f),
                            Via = new[] { new Vector3(0.45f, 0f, 3.6f), new Vector3(24f, 0f, 3.6f) } },
                new Route { Name = "plain_line_810m", From = new Vector3(-600f, 0f, -8.5f), To = new Vector3(210f, 0f, -8.5f) },
                // Not x -300: a north-south line of tyres (tire.068_Colore_0, x -223..-213, z 3..236) fences the
                // plain's west quarter off at person scale (the 0.95 m/step-2 giant steps over it; the road gap is
                // the only way through). (-200, 100) is inside it, on open ground, clear for a 0.3 m capsule.
                new Route { Name = "far_west_to_route_mid", From = new Vector3(-200f, 0f, 100f), To = new Vector3(30f, 0f, 19f) },
                new Route { Name = "far_north_to_route_mid", From = new Vector3(150f, 0f, 200f), To = new Vector3(30f, 0f, 19f) },
            };

            var csv = new StringBuilder("route,gait,set_speed,straight_m,path_m,time_s,mean_speed,arrived,blocked_at,max_lateral_m,start_ground,end_ground\n");
            var trace = new StringBuilder("route,gait,t,x,y,z\n");

            // drive control first: nothing held → nothing moves; W → moves; release → stops
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            Vector3 spawnLanding = _player.transform.position;
            yield return DriveControl();

            foreach (string gait in new[] { "walk", "sprint" })
            {
                foreach (Route r in routes)
                {
                    Vector3 from = r.FromSpawn ? spawnLanding : S(r.From);
                    var legs = new List<Vector3>();
                    if (r.Via != null) foreach (Vector3 v in r.Via) legs.Add(S(v));
                    legs.Add(S(r.To));
                    for (int k = 0; k < legs.Count; k++) { Vector3 w = legs[k]; w.y = GroundY(w, 30f * worldScale); legs[k] = w; }
                    Vector3 to = legs[legs.Count - 1];
                    Teleport(from, YawTo(from, legs[0]), r.FromSpawn ? from.y + 1f : 30f * worldScale);
                    Keys();
                    yield return Settle(0.8f);
                    from = _player.transform.position;
                    float setSpeed = Field(gait == "walk" ? "walkSpeed" : "sprintSpeed");
                    float straight = 0f;
                    { Vector3 a0 = from; foreach (Vector3 w in legs) { straight += Vector2.Distance(new Vector2(a0.x, a0.z), new Vector2(w.x, w.z)); a0 = w; } }
                    int leg = 0;
                    float timeout = Mathf.Max(20f, 3f * straight / Mathf.Max(0.1f, setSpeed));

                    if (gait == "walk") Keys(Key.W); else Keys(Key.W, Key.LeftShift);
                    float t = 0f, path = 0f, maxLat = 0f, nextTrace = 0f, stuckT = 0f;
                    Vector3 prev = _player.transform.position, stuckRef = prev;
                    bool arrived = false; string blockedAt = "";
                    Vector3 legFrom = from;
                    Vector3 line = (legs[0] - from); line.y = 0f; line.Normalize();
                    while (t < timeout)
                    {
                        Vector3 p = _player.transform.position;
                        Vector3 toT = legs[leg] - p; toT.y = 0f;
                        if (toT.magnitude < 0.6f * worldScale || Vector3.Dot(toT, line) < 0f)
                        {
                            if (leg == legs.Count - 1) { arrived = true; break; }
                            legFrom = legs[leg]; leg++;
                            line = legs[leg] - legFrom; line.y = 0f; line.Normalize();
                            continue;
                        }
                        _player.transform.rotation = Quaternion.Euler(0f, YawTo(p, legs[leg]), 0f);   // the mouse's job
                        yield return null;
                        t += Time.deltaTime;
                        Vector3 q = _player.transform.position;
                        path += Vector3.Distance(new Vector3(q.x, 0, q.z), new Vector3(prev.x, 0, prev.z));
                        prev = q;
                        Vector3 off = q - legFrom; off.y = 0f;
                        maxLat = Mathf.Max(maxLat, (off - Vector3.Dot(off, line) * line).magnitude);
                        if (t >= nextTrace) { trace.AppendLine($"{r.Name},{gait},{F(t)},{F(q.x)},{F(q.y)},{F(q.z)}"); nextTrace += 0.25f; }
                        stuckT += Time.deltaTime;
                        if (stuckT > 3f)
                        {
                            if (Vector3.Distance(new Vector3(q.x, 0, q.z), new Vector3(stuckRef.x, 0, stuckRef.z)) < 0.15f * setSpeed * 3f)
                            { blockedAt = V(q).Replace(",", " "); break; }
                            stuckT = 0f; stuckRef = q;
                        }
                    }
                    Keys();
                    Vector3 end = _player.transform.position;
                    csv.AppendLine($"{r.Name},{gait},{F(setSpeed)},{F(straight)},{F(path)},{F(t)},{F(path / Mathf.Max(0.001f, t))}," +
                                   $"{(arrived ? 1 : 0)},{blockedAt},{F(maxLat)},{F(from.y)},{F(end.y)}");
                    _log.AppendLine($"{r.Name} {gait}: {F(straight)} m straight in {F(t)} s, path {F(path)} m, arrived {arrived} {blockedAt}");
                    yield return Settle(0.6f);
                }
            }
            File.WriteAllText(Path.Combine(outputDir, "crossing.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "crossing-trace.csv"), trace.ToString());
        }

        /// <summary>Proves the input drive before any number is trusted (story 1d).</summary>
        private IEnumerator DriveControl()
        {
            Keys();
            yield return Settle(0.5f);
            Vector3 a = _player.transform.position;
            yield return Settle(1.0f);
            Vector3 b = _player.transform.position;
            Keys(Key.W);
            yield return Settle(1.5f);
            Vector3 c = _player.transform.position;
            Keys();
            yield return Settle(0.3f);
            Vector3 d = _player.transform.position;
            yield return Settle(1.0f);
            Vector3 e = _player.transform.position;
            float hz(Vector3 p, Vector3 q) => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z));
            _log.AppendLine($"DRIVE CONTROL: no keys 1.0 s → moved {F(hz(a, b))} m; W 1.5 s → moved {F(hz(b, c))} m; " +
                            $"released → moved {F(hz(d, e))} m in the next 1.0 s. keyboard {(Keyboard.current != null ? Keyboard.current.name : "NONE")}");
        }

        // =============================================================================================
        // movement + raise (Tasks 2.7 / 8.4)
        // =============================================================================================

        private IEnumerator Movement()
        {
            if (_manager != null) _manager.enabled = false;
            var csv = new StringBuilder("test,t,x,y,z,vx,vy,vz,grounded\n");
            var sum = new StringBuilder();
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }

            Vector3 flat = new Vector3(60f, 0f, -2f);         // on the road slab, open both ways
            Teleport(S(flat), 270f, 30f * worldScale);
            yield return Settle(1f);
            yield return DriveControl();

            // walk and sprint: steady-state horizontal speed over the last 2 s of a 4 s hold
            foreach (string gait in new[] { "walk", "sprint" })
            {
                Teleport(S(flat), 270f, 30f * worldScale);
                yield return Settle(0.8f);
                if (gait == "walk") Keys(Key.W); else Keys(Key.W, Key.LeftShift);
                yield return Record(csv, gait, 4f);
                Vector3 a = _samples[_samples.Count / 2], b = _samples[_samples.Count - 1];
                float dt = _sampleT[_samples.Count - 1] - _sampleT[_samples.Count / 2];
                float speed = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z)) / dt;
                Keys();
                yield return Record(csv, gait + "_release", 1f);
                Vector3 r0 = _samples[3], r1 = _samples[_samples.Count - 1];
                sum.AppendLine($"{gait}: measured {F(speed)} m/s vs configured {F(Field(gait == "walk" ? "walkSpeed" : "sprintSpeed"))}; " +
                               $"after release moved {F(Vector2.Distance(new Vector2(r0.x, r0.z), new Vector2(r1.x, r1.z)))} m");
            }

            // jump from standing
            Teleport(S(flat), 270f, 30f * worldScale);
            yield return Settle(1f);
            float y0 = _player.transform.position.y;
            Keys(Key.Space);
            yield return null;
            Keys();
            yield return Record(csv, "jump", 2.5f);
            float apex = float.NegativeInfinity, apexT = 0f, landT = float.NaN; bool left = false;
            for (int i = 0; i < _samples.Count; i++)
            {
                if (_samples[i].y > apex) { apex = _samples[i].y; apexT = _sampleT[i]; }
                if (!_grounded[i]) left = true;
                if (left && _grounded[i] && float.IsNaN(landT)) landT = _sampleT[i];
            }
            float jh = Field("jumpHeight"), g = Field("gravity");
            float v0 = Mathf.Sqrt(2f * jh * -g);
            sum.AppendLine($"jump: apex {F(apex - y0)} m (configured jumpHeight {F(jh)}), apex at {F(apexT)} s (expected {F(v0 / -g)}), " +
                           $"landed (isGrounded again) at {F(landT)} s (expected air time {F(2f * v0 / -g)}), expected v0 {F(v0)} m/s; " +
                           $"landed y {F(_samples[_samples.Count - 1].y)} vs start {F(y0)}");

            // a real kerb, grass → road at x≈0 (0.10 m in today's town); and the road slab's raised edge at x 30
            // x 8: clear of the bus-shelter roof (x -7.9..6.7, ~9 m above the grass; the 8.86 m giant hits it) and
            // the police car (x 1..5). The kerb's height is measured, not assumed: road top minus grass beside it.
            // x 0.5: the road's real kerb (~0.1 m) — a person fits under the shelter roof here; the 0.95 m giant
            // does not (it hits the roof ~9 m up), so at the old scale this test reports "blocked", which is true.
            // (x 8 is a 0.73 m grass-to-road ledge, not a kerb.)
            foreach (var test in new[] { ("kerb_x0.5", new Vector3(0.5f, 0f, -11.5f), 0f, 3f),
                                         ("slab_edge_x30_north", new Vector3(30f, 0f, 9f), 180f, 3.5f) })
            {
                Teleport(S(test.Item2), test.Item3, 30f * worldScale);
                yield return Settle(1f);
                float startY = _player.transform.position.y;
                Vector3 fwd = Quaternion.Euler(0f, test.Item3, 0f) * Vector3.forward;
                float kerbLow = float.NaN, kerbHigh = float.NaN;
                for (float d = 0f; d < 8f * worldScale; d += 0.02f * worldScale)
                {
                    Vector3 a = S(test.Item2) + fwd * d, b = a + fwd * 0.02f * worldScale;
                    float ya = GroundY(a, startY + 1.5f * worldScale), yb = GroundY(b, startY + 1.5f * worldScale);
                    if (yb - ya > 0.04f * worldScale) { kerbLow = ya; kerbHigh = yb; break; }
                }
                sum.AppendLine($"{test.Item1}: measured ledge ahead {F(kerbHigh - kerbLow)} m (from {F(kerbLow)} to {F(kerbHigh)})");
                Keys(Key.W);
                yield return Record(csv, test.Item1, test.Item4);
                Keys();
                float maxY = float.NegativeInfinity; foreach (Vector3 s in _samples) maxY = Mathf.Max(maxY, s.y);
                Vector3 end = _samples[_samples.Count - 1];
                sum.AppendLine($"{test.Item1}: start y {F(startY)} → end y {F(end.y)} (max {F(maxY)}), end z {F(end.z)}, " +
                               $"step offset {F(_cc.stepOffset)}; {(end.y > startY + 0.05f * worldScale ? "STEPPED UP" : "did not rise")}");
            }

            // raise / lower: the real path (SetPhotoMode, which the RaiseCamera input calls), 10 each
            sum.AppendLine(RaiseLowerHeader);
            yield return RaiseLower(sum);

            File.WriteAllText(Path.Combine(outputDir, "movement.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "movement-summary.txt"), sum.ToString());
            _log.Append(sum);
        }

        private const string RaiseLowerHeader = "raise/lower: seconds from SetPhotoMode() to the viewfinder CanvasGroup reaching its target alpha (unscaled, measured each frame)";
        private readonly List<Vector3> _samples = new List<Vector3>();
        private readonly List<float> _sampleT = new List<float>();
        private readonly List<bool> _grounded = new List<bool>();

        private IEnumerator Record(StringBuilder csv, string test, float seconds)
        {
            _samples.Clear(); _sampleT.Clear(); _grounded.Clear();
            float t = 0f;
            Vector3 prev = _player.transform.position;
            while (t < seconds)
            {
                yield return null;
                float dt = Mathf.Max(1e-5f, Time.deltaTime);
                t += Time.deltaTime;
                Vector3 p = _player.transform.position;
                Vector3 v = (p - prev) / dt;
                prev = p;
                _samples.Add(p); _sampleT.Add(t); _grounded.Add(_cc.isGrounded);
                csv.AppendLine($"{test},{F(t, "0.####")},{F(p.x)},{F(p.y, "0.####")},{F(p.z)},{F(v.x)},{F(v.y)},{F(v.z)},{(_cc.isGrounded ? 1 : 0)}");
            }
        }

        private IEnumerator RaiseLower(StringBuilder sum)
        {
            if (_photo == null) { sum.AppendLine("  no PhotoModeController in scene"); yield break; }
            FieldInfo vf = typeof(PhotoModeController).GetField("viewfinderRoot", BindingFlags.Instance | BindingFlags.NonPublic);
            var root = vf != null ? vf.GetValue(_photo) as GameObject : null;
            CanvasGroup group = root != null ? root.GetComponent<CanvasGroup>() : null;
            if (group == null) { sum.AppendLine("  no viewfinder CanvasGroup — cannot time the raise"); yield break; }
            float worstUp = 0f, worstDown = 0f;
            var ups = new List<string>(); var downs = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                foreach (bool raise in new[] { true, false })
                {
                    double start = Time.realtimeSinceStartupAsDouble;
                    _photo.SetPhotoMode(raise);
                    float target = raise ? 1f : 0f;
                    int frames = 0;
                    while (Mathf.Abs(group.alpha - target) > 0.001f && frames < 600) { yield return null; frames++; }
                    float took = (float)(Time.realtimeSinceStartupAsDouble - start);
                    if (raise) { worstUp = Mathf.Max(worstUp, took); ups.Add(F(took, "0.000")); }
                    else { worstDown = Mathf.Max(worstDown, took); downs.Add(F(took, "0.000")); }
                    yield return Settle(0.2f);
                }
            }
            sum.AppendLine($"  raises: {string.Join(" ", ups)}  → worst {F(worstUp, "0.000")} s");
            sum.AppendLine($"  lowers: {string.Join(" ", downs)}  → worst {F(worstDown, "0.000")} s");
        }

        // =============================================================================================
        // film — one lifecycle of the drunk walking his route, from a fixed camera at a person's eye beside it,
        // for the human check on how his walk reads (FR11). JPG frames at 30 fps; ffmpeg joins them afterwards.
        // =============================================================================================

        private IEnumerator Film()
        {
            if (_tpc != null) _tpc.enabled = false;
            var go = new GameObject("FilmCam (rig)");
            Camera fc = go.AddComponent<Camera>();
            fc.enabled = false;
            fc.fieldOfView = 50f;
            fc.nearClipPlane = 0.1f;
            Vector3 mid = S(new Vector3(26f, 0f, 19f)); mid.y = GroundY(mid, 5f * worldScale);
            Vector3 at = S(new Vector3(26f, 0f, 27f)); at.y = GroundY(at, 5f * worldScale) + 1.65f * worldScale;
            fc.transform.SetPositionAndRotation(at, Quaternion.LookRotation(mid + Vector3.up * 0.9f * worldScale - at));
            string dir = Path.Combine(outputDir, "frames");
            Directory.CreateDirectory(dir);

            float t = 0f;
            while ((_manager.ActiveActors.Count == 0) && t < 20f) { t += Time.deltaTime; yield return null; }
            // start at a fresh spawn so the clip is one whole lifecycle
            EventActor first = _manager.ActiveActors.Count > 0 ? _manager.ActiveActors[0] : null;
            t = 0f;
            while (t < 30f && _manager.ActiveActors.Count > 0 && _manager.ActiveActors[0] == first) { t += Time.deltaTime; yield return null; }
            while (_manager.ActiveActors.Count == 0 && t < 60f) { t += Time.deltaTime; yield return null; }

            RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            fc.targetTexture = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            int n = 0; float clock = 0f, next = 0f;
            var log = new StringBuilder("frame,t,x,z,at_peak" + System.Environment.NewLine);
            while (clock < 27f)
            {
                yield return new WaitForEndOfFrame();
                clock += Time.deltaTime;
                if (clock < next) continue;
                next += 1f / 30f;
                fc.Render();
                RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, $"f{n:0000}.jpg"), tex.EncodeToJPG(88));
                EventActor a = _manager.ActiveActors.Count > 0 ? _manager.ActiveActors[0] : null;
                log.AppendLine($"{n},{F(clock)},{(a != null ? F(a.transform.position.x) : "")},{(a != null ? F(a.transform.position.z) : "")},{(a != null && a.IsAtPeak ? 1 : 0)}");
                n++;
            }
            fc.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tex); Destroy(go);
            File.WriteAllText(Path.Combine(outputDir, "film.csv"), log.ToString());
            _log.AppendLine($"film: {n} frames at 30 fps from {V(at)} looking at {V(mid)}");
        }

        // =============================================================================================
        // cue (Tasks 2.6 / 8.2) — measured at the AudioListener, from the game's own output
        // =============================================================================================

        private IEnumerator Cue()
        {
            if (_chase != null) _chase.enabled = false;
            if (_tpc != null) _tpc.enabled = false;
            var tap = _cam.gameObject.AddComponent<ScaleCueTap>();

            var sb = new StringBuilder("ALL AUDIO SOURCES AT START (anything playing besides the drunk contaminates the sweep):\n");
            foreach (AudioSource a in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                sb.AppendLine($"  {a.name} active {a.isActiveAndEnabled} playing {a.isPlaying} clip {(a.clip != null ? a.clip.name : "none")} spatial {F(a.spatialBlend)}");
            _log.Append(sb);

            float t = 0f;
            while ((_manager.ActiveActors.Count == 0) && t < 20f) { t += Time.deltaTime; yield return null; }
            var csv = new StringBuilder("t,sweep,target_m,distance_m,actor,phase,since_peak_start_s,settled,rms,dbfs\n");
            EventActor current = null; EventPhase phase = EventPhase.Spawn; double peakStart = -1e9;
            System.Action<EventPhase> onPhase = p => { phase = p; if (p == EventPhase.Peak) peakStart = Time.timeAsDouble; };
            AudioSource src = null; string curveDesc = "";

            for (int sweep = 0; sweep < cueSweeps; sweep++)
            {
                foreach (float d in cueDistances)
                {
                    float held = 0f;
                    while (held < cueHoldSeconds)
                    {
                        EventActor a = _manager.ActiveActors.Count > 0 ? _manager.ActiveActors[0] : null;
                        if (a != current)
                        {
                            if (current != null) current.PhaseChanged -= onPhase;
                            current = a;
                            if (current != null) { current.PhaseChanged += onPhase; phase = EventPhase.Spawn; }
                            src = current != null ? current.GetComponent<AudioSource>() : null;
                            if (src != null && curveDesc.Length == 0)
                            {
                                AnimationCurve c = src.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
                                var cs = new StringBuilder($"cue source: rolloff {src.rolloffMode} min {F(src.minDistance)} max {F(src.maxDistance)} spatialBlend {F(src.spatialBlend)} volume {F(src.volume)}; curve(d) at:");
                                foreach (float dd in cueDistances) cs.Append($" {F(dd)}→{F(c.Evaluate(dd / src.maxDistance), "0.####")}");
                                curveDesc = cs.ToString();
                                _log.AppendLine(curveDesc);
                            }
                        }
                        if (src != null)
                        {
                            Vector3 sp = src.transform.position;
                            _cam.transform.SetPositionAndRotation(sp + Vector3.forward * d, Quaternion.LookRotation(Vector3.back));
                        }
                        yield return null;
                        held += Time.deltaTime;
                        float dist = src != null ? Vector3.Distance(_cam.transform.position, src.transform.position) : float.NaN;
                        foreach (Vector2 blk in tap.Drain())
                            csv.AppendLine($"{F(Time.time)},{sweep},{F(d)},{F(dist)},{(current != null ? current.GetInstanceID() : 0)}," +
                                           $"{(current != null && current.isActiveAndEnabled ? phase.ToString() : "none")},{F((float)(Time.timeAsDouble - peakStart))}," +
                                           $"{(held > 0.35f ? 1 : 0)},{F(blk.x, "0.000000")},{F(blk.y, "0.00")}");
                    }
                }
            }

            // FLOOR: the drunk DEACTIVATED (disabling the manager despawns nothing), listener where it is.
            if (_manager != null) _manager.enabled = false;
            foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false);
            if (current != null) current.gameObject.SetActive(false);
            float f = 0f;
            while (f < 3f)
            {
                yield return null;
                f += Time.deltaTime;
                foreach (Vector2 blk in tap.Drain())
                    csv.AppendLine($"{F(Time.time)},floor,0,,0,floor_deactivated,,{(f > 0.35f ? 1 : 0)},{F(blk.x, "0.000000")},{F(blk.y, "0.00")}");
            }
            File.WriteAllText(Path.Combine(outputDir, "sweep.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "curve.txt"), curveDesc + "\n");
        }
    }

    /// <summary>
    /// Taps the mixed output on the AudioListener's GameObject. OnAudioFilterRead runs on the audio thread, so
    /// blocks are handed over through a lock; each is (rms, dBFS) over every channel of the block.
    /// </summary>
    public class ScaleCueTap : MonoBehaviour
    {
        private readonly object _gate = new object();
        private readonly List<Vector2> _blocks = new List<Vector2>();

        private void OnAudioFilterRead(float[] data, int channels)
        {
            double s = 0;
            for (int i = 0; i < data.Length; i++) s += data[i] * (double)data[i];
            float rms = (float)System.Math.Sqrt(s / System.Math.Max(1, data.Length));
            float db = rms > 1e-9f ? 20f * Mathf.Log10(rms) : -180f;
            lock (_gate) _blocks.Add(new Vector2(rms, db));
        }

        public List<Vector2> Drain()
        {
            lock (_gate)
            {
                var copy = new List<Vector2>(_blocks);
                _blocks.Clear();
                return copy;
            }
        }
    }
}
#endif
