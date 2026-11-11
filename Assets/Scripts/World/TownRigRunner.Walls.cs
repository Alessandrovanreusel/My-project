#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using CameraGame.Events;

namespace CameraGame.World
{
    /// <summary>
    /// Story 2.2 AC4 / 1f: pressed against a wall, does the view ever show what is behind it?
    ///
    /// Per wall: a pure green, unlit marker box fills the space just BEHIND the wall (its far face found by a reverse
    /// ray). The player walks into the wall through the real input at 0°, ±30° and ±60° to its normal until the
    /// controller is in contact, then looks at it at pitch −80 (up), −45, 0, +45, +80 (down) through queued Mouse
    /// delta events (OnLook → _pitch), in Walk (FOV 60), raised (FOV 60) and raised + zoomed (FOV 18). Each pose is
    /// rendered from the REAL camera pose (after ThirdPersonCamera.LateUpdate) and its magenta pixels counted:
    /// the pass is 0. Positive control per pose: the same render with the wall's renderers switched off must show
    /// the marker. Geometric check: linecasts from the body axis at eye height to the near plane's centre and corners.
    /// </summary>
    public partial class TownRigRunner
    {
        [System.Serializable] public class WallSpec { public string name; public float[] stand; public float[] aim; }
        [System.Serializable] public class WallList { public WallSpec[] walls; }

        partial void ExtraModes(ref IEnumerator routine)
        {
            if (mode == "walls") routine = Walls();
            else ExtraModes2(ref routine);
        }

        partial void ExtraModes2(ref IEnumerator routine);

        private static readonly float[] Pitches = { -80f, -45f, 0f, 45f, 80f };
        private static readonly float[] Angles = { 0f, 30f, -30f, 60f, -60f };

        private IEnumerator Walls()
        {
            string json = ReadData("walls/walls.json");
            if (json == null) { _log.AppendLine("RIG BROKEN: no walls/walls.json"); yield break; }
            WallSpec[] walls = JsonUtility.FromJson<WallList>(json).walls;
            if (_manager != null) foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false);
            if (_manager != null) _manager.enabled = false;
            if (_fall != null) _fall.enabled = false;
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            Place(new Vector3(60f, 0f, -2f), 270f);
            yield return Settle(1f);
            yield return DriveControl();
            yield return PitchControl();
            // 7.3: the open-space pose — on the slab, nothing within metres: the eye must be 1.650 m up and 0.316 m
            // forward of the feet at every pitch (the nudge follows the yaw only since Story 2.2).
            foreach (float op in new[] { -80f, 0f, 80f })
            {
                yield return SetPitch(op);
                yield return new WaitForEndOfFrame();
                Vector3 feet = _player.transform.position, cam = _cam.transform.position;
                Vector3 fwd = Quaternion.Euler(0f, _player.transform.eulerAngles.y, 0f) * Vector3.forward;
                Vector3 d = cam - feet;
                _log.AppendLine($"OPEN POSE pitch {F(op)} (read {F(_tpc.CameraPitch)}): up {F(d.y, "0.0000")} m, forward {F(Vector3.Dot(new Vector3(d.x, 0f, d.z), fwd), "0.0000")} m, sideways {F(Vector3.Cross(fwd, new Vector3(d.x, 0f, d.z)).y, "0.0000")} m, near clip {F(_cam.nearClipPlane, "0.000")}");
            }
            yield return SetPitch(0f);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.green };   // pure green: the supermarket is pink, so a magenta marker false-fired on its highlights
            var csv = new StringBuilder("wall,angle,mode,pitch,fov,marker_px,control_px,geo_hits,near_hit_names,cam_x,cam_y,cam_z,body_to_wall_m,in_contact,valid,see_through\n");
            var perWall = new StringBuilder("wall,valid_poses,see_through_poses,geo_hit_poses,max_control_px,max_marker_px\n");
            var worst = new List<KeyValuePair<int, string>>();
            Directory.CreateDirectory(Path.Combine(outputDir, "frames"));
            foreach (WallSpec w in walls)
            {
                Vector3 stand = new Vector3(w.stand[0], 0f, w.stand[1]);
                float gy = GroundY(stand);
                Vector3 aim = new Vector3(w.aim[0], (float.IsNaN(gy) ? 0f : gy) + (w.aim.Length > 2 ? w.aim[2] : 1.2f), w.aim.Length > 3 ? w.aim[3] : w.aim[1]);
                if (w.aim.Length == 2) aim = new Vector3(w.aim[0], (float.IsNaN(gy) ? 0f : gy) + 1.2f, w.aim[1]);
                Vector3 from = new Vector3(stand.x, aim.y, stand.z);
                if (!FirstWall(from, aim - from, 30f, out RaycastHit hit))
                { _log.AppendLine($"{w.name}: NO WALL found from {V(from)} toward {V(aim)}"); continue; }
                Vector3 n = hit.normal; n.y = 0f;
                if (n.sqrMagnitude < 0.01f) { _log.AppendLine($"{w.name}: hit {hit.collider.name} is not a wall (normal {V(hit.normal)})"); continue; }
                n.Normalize();
                Collider wallCol = hit.collider;
                Renderer[] wallRenderers = wallCol.GetComponentsInParent<Renderer>(true);
                if (wallRenderers.Length == 0) wallRenderers = wallCol.GetComponentsInChildren<Renderer>(true);
                // the wall's far face: a reverse ray from 6 m behind toward the front surface, back faces on
                float depth = 0.3f;
                bool prevBack = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                if (wallCol.Raycast(new Ray(hit.point - n * 6f, n), out RaycastHit back, 6f)) depth = Mathf.Clamp(6f - back.distance, 0.02f, 3f);
                Physics.queriesHitBackfaces = prevBack;
                // The marker must hide entirely behind the wall's own silhouette, or it shows round the edges of a
                // narrow wall and reads as a see-through that is not there: measure the wall's extent in its plane.
                Vector3 right = Vector3.Cross(Vector3.up, n).normalized;
                float l0 = Extent(wallCol, hit.point, n, -right, 8f), r0 = Extent(wallCol, hit.point, n, right, 8f);
                float d0 = Extent(wallCol, hit.point, n, Vector3.down, 4f), u0 = Extent(wallCol, hit.point, n, Vector3.up, 8f);
                float mw = l0 + r0 - 0.4f, mh = d0 + u0 - 0.4f;
                GameObject marker = null;
                if (mw > 0.05f && mh > 0.05f)
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name = "WallMarker (rig)";
                    Destroy(marker.GetComponent<Collider>());
                    marker.GetComponent<Renderer>().sharedMaterial = mat;
                    marker.transform.rotation = Quaternion.LookRotation(-n);
                    Vector3 centre = hit.point + right * ((r0 - l0) * 0.5f) + Vector3.up * ((u0 - d0) * 0.5f);
                    marker.transform.position = centre - n * (depth + 0.05f + 0.5f);
                    marker.transform.localScale = new Vector3(mw, mh, 1f);
                }
                _log.AppendLine($"{w.name}: wall {wallCol.name} at {V(hit.point)} normal {V(n)} depth {F(depth)} m; extent L{F(l0)} R{F(r0)} D{F(d0)} U{F(u0)} → marker " +
                                (marker != null ? $"{F(mw)} x {F(mh)} m" : "NONE (too narrow: geometric check only)") + $"; {wallRenderers.Length} renderer(s)");

                int nValid = 0, nThrough = 0, nGeo = 0, maxCtrl = 0, maxPx = 0;
                foreach (float ang in Angles)
                {
                    Vector3 heading = Quaternion.Euler(0f, ang, 0f) * -n;      // walking direction into the wall
                    Vector3 start = hit.point - heading * 3.5f;
                    start.y = 0f;
                    float yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
                    Place(start, yaw, hit.point.y + 4f);
                    yield return Settle(0.4f);
                    _rec.hits.Clear(); _rec.recording = true;
                    Keys(Key.W);
                    float t = 0f, still = 0f; Vector3 prev = _player.transform.position; bool contact = false;
                    while (t < 6f)
                    {
                        _player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        yield return null;
                        t += Time.deltaTime;
                        Vector3 q = _player.transform.position;
                        float sp = Hz(q, prev) / Mathf.Max(1e-5f, Time.deltaTime); prev = q;
                        foreach (TownHitRecorder.Hit h in _rec.hits) if (h.c == wallCol) { contact = true; break; }
                        if (contact) { still += Time.deltaTime; if (Mathf.Abs(ang) > 1f ? still > 0.3f : (sp < 0.05f && still > 0.3f)) break; }
                    }
                    Keys();
                    _rec.recording = false;
                    yield return Settle(0.2f);
                    float bodyToWall = Mathf.Abs(Vector3.Dot(_player.transform.position - hit.point, n));
                    foreach (string m in new[] { "walk60", "raised60", "zoom18" })
                    {
                        if (m == "raised60") { _photo.SetPhotoMode(true); _photo.SetZoom(0f); yield return Settle(0.6f); }
                        if (m == "zoom18") { _photo.SetZoom(1f); yield return Settle(1.0f); }
                        foreach (float pitch in Pitches)
                        {
                            yield return SetPitch(pitch);
                            yield return new WaitForEndOfFrame();
                            string tag = $"{w.name}_a{ang:+0;-0;0}_{m}_p{pitch:+0;-0;0}";
                            int px = MarkerPixels(_cam, null, out _);
                            foreach (Renderer r in wallRenderers) r.enabled = false;
                            int ctrl = MarkerPixels(_cam, null, out _);
                            foreach (Renderer r in wallRenderers) r.enabled = true;
                            int geo = NearPlaneHits(out string names);
                            // A pose tests the wall only if the body touched it and the wall hides at least part of the
                            // marker (control > marker). Marker == control means the camera sees the marker directly,
                            // round the wall's side: a rig pose, not a see-through.
                            bool valid = contact && ctrl > px;
                            bool through = valid && px > 0;
                            if (valid) nValid++;
                            if (through) nThrough++;
                            if (geo > 0) nGeo++;
                            maxCtrl = Mathf.Max(maxCtrl, ctrl); maxPx = Mathf.Max(maxPx, valid ? px : 0);
                            if (through || geo > 0) { MarkerPixels(_cam, Path.Combine(outputDir, "frames", tag + ".jpg"), out _); worst.Add(new KeyValuePair<int, string>(px, tag + (geo > 0 ? " [geo " + geo + "]" : ""))); }
                            Vector3 cp = _cam.transform.position;
                            csv.AppendLine($"{w.name},{F(ang)},{m},{F(pitch)},{F(_cam.fieldOfView, "0.0")},{px},{ctrl},{geo},{names},{F(cp.x)},{F(cp.y)},{F(cp.z)},{F(bodyToWall)},{(contact ? 1 : 0)},{(valid ? 1 : 0)},{(through ? 1 : 0)}");
                        }
                    }
                    _photo.SetZoom(0f);
                    _photo.SetPhotoMode(false);
                    yield return SetPitch(0f);
                    yield return Settle(0.4f);
                    // one reference frame per wall and angle, walk mode, pitch 0, with the control beside it
                    if (ang == 0f)
                    {
                        yield return new WaitForEndOfFrame();
                        MarkerPixels(_cam, Path.Combine(outputDir, $"ref_{w.name}.jpg"), out _);
                        foreach (Renderer r in wallRenderers) r.enabled = false;
                        MarkerPixels(_cam, Path.Combine(outputDir, $"ref_{w.name}_control.jpg"), out _);
                        foreach (Renderer r in wallRenderers) r.enabled = true;
                    }
                }
                if (marker != null) Destroy(marker);
                perWall.AppendLine($"{w.name},{nValid},{nThrough},{nGeo},{maxCtrl},{maxPx}");
            }
            File.WriteAllText(Path.Combine(outputDir, "per-wall.csv"), perWall.ToString());
            _log.Append(perWall);
            File.WriteAllText(Path.Combine(outputDir, "walls.csv"), csv.ToString());
            worst.Sort((a, b) => b.Key.CompareTo(a.Key));
            var ws = new StringBuilder("worst poses (marker pixels, tag):\n");
            for (int i = 0; i < Mathf.Min(40, worst.Count); i++) ws.AppendLine($"  {worst[i].Key}  {worst[i].Value}");
            File.WriteAllText(Path.Combine(outputDir, "worst.txt"), ws.ToString());
            _log.Append(ws);
        }

        /// <summary>How far the wall's front face continues from <paramref name="p"/> along <paramref name="dir"/>
        /// (rays fired into the wall every 0.1 m until one misses it).</summary>
        private static float Extent(Collider wall, Vector3 p, Vector3 n, Vector3 dir, float max)
        {
            float s = 0f;
            for (float d = 0.1f; d <= max; d += 0.1f)
            {
                if (!wall.Raycast(new Ray(p + dir * d + n * 0.5f, -n), out RaycastHit h, 1.2f)) break;
                if (Vector3.Dot(h.normal, n) < 0.7f) break;   // round a corner: no longer the same face
                s = d;
            }
            return s;
        }

        /// <summary>First collider along a ray that is not the player, an actor, a trigger or a rig marker.</summary>
        private bool FirstWall(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(from, dir.normalized, _hitBuf, dist, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
                if (!IsMine(_hitBuf[i].collider) && _hitBuf[i].distance < bd) { bd = _hitBuf[i].distance; best = _hitBuf[i]; }
            return bd < float.MaxValue;
        }

        /// <summary>The real pitch path: a Mouse delta event → PlayerInput → OnLook → _pitch (sensitivity 0.15°/unit).</summary>
        private IEnumerator SetPitch(float target)
        {
            if (Mouse.current == null || _tpc == null) yield break;
            float dy = (_tpc.CameraPitch - target) / 0.15f;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { delta = new Vector2(0f, dy) });
            yield return null;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { delta = Vector2.zero });
            yield return null;
            yield return null;
        }

        /// <summary>Control: N units of mouse delta move the pitch by N × 0.15°.</summary>
        private IEnumerator PitchControl()
        {
            yield return SetPitch(0f);
            float p0 = _tpc.CameraPitch;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { delta = new Vector2(0f, 100f) });
            yield return null;
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { delta = Vector2.zero });
            yield return null; yield return null;
            float p1 = _tpc.CameraPitch;
            _log.AppendLine($"PITCH CONTROL: +100 units of mouse delta y moved the pitch {F(p0)} → {F(p1)} ({F(p1 - p0)}°, expected −15°: up is negative)");
            yield return SetPitch(0f);
        }

        /// <summary>Renders the camera and counts the marker's magenta pixels.</summary>
        private static int MarkerPixels(Camera c, string savePath, out float std)
        {
            RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevT = c.targetTexture;
            c.targetTexture = rt;
            c.Render();
            c.targetTexture = prevT;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = tex.GetPixels32();
            int n = 0;
            foreach (Color32 p in px) if (p.g > 200 && p.r < 50 && p.b < 50) n++;   // the unlit green marker; nothing in the town is pure green
            std = LumaStd(tex);
            if (savePath != null) File.WriteAllBytes(savePath, tex.EncodeToJPG(85));
            Destroy(tex);
            return n;
        }

        /// <summary>Linecasts from the body axis at eye height to the near plane's centre and four corners.</summary>
        private int NearPlaneHits(out string names)
        {
            float eye = _tpc.Config != null ? _tpc.Config.SafeEyeHeight : 1.65f;
            Vector3 axis = _player.transform.position + Vector3.up * eye;
            Transform ct = _cam.transform;
            float nz = _cam.nearClipPlane;
            float hh = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * nz, hw = hh * _cam.aspect;
            Vector3 c = ct.position + ct.forward * nz;
            Vector3[] pts = { c, c + ct.up * hh + ct.right * hw, c + ct.up * hh - ct.right * hw, c - ct.up * hh + ct.right * hw, c - ct.up * hh - ct.right * hw };
            int hits = 0; var sb = new StringBuilder();
            bool prevBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            foreach (Vector3 p in pts)
            {
                int n = Physics.RaycastNonAlloc(axis, (p - axis).normalized, _hitBuf, Vector3.Distance(axis, p), ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (!IsMine(_hitBuf[i].collider)) { hits++; sb.Append(_hitBuf[i].collider.name.Replace(",", " ").Replace(" ", "_")).Append(';'); break; }
            }
            Physics.queriesHitBackfaces = prevBack;
            names = sb.ToString();
            return hits;
        }
    }
}
#endif
