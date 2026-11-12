#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using CameraGame.Events;

namespace CameraGame.World
{
    /// <summary>
    /// Story 2.2 AC3 / Task 5.3: walk the real controller into the edges of the town — the shoreline at 6 points and the
    /// ring at 8 bearings — at walk and at sprint, holding W for 10 s after contact, then jump once at the barrier.
    /// Pass: inside the playable area on every frame, never standing over the water, never off the map. Each contact is
    /// photographed from the eye. FallRecovery (if present) is switched OFF here, so a barrier that leaks shows as a
    /// leak instead of being silently put back.
    /// </summary>
    public partial class TownRigRunner
    {
        partial void ExtraModes2(ref IEnumerator routine)
        {
            if (mode == "edges") routine = Edges();
            else ExtraModes3(ref routine);
        }

        partial void ExtraModes3(ref IEnumerator routine);

        private struct EdgeCase { public string name, kind; public Vector2 start, dir; }

        private IEnumerator Edges()
        {
            TownBoundsConfig cfg = _fall != null && _fall.Config != null ? _fall.Config
                                 : UnityEditor.AssetDatabase.LoadAssetAtPath<TownBoundsConfig>("Assets/Data/World/TownBoundsConfig.asset");
            if (cfg == null) { _log.AppendLine("RIG BROKEN: no TownBoundsConfig"); yield break; }
            if (_fall != null) _fall.enabled = false;
            if (_manager != null) { foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false); _manager.enabled = false; }
            Vector2[] poly = cfg.SafePlayableArea;
            var cases = new List<EdgeCase>();
            foreach (float x in new[] { -560f, -400f, -250f, -100f, 50f, 180f })
                cases.Add(new EdgeCase { name = $"shore_x{x:0}", kind = "shore", start = new Vector2(x, 245f), dir = Vector2.up });
            Vector2 c = new Vector2(-215f, -65f);
            for (int b = 0; b < 360; b += 45)
            {
                var dir = new Vector2(Mathf.Sin(b * Mathf.Deg2Rad), Mathf.Cos(b * Mathf.Deg2Rad));
                float t = 0f;
                while (t < 2000f && TownBoundsConfig.Contains(poly, c + dir * (t + 1f))) t += 1f;
                Vector2 edge = c + dir * t;
                cases.Add(new EdgeCase { name = $"ring_b{b:000}", kind = b == 0 || b == 45 || b == 315 ? "shore" : "ring", start = edge - dir * 30f, dir = dir });
            }
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            yield return DriveControl();
            var csv = new StringBuilder("case,kind,gait,start_x,start_z,end_x,end_y,end_z,inside_every_frame,max_beyond_edge_m,min_y,contact,first_contact_collider,stopped_by,jump_apex_m,after_jump_inside\n");
            var trace = new StringBuilder("case,gait,t,x,y,z,inside\n");
            Directory.CreateDirectory(Path.Combine(outputDir, "photos"));
            foreach (string gait in new[] { "walk", "sprint" })
                foreach (EdgeCase ec in cases)
                {
                    float yaw = Mathf.Atan2(ec.dir.x, ec.dir.y) * Mathf.Rad2Deg;
                    Place(new Vector3(ec.start.x, 0f, ec.start.y), yaw);
                    Keys();
                    yield return Settle(0.6f);
                    _rec.hits.Clear(); _rec.recording = true;
                    if (gait == "walk") Keys(Key.W); else Keys(Key.W, Key.LeftShift);
                    bool inside = true, contact = false; float beyond = 0f, minY = float.MaxValue, t = 0f, held = 0f;
                    string firstContact = "", stoppedBy = "";
                    Vector3 prev = _player.transform.position; float still = 0f;
                    while (t < 40f)
                    {
                        _player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);   // the mouse's job
                        yield return null;
                        t += Time.deltaTime;
                        Vector3 p = _player.transform.position;
                        bool ins = cfg.IsAllowed(new Vector2(p.x, p.z));
                        if (!ins) { inside = false; beyond = Mathf.Max(beyond, DistanceOutside(poly, new Vector2(p.x, p.z))); }
                        minY = Mathf.Min(minY, p.y);
                        if (Mathf.FloorToInt(t * 4f) != Mathf.FloorToInt((t - Time.deltaTime) * 4f))
                            trace.AppendLine($"{ec.name},{gait},{F(t)},{F(p.x)},{F(p.y)},{F(p.z)},{(ins ? 1 : 0)}");
                        float sp = Hz(p, prev) / Mathf.Max(1e-5f, Time.deltaTime); prev = p;
                        if (!contact)
                        {
                            foreach (TownHitRecorder.Hit h in _rec.hits)
                                if (h.c != null && Vector3.Angle(h.normal, Vector3.up) > 50f) { contact = true; firstContact = h.c.name; break; }
                            if (!contact && sp < 0.2f) { still += Time.deltaTime; if (still > 1f) { contact = true; firstContact = "(stopped, no side hit: terrain slope)"; } }
                            else if (sp >= 0.2f) still = 0f;
                        }
                        else
                        {
                            held += Time.deltaTime;
                            if (held >= 10f) break;
                        }
                    }
                    stoppedBy = firstContact.Contains("Edge") ? "TownBounds barrier" : firstContact;
                    if (gait == "walk")
                    {
                        yield return new WaitForEndOfFrame();
                        RenderTo(_cam, Path.Combine(outputDir, "photos", $"{ec.name}.jpg"));
                    }
                    // one jump at the barrier, W still held
                    float y0 = _player.transform.position.y, apex = y0;
                    if (gait == "walk") Keys(Key.W, Key.Space); else Keys(Key.W, Key.LeftShift, Key.Space);
                    yield return null;
                    if (gait == "walk") Keys(Key.W); else Keys(Key.W, Key.LeftShift);
                    float tj = 0f; bool insideJump = true;
                    while (tj < 2.5f)
                    {
                        _player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        yield return null; tj += Time.deltaTime;
                        Vector3 p = _player.transform.position; apex = Mathf.Max(apex, p.y);
                        if (!cfg.IsAllowed(new Vector2(p.x, p.z))) { insideJump = false; beyond = Mathf.Max(beyond, DistanceOutside(poly, new Vector2(p.x, p.z))); }
                    }
                    Keys();
                    _rec.recording = false;
                    Vector3 e = _player.transform.position;
                    csv.AppendLine($"{ec.name},{ec.kind},{gait},{F(ec.start.x)},{F(ec.start.y)},{F(e.x)},{F(e.y)},{F(e.z)},{(inside ? 1 : 0)},{F(beyond)},{F(minY)},{(contact ? 1 : 0)},{firstContact.Replace(",", " ")},{stoppedBy.Replace(",", " ")},{F(apex - y0)},{(insideJump ? 1 : 0)}");
                    _log.AppendLine($"{ec.name} {gait}: end {V(e)}, inside {inside}, beyond {F(beyond)} m, stopped by {stoppedBy}, jump apex {F(apex - y0)} m, inside after jump {insideJump}");
                }
            File.WriteAllText(Path.Combine(outputDir, "edges.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "trace.csv"), trace.ToString());
        }

        /// <summary>Distance from a point outside the polygon to its nearest edge (0 inside).</summary>
        private static float DistanceOutside(Vector2[] poly, Vector2 p)
        {
            if (TownBoundsConfig.Contains(poly, p)) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
#endif
