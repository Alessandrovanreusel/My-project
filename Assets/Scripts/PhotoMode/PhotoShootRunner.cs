#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CameraGame.Events;
using CameraGame.Grading;

namespace CameraGame.PhotoMode
{
    /// <summary>
    /// Walks the camera through a set of vantage points in a private world, pulls the REAL shutter at each
    /// one, and saves what the camera saw. Driven by the editor tool <c>PhotoShootRig</c>
    /// (Tools > Grading > Photo Shoot).
    ///
    /// This is not a test. It asserts nothing and cannot pass or fail — it produces photographs to be
    /// looked at. It runs the real path end to end: real EventManager, real pooled EventActor with its
    /// Animator and NavMeshAgent live, real <see cref="PhotoModeController.Capture"/>, real ShotGrader.
    /// Nothing is re-implemented for the rig, so what the pictures show is what the game does.
    ///
    /// ⚠️ WHY THIS LIVES IN THE RUNTIME ASSEMBLY, WRAPPED IN #if UNITY_EDITOR:
    /// it was first written in the Editor assembly, which seemed tidier. Unity cannot resolve an
    /// editor-assembly MonoBehaviour attached to a scene object when entering play mode — the whole
    /// component came back as "The referenced script (Unknown) on this Behaviour is missing!" and the rig
    /// silently did nothing while the scene ran on around it. The #if keeps it out of builds just as
    /// effectively, and it actually runs.
    /// </summary>
    public class PhotoShootRunner : MonoBehaviour
    {
        [HideInInspector] public PhotoModeController photo;
        [HideInInspector] public EventManager manager;
        [HideInInspector] public Camera cam;
        [HideInInspector] public CameraConfig cameraConfig;
        [HideInInspector] public string outputDir = "Temp/PhotoShoot";

        private const int ShotWidth = 960;
        private const int ShotHeight = 540;

        /// <summary>One vantage point. Note there is no "expected" field — the photograph is the result.</summary>
        private readonly struct Pose
        {
            public readonly string Name;
            public readonly float Distance;    // in subject heights
            public readonly float YawOffset;   // degrees off the line to the subject; 180 = looking away
            public readonly bool FromBehind;
            public readonly bool Zoom;
            public readonly bool Wall;
            public readonly string Intent;

            public Pose(string name, float distance, string intent, float yaw = 0f,
                        bool zoom = false, bool wall = false, bool fromBehind = false)
            {
                Name = name; Distance = distance; Intent = intent;
                YawOffset = yaw; Zoom = zoom; Wall = wall; FromBehind = fromBehind;
            }
        }

        private static readonly Pose[] Poses =
        {
            new Pose("a_close",        1.3f, "Standing right in front of him."),
            new Pose("b_mid",          2.5f, "A few paces back."),
            new Pose("c_far",          6f,   "Across the street."),
            new Pose("d_very_far",     14f,  "Far down the road."),
            new Pose("e_far_zoomed",   6f,   "Same spot as c_far, zoomed all the way in.", zoom: true),
            // Close enough to clear the coverage gate, so the shot actually reaches the occlusion test.
            // At 2 heights the animated subject only measures ~5%, so it was being rejected as TooSmall
            // and the occlusion check never ran at all.
            new Pose("f_behind_wall",  1.2f, "A wall dropped between the camera and him.", wall: true),
            new Pose("g_looking_away", 2f,   "Close, but facing the opposite way.", yaw: 180f),
            new Pose("h_off_to_side",  2f,   "Close, but he is off past the edge of the frame.", yaw: 55f),
            new Pose("i_from_behind",  2f,   "Photographing him from behind.", fromBehind: true),

            // Added by the 2026-07-26 code review. Nothing here previously came closer than 1.2 subject
            // heights (~11 units against a ~2.2-unit box half-depth), so NO pose ever put an AABB corner
            // behind the near plane — the near-plane clipping branch and the BehindCamera early-out had no
            // coverage at all in this rig once the T-pose bench was deleted.
            new Pose("k_straddling",   0.12f, "Nose-to-nose — his box straddles the lens."),
            // The bug the review found: an AABB that ENCLOSES the camera is outside no frustum plane, so a
            // photo taken facing away used to score 100% and five stars. Must now read BehindCamera.
            new Pose("l_inside_away",  0.05f, "Standing inside him, facing the other way.", yaw: 180f),
        };

        private readonly StringBuilder _log = new StringBuilder();
        private GameObject _wall;

        // Bound for the whole run so cam.pixelWidth/Height match the saved image exactly — see Save().
        private RenderTexture _rt;
        private bool _prevRunInBackground;
        private RenderTexture _prevTarget;

        private IEnumerator Start()
        {
            // Record before mutating: a rig must put back everything it changes, even a play-session value.
            _prevRunInBackground = Application.runInBackground;
            Application.runInBackground = true;

            try
            {
                yield return RunShoot();
            }
            finally
            {
                Restore();
                Finish();
            }
        }

        private IEnumerator RunShoot()
        {
            Directory.CreateDirectory(outputDir);

            // Bind the render target BEFORE anything is graded. ShotGrader measures coverage against
            // cam.pixelWidth/Height; if the target is bound only inside Save(), the grader measures the Game
            // View while the picture is a fixed 960x540, so the two are different projections with different
            // horizontal FOV (vertical FOV is what Unity preserves). The old code tried to paper over that by
            // rescaling the rect by 960/Screen.width, which is wrong by the aspect ratio and drifts further
            // from centre — putting the drawn box off the subject in the one artefact whose entire job is to
            // answer "is the box on him?". Binding it up front makes the graded frame and the saved image the
            // same pixel space, and the rect needs no scaling at all.
            _prevTarget = cam.targetTexture;
            _rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = _rt;

            _log.AppendLine("PHOTO SHOOT — real shutter, real grader, real actor.");
            _log.AppendLine("No expected values: these are photographs to be looked at.");
            _log.AppendLine($"Graded and saved at {ShotWidth}x{ShotHeight} (one pixel space, no rescaling).");
            _log.AppendLine();

            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "Wall";
            _wall.SetActive(false);

            // The empty-world shot goes FIRST, while the manager is held off. Waiting for an empty world
            // later never works: the manager respawns half a second after each despawn, so the wait just
            // burns a minute watching him walk.
            yield return ShootEmptyWorld();

            manager.enabled = true;

            float t = 0f;
            while (manager.ActiveActors.Count == 0 && t < 15f) { t += Time.deltaTime; yield return null; }
            if (manager.ActiveActors.Count == 0)
            {
                _log.AppendLine("No actor ever spawned — nothing else to photograph.");
                yield break;
            }

            foreach (Pose p in Poses) yield return Shoot(p);
        }

        /// <summary>Puts back everything the rig changed. Runs from Start's finally, so a throw mid-shoot
        /// cannot leave the camera rendering into a dead target or the editor on borrowed settings.</summary>
        private void Restore()
        {
            if (cam != null) cam.targetTexture = _prevTarget;

            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }

            Application.runInBackground = _prevRunInBackground;
        }

        private IEnumerator ShootEmptyWorld()
        {
            manager.enabled = false;      // hold off spawning so the street is genuinely empty
            _wall.SetActive(false);

            cam.transform.position = new Vector3(0f, 8f, 30f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            photo.SetPhotoMode(true);
            photo.SetZoom(0f);
            yield return null;
            yield return null;

            int actors = manager.ActiveActors.Count;
            photo.Capture();
            yield return new WaitForEndOfFrame();

            GradeDetail d = photo.LastDetail;
            Save(Path.Combine(outputDir, "j_no_subject.png"), d);

            _log.AppendLine("j_no_subject  —  Shutter pressed with nobody in the world at all.");
            _log.AppendLine($"    VERDICT: {(d.Miss == GradeMiss.None ? "COUNTED (!)" : $"rejected — {d.Miss}")}");
            _log.AppendLine($"    live actors at shutter: {actors}");
            _log.AppendLine();

            yield return new WaitForSecondsRealtime(1.8f);
        }

        private IEnumerator Shoot(Pose p)
        {
            if (manager.ActiveActors.Count == 0)
            {
                float w = 0f;
                while (manager.ActiveActors.Count == 0 && w < 40f) { w += Time.deltaTime; yield return null; }
                if (manager.ActiveActors.Count == 0) yield break;
            }

            EventActor actor = manager.ActiveActors[0];
            Bounds b = actor.Bounds;
            float height = Mathf.Max(0.01f, b.size.y);

            Vector3 dir = p.FromBehind ? Vector3.back : Vector3.forward;
            Vector3 camPos = b.center + dir * (p.Distance * height);
            cam.transform.position = camPos;
            cam.transform.rotation = Quaternion.LookRotation(b.center - camPos, Vector3.up)
                                     * Quaternion.Euler(0f, p.YawOffset, 0f);

            _wall.SetActive(p.Wall);
            if (p.Wall)
            {
                _wall.transform.position = Vector3.Lerp(camPos, b.center, 0.45f);
                _wall.transform.localScale = new Vector3(height * 5f, height * 5f, 0.4f);
                _wall.transform.rotation = Quaternion.LookRotation(b.center - camPos, Vector3.up);
            }

            Physics.SyncTransforms();

            // Raise FIRST: SetMode resets zoom when cameraConfig.resetZoomOnRaise is on, so zooming before
            // raising would be thrown away.
            photo.SetPhotoMode(true);
            yield return null;
            photo.SetZoom(p.Zoom ? 1f : 0f);

            // Wait for the FOV to arrive. Update EASES toward the zoom target, so shooting two frames after
            // asking for full zoom catches it mid-travel — an earlier run recorded 22° for a shot meant to
            // be at 18°, quietly testing something other than full zoom.
            float target = cameraConfig != null
                ? (p.Zoom ? cameraConfig.teleFov : cameraConfig.wideFov)
                : 60f;
            float waited = 0f;
            while (Mathf.Abs(cam.fieldOfView - target) > 0.25f && waited < 4f)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            // Re-read the actor from the manager, not from the local variable captured before the yields
            // above. Actors are POOLED, and ActiveActors says so explicitly: a reference held across frames
            // does not go null when its event ends — it quietly becomes a different event, or an inactive
            // instance parked at its despawn point. The zoom-settle loop can wait up to 4 s and the inter-pose
            // hold is 1.8 s, which is ample for a lifecycle to end. Without this the rig aimed at a stale
            // pose, graded an empty world, and wrote "rejected — NoSubject" against a caption reading
            // "standing right in front of him" — a well-formed log line that looks exactly like a real bug.
            if (manager.ActiveActors.Count == 0)
            {
                _log.AppendLine($"{p.Name}  —  SKIPPED: he despawned mid-pose (pooled reuse), nothing to shoot.");
                _log.AppendLine();
                yield break;
            }

            actor = manager.ActiveActors[0];
            b = actor.Bounds;
            cam.transform.rotation = Quaternion.LookRotation(b.center - cam.transform.position, Vector3.up)
                                     * Quaternion.Euler(0f, p.YawOffset, 0f);

            photo.Capture();          // the real shutter
            yield return new WaitForEndOfFrame();

            ShotGrade grade = photo.LastGrade;
            GradeDetail detail = photo.LastDetail;
            Save(Path.Combine(outputDir, p.Name + ".png"), detail);

            _log.AppendLine($"{p.Name}  —  {p.Intent}");
            _log.AppendLine($"    VERDICT: {(detail.Miss == GradeMiss.None ? $"counted — {grade}" : $"rejected — {detail.Miss}")}");
            _log.AppendLine($"    coverage {detail.Coverage01:P2}  line-of-sight {detail.VisibleText}  " +
                            $"box {detail.ScreenRect.width:F0}x{detail.ScreenRect.height:F0}px");
            _log.AppendLine($"    distance {Vector3.Distance(cam.transform.position, b.center):F1} units " +
                            $"({p.Distance:F1} heights)  fov {cam.fieldOfView:F0}°  yaw {p.YawOffset:F0}°  wall {p.Wall}");
            _log.AppendLine();

            yield return new WaitForSecondsRealtime(1.8f);
        }

        /// <summary>
        /// Renders the camera to a texture and draws the grader's own box on it.
        ///
        /// NOT ScreenCapture.CaptureScreenshot: that grabs the Game View backbuffer, and while the editor
        /// runs unattended the Game View does not repaint its 3D content — an earlier run produced ten
        /// images of the readout floating on blank white. Rendering the camera directly needs no window.
        /// </summary>
        private void Save(string path, GradeDetail detail)
        {
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                // _rt is already bound to the camera (since RunShoot), so this renders the SAME projection
                // the grader measured — no rescaling, and the drawn box is in the image's own pixel space.
                cam.Render();
                RenderTexture.active = _rt;
                tex.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);

                Rect r = detail.ScreenRect;
                if (r.width > 0f && r.height > 0f)
                    DrawRect(tex, r, detail.Miss == GradeMiss.None ? Color.green : Color.red, 3);

                DrawCross(tex, ShotWidth / 2, ShotHeight / 2, 12, Color.white);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prevActive;
                Destroy(tex);
            }
        }

        private static void DrawRect(Texture2D tex, Rect r, Color c, int thickness)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(r.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.RoundToInt(r.xMax), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(r.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(r.yMax), 0, tex.height - 1);

            for (int t = 0; t < thickness; t++)
            {
                for (int x = x0; x <= x1; x++) { Px(tex, x, y0 + t, c); Px(tex, x, y1 - t, c); }
                for (int y = y0; y <= y1; y++) { Px(tex, x0 + t, y, c); Px(tex, x1 - t, y, c); }
            }
        }

        private static void DrawCross(Texture2D tex, int cx, int cy, int size, Color c)
        {
            for (int i = -size; i <= size; i++) { Px(tex, cx + i, cy, c); Px(tex, cx, cy + i, c); }
        }

        private static void Px(Texture2D tex, int x, int y, Color c)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height) tex.SetPixel(x, y, c);
        }

        /// <summary>
        /// Writes the log and leaves play mode. Called from Start's <c>finally</c>, so it runs even when a
        /// pose throws — previously an IOException or a destroyed reference lost shots.txt entirely, left
        /// partial PNGs behind, and kept play mode running indefinitely in the test world.
        ///
        /// ExitPlaymode is what triggers PhotoShootRig's scene restore, so without it the editor sat in the
        /// test world until a human pressed Stop — which for an unattended run means forever, and is exactly
        /// the "never leave the editor sitting in the test world" rule in CLAUDE.md.
        /// </summary>
        private void Finish()
        {
            _log.AppendLine("Shoot complete.");

            try
            {
                File.WriteAllText(Path.Combine(outputDir, "shots.txt"), _log.ToString());
            }
            catch (IOException e)
            {
                Debug.LogError($"[PhotoShoot] Could not write shots.txt: {e.Message}");
            }

            Debug.Log("[PhotoShoot] DONE\n" + _log);

            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }
}
#endif
