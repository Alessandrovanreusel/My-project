#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
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
        };

        private readonly StringBuilder _log = new StringBuilder();
        private GameObject _wall;

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            Directory.CreateDirectory(outputDir);

            _log.AppendLine("PHOTO SHOOT — real shutter, real grader, real actor.");
            _log.AppendLine("No expected values: these are photographs to be looked at.");
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
                Finish();
                yield break;
            }

            foreach (Pose p in Poses) yield return Shoot(p);

            Finish();
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

            // Re-read his bounds: he is walking, and the pose was computed a few frames ago.
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
            var rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;

            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, ShotWidth, ShotHeight), 0, 0);

                // The grader measured against cam.pixelWidth/Height — the Game View, since no target
                // texture was bound at capture time — so scale its rect into this image's pixel space.
                Rect r = detail.ScreenRect;
                if (r.width > 0f && r.height > 0f)
                {
                    float sx = ShotWidth / (float)Mathf.Max(1, Screen.width);
                    float sy = ShotHeight / (float)Mathf.Max(1, Screen.height);
                    DrawRect(tex, new Rect(r.x * sx, r.y * sy, r.width * sx, r.height * sy),
                             detail.Miss == GradeMiss.None ? Color.green : Color.red, 3);
                }

                DrawCross(tex, ShotWidth / 2, ShotHeight / 2, 12, Color.white);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                Destroy(rt);
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

        private void Finish()
        {
            _log.AppendLine("Shoot complete.");
            File.WriteAllText(Path.Combine(outputDir, "shots.txt"), _log.ToString());
            Debug.Log("[PhotoShoot] DONE\n" + _log);
        }
    }
}
#endif
