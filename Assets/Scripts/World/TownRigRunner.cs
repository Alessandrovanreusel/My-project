#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CameraGame.Events;
using CameraGame.PhotoMode;

namespace CameraGame.World
{
    /// <summary>Records every collider the player's CharacterController touches (rig-side, Story 2.2 AC2).</summary>
    public class TownHitRecorder : MonoBehaviour
    {
        public struct Hit { public float t; public Collider c; public Vector3 point, normal; }
        public readonly List<Hit> hits = new List<Hit>();
        public bool recording;
        private void OnControllerColliderHit(ControllerColliderHit h)
        {
            if (!recording || hits.Count > 20000) return;
            hits.Add(new Hit { t = Time.time, c = h.collider, point = h.point, normal = h.normal });
        }
    }

    /// <summary>
    /// Story 2.2's play-mode rig runner (the editor half is <c>TownRig</c>). Drives the REAL game: the player walks
    /// through the real input path (Keyboard state events → PlayerInput Send Messages → OnMove/OnSprint/OnJump), yaw is
    /// set on the player's transform each frame (the mouse's job), and pitch goes through queued Mouse delta events
    /// (OnLook). The private input fields are never written and the transform is never moved to make progress.
    ///
    /// Runtime assembly behind #if UNITY_EDITOR — an editor-assembly MonoBehaviour is "missing" on a scene object in
    /// play mode (CLAUDE.md trap). Modes live in partial files: TownRigRunner.*.cs.
    /// </summary>
    public partial class TownRigRunner : MonoBehaviour
    {
        [HideInInspector] public string mode = "fps";
        [HideInInspector] public string staging = "";
        [HideInInspector] public string outputDir;
        [HideInInspector] public string dataDir;

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
        private FallRecovery _fall;
        private TownHitRecorder _rec;

        private bool _prevRunInBackground;
        private InputSettings.EditorInputBehaviorInPlayMode _prevEditorInput;
        private InputSettings.BackgroundBehavior _prevBackground;
        private float _prevTimeScale;

        private static string F(float v, string fmt = "0.###") => v.ToString(fmt, Inv);
        private static string V(Vector3 v) => $"[{F(v.x)} {F(v.y)} {F(v.z)}]";

        private IEnumerator Start()
        {
            _prevRunInBackground = Application.runInBackground;
            _prevTimeScale = Time.timeScale;
            _prevEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            _prevBackground = InputSystem.settings.backgroundBehavior;
            Application.runInBackground = true;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            try
            {
                Directory.CreateDirectory(outputDir);
                _player = GameObject.Find("main characters");
                _cc = _player != null ? _player.GetComponent<CharacterController>() : null;
                _tpc = _player != null ? _player.GetComponent<ThirdPersonController>() : null;
                _fall = _player != null ? _player.GetComponent<FallRecovery>() : null;
                _cam = Camera.main;
                _chase = _cam != null ? _cam.GetComponent<ThirdPersonCamera>() : null;
                _manager = FindFirstObjectByType<EventManager>(FindObjectsInactive.Include);
                _photo = FindFirstObjectByType<PhotoModeController>(FindObjectsInactive.Include);
                _log.AppendLine($"TOWN RIG — mode {mode} {staging}; started {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}; unity {Application.unityVersion}; " +
                                $"screen {Screen.width}x{Screen.height}; quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
                if (_player == null || _cc == null || _cam == null) { _log.AppendLine("RIG BROKEN: no player / controller / main camera."); yield break; }
                _rec = _player.AddComponent<TownHitRecorder>();
                _log.AppendLine($"player at start {V(_player.transform.position)}; FallRecovery {(_fall != null ? "present" : "absent")}");
                switch (mode)
                {
                    case "fps": yield return Fps(); break;
                    case "walklines": yield return WalkLines(); break;
                    default: yield return Extra(); break;
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
                catch (IOException e) { Debug.LogError("[TownRig] could not write log: " + e.Message); }
                Debug.Log("[TownRig] DONE " + mode + " → " + outputDir);
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            }
        }

        /// <summary>Modes defined in other partial files hook in here.</summary>
        partial void ExtraModes(ref IEnumerator routine);

        private IEnumerator Extra()
        {
            IEnumerator r = null;
            ExtraModes(ref r);
            if (r == null) { _log.AppendLine("unknown mode " + mode); yield break; }
            yield return r;
        }

        // =============================================================================================
        // shared helpers
        // =============================================================================================

        private static void Keys(params Key[] keys)
        {
            if (Keyboard.current == null) return;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
        }

        private bool IsMine(Collider c) =>
            c != null && ((_player != null && c.transform.IsChildOf(_player.transform)) || c.GetComponentInParent<EventActor>() != null);

        private readonly RaycastHit[] _hitBuf = new RaycastHit[64];

        /// <summary>Highest surface under a point (any collider except the player, actors and triggers).</summary>
        private float GroundY(Vector3 p, float from = 400f)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, from, p.z), Vector3.down, _hitBuf, from + 400f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
                if (!IsMine(_hitBuf[i].collider) && _hitBuf[i].point.y > best) best = _hitBuf[i].point.y;
            return float.IsNegativeInfinity(best) ? float.NaN : best;
        }

        private static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>Rig placement (not gameplay): the CharacterController's own rules — off for the jump, on again so the
        /// next Move() starts clean.</summary>
        private void Place(Vector3 at, float yaw, float groundFrom = 400f)
        {
            float g = GroundY(at, groundFrom);
            float y = float.IsNaN(g) ? at.y : g + 0.05f;
            _cc.enabled = false;
            _player.transform.SetPositionAndRotation(new Vector3(at.x, y, at.z), Quaternion.Euler(0f, yaw, 0f));
            _cc.enabled = true;
            Physics.SyncTransforms();
        }

        private static IEnumerator Settle(float seconds)
        {
            float t = 0f;
            while (t < seconds) { t += Time.deltaTime; yield return null; }
        }

        private static float Hz(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>Proves the input drive before any number is trusted: no keys → no motion; W → walk; release → stop.</summary>
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
            _log.AppendLine($"DRIVE CONTROL: no keys 1.0 s → moved {F(Hz(a, b))} m; W 1.5 s → moved {F(Hz(b, c))} m; " +
                            $"released → moved {F(Hz(d, e))} m in the next 1.0 s. keyboard {(Keyboard.current != null ? Keyboard.current.name : "NONE")}");
        }

        /// <summary>Renders a camera into an offscreen target without moving it; returns the luma std-dev (a blank or
        /// uniform frame means the rig is broken, not the game).</summary>
        private static float RenderTo(Camera c, string path, int w = W, int h = H)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevT = c.targetTexture;
            c.targetTexture = rt;
            c.ResetAspect();
            c.Render();
            c.targetTexture = prevT;
            c.ResetAspect();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            float std = LumaStd(tex);
            if (path != null) File.WriteAllBytes(path, path.EndsWith(".png") ? tex.EncodeToPNG() : tex.EncodeToJPG(88));
            Destroy(tex);
            return std;
        }

        private static float LumaStd(Texture2D tex)
        {
            Color32[] px = tex.GetPixels32();
            double s = 0, s2 = 0; int n = 0;
            for (int i = 0; i < px.Length; i += 7)
            {
                double l = 0.2126 * px[i].r + 0.7152 * px[i].g + 0.0722 * px[i].b;
                s += l; s2 += l * l; n++;
            }
            double mean = s / n;
            return (float)System.Math.Sqrt(System.Math.Max(0, s2 / n - mean * mean));
        }

        private string ReadData(string rel)
        {
            string p = Path.Combine(dataDir, rel);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }

        // =============================================================================================
        // fps — the frame-time instrument (1k): a fixed camera tour, 5 s per viewpoint, 3 rounds, rotated
        // =============================================================================================

        [System.Serializable] public class Viewpoint { public string name; public float[] eye; public float[] look; }
        [System.Serializable] public class ViewpointList { public Viewpoint[] viewpoints; }

        private IEnumerator Fps()
        {
            string json = ReadData("fps/viewpoints.json");
            if (json == null) { _log.AppendLine("RIG BROKEN: no fps/viewpoints.json"); yield break; }
            Viewpoint[] vps = JsonUtility.FromJson<ViewpointList>(json).viewpoints;
            if (_tpc != null) _tpc.enabled = false;
            if (_chase != null) _chase.enabled = false;
            if (_fall != null) _fall.enabled = false;
            if (_photo != null) _photo.enabled = false;
            var csv = new StringBuilder("round,viewpoint,frame,unscaled_dt_ms\n");
            var sum = new StringBuilder("viewpoint");
            var med = new float[3, vps.Length];
            for (int r = 0; r < 3; r++) sum.Append($",round{r}_median_ms");
            sum.AppendLine(",median_of_rounds_ms,spread_ms,frames");
            yield return Settle(3f);   // let the town load in and the drunk spawn
            for (int r = 0; r < 3; r++)
            {
                for (int k = 0; k < vps.Length; k++)
                {
                    int v = (k + r) % vps.Length;   // rotate the order each round (lesson 28)
                    Viewpoint vp = vps[v];
                    Vector3 eye = new Vector3(vp.eye[0], vp.eye[1], vp.eye[2]);
                    Vector3 look = new Vector3(vp.look[0], vp.look[1], vp.look[2]);
                    _cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    yield return Settle(1f);
                    var dts = new List<float>();
                    float t = 0f;
                    while (t < 5f)
                    {
                        yield return null;
                        t += Time.unscaledDeltaTime;
                        dts.Add(Time.unscaledDeltaTime * 1000f);
                        csv.AppendLine($"{r},{vp.name},{dts.Count},{F(Time.unscaledDeltaTime * 1000f, "0.000")}");
                    }
                    dts.Sort();
                    med[r, v] = dts[dts.Count / 2];
                    if (r == 0) RenderTo(_cam, Path.Combine(outputDir, $"vp_{vp.name}.jpg"));
                }
            }
            for (int v = 0; v < vps.Length; v++)
            {
                var m = new List<float> { med[0, v], med[1, v], med[2, v] };
                m.Sort();
                sum.AppendLine($"{vps[v].name},{F(med[0, v], "0.00")},{F(med[1, v], "0.00")},{F(med[2, v], "0.00")},{F(m[1], "0.00")},{F(m[2] - m[0], "0.00")},");
            }
            File.WriteAllText(Path.Combine(outputDir, "frames.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "summary.csv"), sum.ToString());
            _log.Append(sum);
        }

        // =============================================================================================
        // walklines — straight lines through scatter and onto the slab edge (AC2 positive control and its after)
        // =============================================================================================

        [System.Serializable] public class Line { public string name; public string target; public float[] from; public float[] to; }
        [System.Serializable] public class LineList { public Line[] lines; }

        private IEnumerator WalkLines()
        {
            string json = ReadData("walk/lines.json");
            if (json == null) { _log.AppendLine("RIG BROKEN: no walk/lines.json"); yield break; }
            Line[] lines = JsonUtility.FromJson<LineList>(json).lines;
            if (_manager != null) foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false);
            if (_manager != null) _manager.enabled = false;
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            Place(new Vector3(60f, 0f, -2f), 270f);
            yield return Settle(1f);
            yield return DriveControl();

            var csv = new StringBuilder("line,target,gait,set_speed,length_m,time_s,arrived,mid_speed_min,mid_speed_median,mid_speed_max,stick_s,hits_total,hits_by_class\n");
            var trace = new StringBuilder("line,gait,t,x,y,z,speed\n");
            var hitsCsv = new StringBuilder("line,gait,t,class,collider,x,y,z,nx,ny,nz\n");
            foreach (string gait in new[] { "walk", "sprint" })
            {
                foreach (Line ln in lines)
                {
                    Vector3 a = new Vector3(ln.from[0], 0f, ln.from[1]), b = new Vector3(ln.to[0], 0f, ln.to[1]);
                    Place(a, YawTo(a, b));
                    Keys();
                    yield return Settle(0.8f);
                    a = _player.transform.position;
                    float setSpeed = gait == "walk" ? (_tpc.Config != null ? _tpc.Config.SafeWalkSpeed : 4f) : (_tpc.Config != null ? _tpc.Config.SafeSprintSpeed : 7f);
                    float len = Hz(a, b);
                    _rec.hits.Clear();
                    _rec.recording = true;
                    if (gait == "walk") Keys(Key.W); else Keys(Key.W, Key.LeftShift);
                    float t = 0f, stick = 0f, below = 0f, timeout = Mathf.Max(10f, 3f * len / setSpeed);
                    bool arrived = false;
                    Vector3 prev = _player.transform.position;
                    var mid = new List<float>();
                    while (t < timeout)
                    {
                        Vector3 p = _player.transform.position;
                        Vector3 toB = b - p; toB.y = 0f;
                        if (toB.magnitude < 0.5f || Vector3.Dot(toB, b - a) < 0f) { arrived = true; break; }
                        _player.transform.rotation = Quaternion.Euler(0f, YawTo(p, b), 0f);   // the mouse's job
                        yield return null;
                        float dt = Mathf.Max(1e-5f, Time.deltaTime);
                        t += Time.deltaTime;
                        Vector3 q = _player.transform.position;
                        float sp = Hz(q, prev) / dt;
                        prev = q;
                        float along = Vector3.Dot(new Vector3(q.x - a.x, 0f, q.z - a.z), (b - a).normalized) / Mathf.Max(0.01f, len);
                        if (t > 0.5f && along > 0.1f && along < 0.9f) mid.Add(sp);
                        trace.AppendLine($"{ln.name},{gait},{F(t)},{F(q.x)},{F(q.y)},{F(q.z)},{F(sp)}");
                        if (t > 0.5f && sp < 0.5f * setSpeed) { below += Time.deltaTime; if (below > 0.3f) stick += Time.deltaTime; }
                        else below = 0f;
                        if (stick > 3f) break;   // pinned: stop wasting time, the stick is recorded
                    }
                    Keys();
                    _rec.recording = false;
                    mid.Sort();
                    var byClass = new SortedDictionary<string, int>();
                    foreach (TownHitRecorder.Hit h in _rec.hits)
                    {
                        if (Vector3.Angle(h.normal, Vector3.up) < 30f) continue;   // standing on the ground is not a hit
                        string cls = h.c != null ? ColliderRule.ClassOf(h.c.name) : "?";
                        byClass[cls] = byClass.TryGetValue(cls, out int n) ? n + 1 : 1;
                        hitsCsv.AppendLine($"{ln.name},{gait},{F(h.t)},{cls},{(h.c != null ? h.c.name.Replace(",", " ") : "?")},{F(h.point.x)},{F(h.point.y)},{F(h.point.z)},{F(h.normal.x)},{F(h.normal.y)},{F(h.normal.z)}");
                    }
                    var cl = new StringBuilder();
                    int total = 0;
                    foreach (var kv in byClass) { cl.Append($"{kv.Key}:{kv.Value} "); total += kv.Value; }
                    string mmin = mid.Count > 0 ? F(mid[0]) : "", mmed = mid.Count > 0 ? F(mid[mid.Count / 2]) : "", mmax = mid.Count > 0 ? F(mid[mid.Count - 1]) : "";
                    csv.AppendLine($"{ln.name},{ln.target},{gait},{F(setSpeed)},{F(len)},{F(t)},{(arrived ? 1 : 0)},{mmin},{mmed},{mmax},{F(stick)},{total},{cl.ToString().Trim()}");
                    _log.AppendLine($"{ln.name} {gait}: {F(len)} m in {F(t)} s, arrived {arrived}, mid speed {mmin}/{mmed}/{mmax}, stick {F(stick)} s, side hits {total} {cl}");
                    yield return Settle(0.4f);
                }
            }
            File.WriteAllText(Path.Combine(outputDir, "lines.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "trace.csv"), trace.ToString());
            File.WriteAllText(Path.Combine(outputDir, "hits.csv"), hitsCsv.ToString());
        }
    }
}
#endif
