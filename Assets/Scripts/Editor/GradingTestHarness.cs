using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CameraGame.Events;
using CameraGame.Grading;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// A controlled test bench for the photo-grading gates (Story 1.9).
    ///
    /// WHY THIS EXISTS. Grading has no sound and no animation — there is nothing to check by ear or eye
    /// the way the cue (1.8) and the walk (1.7) could be checked. Verifying it by hand means walking to a
    /// spot, guessing a distance, pressing the shutter, and reading an overlay that has already gone stale
    /// by the time you screenshot it. That is slow, unrepeatable, and it has already produced one wrong
    /// conclusion.
    ///
    /// So instead: build a clean scene, put the subject and the camera at EXACT scripted positions, run the
    /// real <see cref="ShotGrader"/>, render what the camera sees, and draw the grader's own computed box
    /// onto that image. If the box does not sit on the subject, you can see it immediately — no play
    /// session, no timing, no ambiguity. Every run is byte-for-byte repeatable.
    ///
    /// Runs entirely in EDIT mode: no play, no coroutines, no domain reloads. Tools > Grading > Run Gate Tests.
    ///
    /// Output: Temp/GradingTests/ — one PNG per scenario plus report.txt. Temp/ is Unity-generated and
    /// git-ignored, so this never pollutes the project.
    /// </summary>
    public static class GradingTestHarness
    {
        private const string OutputDir = "Temp/GradingTests";
        private const string ConfigPath = "Assets/Data/Grading/GradingConfig.asset";
        private const string DrunkPrefabPath = "Assets/Prefabs/Events/EventActor_Drunk.prefab";

        private const int RenderWidth = 960;
        private const int RenderHeight = 540;
        private const float WideFov = 60f;      // matches CameraConfig.wideFov (1x)
        private const float TeleFov = 18f;      // matches CameraConfig.teleFov (4x, full zoom)

        /// <summary>One scripted camera/subject arrangement and what we expect the grader to say.</summary>
        private readonly struct Scenario
        {
            public readonly string Name;
            public readonly float DistanceInHeights;   // camera distance, in multiples of subject height
            public readonly bool FaceAway;             // point the camera 180° away from the subject
            public readonly bool Occlude;              // drop a wall between camera and subject
            public readonly float LateralOffsetHeights;// slide the camera sideways
            public readonly bool AimAtSubject;         // false = aim straight ahead, so an offset subject leaves the frame
            public readonly float Fov;                 // vertical FOV; WideFov = 1x, TeleFov = full zoom
            public readonly GradeMiss Expected;        // GradeMiss.None == expect a pass
            public readonly string Why;

            public Scenario(string name, float distance, GradeMiss expected, string why,
                            bool faceAway = false, bool occlude = false, float lateral = 0f,
                            bool aimAtSubject = true, float fov = WideFov)
            {
                Name = name; DistanceInHeights = distance; Expected = expected; Why = why;
                FaceAway = faceAway; Occlude = occlude; LateralOffsetHeights = lateral;
                AimAtSubject = aimAtSubject; Fov = fov;
            }
        }

        // Distances are in SUBJECT HEIGHTS, not metres — this world is ~4x metric and the subject is ~9
        // units tall, so absolute numbers would be meaningless to read. Measured from the subject's centre.
        private static readonly Scenario[] Scenarios =
        {
            new Scenario("01_very_close", 1.2f, GradeMiss.None,
                "Right in front of him — should pass with large coverage."),
            new Scenario("02_close",      2.0f, GradeMiss.None,
                "The intended 'good shot' range. NOTE 2.5 heights measures 7.64% — just under the gate — " +
                "so the 8% threshold demands framing tighter than ~2.4 body-heights. A tuning question."),
            new Scenario("03_medium",     6f,   GradeMiss.TooSmall,
                "Across the street — expected to fall under the 8% gate."),
            new Scenario("04_far",        15f,  GradeMiss.TooSmall,
                "Far away — clearly too small to count."),
            new Scenario("05_facing_away", 2.5f, GradeMiss.OutsideFrustum,
                "Standing next to him but pointed the other way — no subject in the photo.", faceAway: true),
            new Scenario("06_off_frame",  1.2f, GradeMiss.OutsideFrustum,
                "Close, but he is off to the side and the camera looks straight ahead — the 'no drunk on " +
                "camera' case. Aims forward, NOT at him: aiming at him would just re-centre him.",
                lateral: 4f, aimAtSubject: false),
            new Scenario("07_occluded",   1.2f, GradeMiss.Occluded,
                "A wall directly between camera and subject — in frame and large enough to clear the " +
                "coverage gate, so the occlusion test is actually reached.", occlude: true),
            new Scenario("08_straddling", 0.35f, GradeMiss.None,
                "Nose-to-nose: his bounds straddle the camera plane. Must give a SANE number, " +
                "not the enormous one an unclipped WorldToScreenPoint would produce."),

            // Zoom x grading. Nothing had tested this interaction, and it decides whether the game is
            // "walk up close" or "shoot from across the street" — the whole feel of a photography game.
            new Scenario("09_medium_zoomed", 6f, GradeMiss.None,
                "Same distance as 03_medium (which fails at 1.37% wide) but at full 4x zoom. If the gate " +
                "is passable here, telephoto is the intended tool for distance and the 8% threshold is fine.",
                fov: TeleFov),
            new Scenario("10_far_zoomed", 15f, GradeMiss.TooSmall,
                "Full zoom from genuinely far away — establishes where even telephoto stops counting.",
                fov: TeleFov),
        };

        [MenuItem("Tools/Grading/Run Gate Tests")]
        public static void Run()
        {
            var report = new StringBuilder();
            string previousScene = SceneManager.GetActiveScene().path;

            // Refuse to clobber unsaved work — this swaps the open scene out and back.
            if (SceneManager.GetActiveScene().isDirty)
            {
                EditorUtility.DisplayDialog("Grading Tests",
                    "The open scene has unsaved changes. Save it first — this harness swaps scenes.", "OK");
                return;
            }

            // Fail fast before touching the open scene if the inputs are missing at all.
            if (AssetDatabase.LoadAssetAtPath<GradingConfig>(ConfigPath) == null)
            {
                Debug.LogError($"[GradingTest] No GradingConfig at {ConfigPath}.");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DrunkPrefabPath) == null)
            {
                Debug.LogError($"[GradingTest] No subject prefab at {DrunkPrefabPath}.");
                return;
            }

            Directory.CreateDirectory(OutputDir);

            // A fresh single scene. Isolation matters: run this additively on top of SampleScene and its
            // 16,321 MeshColliders would block the occlusion linecasts and its terrain would fill the frame.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ⚠️ RELOAD AFTER THE SCENE SWAP, NOT BEFORE. Opening a scene in Single mode unloads unused
            // assets, which destroys the NATIVE half of any ScriptableObject loaded beforehand. The managed
            // C# object survives with its public fields intact — so `cfg.minCoverage` still reads 8% while
            // `cfg == null` is simultaneously TRUE, because Unity's overloaded == tests the native pointer.
            // The first run of this harness hit exactly that: a config that printed perfect values and made
            // every single scenario report NoConfig.
            var cfg = AssetDatabase.LoadAssetAtPath<GradingConfig>(ConfigPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DrunkPrefabPath);

            try
            {
                BuildScene(prefab, out Camera cam, out GameObject subjectRoot, out GameObject wall);

                var subject = new RendererSubject(subjectRoot.transform);
                Bounds b = subject.Bounds;
                float height = Mathf.Max(0.01f, b.size.y);

                report.AppendLine("GRADING GATE TESTS");
                report.AppendLine($"subject bounds  centre {b.center}  size {b.size}  (height {height:F2} units)");
                report.AppendLine($"config          minCoverage {cfg.minCoverage:P2}  samples {cfg.SafeOcclusionSamples}  " +
                                  $"minVisible {cfg.SafeMinVisibleSamples:P0}  occluderMask {cfg.occluderMask.value}");
                report.AppendLine($"render          {RenderWidth}x{RenderHeight} @ {WideFov}° vertical FOV");
                report.AppendLine();

                // ⚠️ Bind the render target for the WHOLE run, before any grading. ShotGrader measures
                // coverage against cam.pixelWidth/pixelHeight, which is the Game view size until a
                // targetTexture is assigned. Grading first and rendering second measured against a 979px
                // game view while writing a 960px PNG — so the drawn box lived in a slightly different
                // pixel space than the image under it. Same trap as reading a value before the thing that
                // defines it is in place.
                var rt = new RenderTexture(RenderWidth, RenderHeight, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;

                int passed = 0;
                foreach (Scenario s in Scenarios)
                {
                    PlaceScenario(s, cam, subject.Bounds, height, wall);

                    // Transforms were moved by script; physics needs telling before any linecast.
                    Physics.SyncTransforms();

                    ShotGrade grade = ShotGrader.Grade(cam, subject, cfg, out GradeDetail detail);

                    bool ok = detail.Miss == s.Expected;
                    if (ok) passed++;

                    string png = Path.Combine(OutputDir, s.Name + ".png");
                    CaptureWithOverlay(cam, rt, detail, png);

                    report.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {s.Name}  " +
                                      $"(distance {s.DistanceInHeights:F2} heights = {s.DistanceInHeights * height:F1} units, " +
                                      $"fov {s.Fov:F0}°)");
                    report.AppendLine($"    {s.Why}");
                    report.AppendLine($"    expected {s.Expected}  ->  got {detail.Miss}");
                    report.AppendLine($"    grade {grade}  coverage {detail.Coverage01:P2}  " +
                                      $"line-of-sight {detail.VisibleText}  box {detail.ScreenRect.width:F0}x{detail.ScreenRect.height:F0}px " +
                                      $"at ({detail.ScreenRect.x:F0},{detail.ScreenRect.y:F0})");
                    report.AppendLine();
                }

                report.AppendLine($"{passed}/{Scenarios.Length} scenarios matched expectations.");

                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            catch (Exception e)
            {
                report.AppendLine("EXCEPTION: " + e);
                Debug.LogError("[GradingTest] " + e);
            }
            finally
            {
                string reportPath = Path.Combine(OutputDir, "report.txt");
                File.WriteAllText(reportPath, report.ToString());

                // Always restore the scene the developer was working in.
                if (!string.IsNullOrEmpty(previousScene))
                    EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

                Debug.Log($"[GradingTest] Done. Report: {reportPath}\n{report}");
            }
        }

        // --- scene construction ------------------------------------------------------------------------

        private static void BuildScene(GameObject prefab, out Camera cam, out GameObject subjectRoot, out GameObject wall)
        {
            var camGo = new GameObject("TestCamera");
            cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = WideFov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 5000f;              // this world is ~4x metric; the default 1000 clips scenery
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.20f, 0.28f);   // flat, so the subject reads clearly

            var lightGo = new GameObject("KeyLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            lightGo.transform.rotation = Quaternion.Euler(45f, 160f, 0f);

            subjectRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            subjectRoot.transform.position = Vector3.zero;
            subjectRoot.transform.rotation = Quaternion.identity;

            // The occluder must be on a layer inside GradingConfig.occluderMask (Default), exactly like the
            // real world geometry. Disabled by default; only the occlusion scenario switches it on.
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Occluder";
            wall.layer = 0;
            wall.SetActive(false);
        }

        /// <summary>Positions the camera (and optionally the wall) for one scenario.</summary>
        private static void PlaceScenario(Scenario s, Camera cam, Bounds b, float height, GameObject wall)
        {
            Vector3 centre = b.center;
            float dist = s.DistanceInHeights * height;
            cam.fieldOfView = s.Fov;

            // Camera sits on +Z looking back toward the subject, at his mid-height.
            Vector3 camPos = centre + new Vector3(s.LateralOffsetHeights * height, 0f, dist);
            cam.transform.position = camPos;

            if (s.FaceAway)
                cam.transform.rotation = Quaternion.LookRotation(camPos - centre, Vector3.up);  // 180° away
            else if (s.AimAtSubject)
                cam.transform.rotation = Quaternion.LookRotation(centre - camPos, Vector3.up);
            else
                // Straight down -Z regardless of where the subject is. Without this, a lateral offset
                // combined with LookRotation(centre - camPos) just re-centres him and tests nothing.
                cam.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

            wall.SetActive(s.Occlude);
            if (s.Occlude)
            {
                // Broad slab halfway between, big enough to cover the subject completely.
                wall.transform.position = Vector3.Lerp(camPos, centre, 0.5f);
                wall.transform.rotation = Quaternion.identity;
                wall.transform.localScale = new Vector3(height * 4f, height * 4f, 0.5f);
            }
        }

        // --- capture -----------------------------------------------------------------------------------

        /// <summary>
        /// Renders the camera to a PNG and draws the grader's own screen rect on top, so the image answers
        /// "did it box the right thing?" without any interpretation.
        /// </summary>
        private static void CaptureWithOverlay(Camera cam, RenderTexture rt, GradeDetail detail, string path)
        {
            RenderTexture previousActive = RenderTexture.active;

            var tex = new Texture2D(RenderWidth, RenderHeight, TextureFormat.RGB24, false);
            try
            {
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, RenderWidth, RenderHeight), 0, 0);

                // ReadPixels and the grader's rect share the same origin (bottom-left, y up), so the rect
                // maps straight onto the texture with no flip.
                if (detail.ScreenRect.width > 0f && detail.ScreenRect.height > 0f)
                {
                    Color c = detail.Miss == GradeMiss.None ? Color.green : Color.red;
                    DrawRectOutline(tex, detail.ScreenRect, c, 3);
                }

                // Crosshair at frame centre — makes it obvious where the camera was actually aimed.
                DrawCross(tex, RenderWidth / 2, RenderHeight / 2, 12, Color.white);

                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static void DrawRectOutline(Texture2D tex, Rect r, Color c, int thickness)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(r.xMin), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.RoundToInt(r.xMax), 0, tex.width - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(r.yMin), 0, tex.height - 1);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(r.yMax), 0, tex.height - 1);

            for (int t = 0; t < thickness; t++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    SetPixelSafe(tex, x, y0 + t, c);
                    SetPixelSafe(tex, x, y1 - t, c);
                }
                for (int y = y0; y <= y1; y++)
                {
                    SetPixelSafe(tex, x0 + t, y, c);
                    SetPixelSafe(tex, x1 - t, y, c);
                }
            }
        }

        private static void DrawCross(Texture2D tex, int cx, int cy, int size, Color c)
        {
            for (int i = -size; i <= size; i++)
            {
                SetPixelSafe(tex, cx + i, cy, c);
                SetPixelSafe(tex, cx, cy + i, c);
            }
        }

        private static void SetPixelSafe(Texture2D tex, int x, int y, Color c)
        {
            if (x >= 0 && x < tex.width && y >= 0 && y < tex.height) tex.SetPixel(x, y, c);
        }

        /// <summary>
        /// A minimal <see cref="ISubject"/> over a plain transform hierarchy, so the harness can grade the
        /// real drunk geometry without running <c>EventActor</c>'s lifecycle. (EventActor caches its
        /// renderers in Awake, and Awake does not run in edit mode — its Bounds would come back as a
        /// zero-size point.) Mirrors EventActor.Bounds exactly: encapsulate every child renderer.
        /// </summary>
        private sealed class RendererSubject : ISubject
        {
            private readonly Transform _root;
            public RendererSubject(Transform root) => _root = root;

            public Bounds Bounds
            {
                get
                {
                    Renderer[] rs = _root.GetComponentsInChildren<Renderer>(true);
                    bool started = false;
                    Bounds b = default;
                    foreach (Renderer r in rs)
                    {
                        if (r == null) continue;
                        if (!started) { b = r.bounds; started = true; }
                        else b.Encapsulate(r.bounds);
                    }
                    return started ? b : new Bounds(_root.position, Vector3.zero);
                }
            }

            public bool IsAtPeak => false;
            public float TimeToPeak => 0f;
            public string SubjectId => "TestSubject";
        }
    }
}
