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
    /// Story 1.10 added the axis the rig had no notion of: WHEN it shoots. Framing poses fire as soon as
    /// the camera settles; TIMING poses track the live actor and pull the shutter at a chosen distance from
    /// the peak, which is the only way to photograph what the timing curve actually does to a player.
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
        [HideInInspector] public GradingConfig gradingConfig;
        [HideInInspector] public string outputDir = "Temp/PhotoShoot";

        private const int ShotWidth = 960;
        private const int ShotHeight = 540;

        /// <summary>When the shutter is pulled.</summary>
        private enum Shutter
        {
            /// <summary>As soon as the camera settles, wherever the lifecycle happens to be.</summary>
            OnSettle,

            /// <summary>When the live actor's <c>PeakOffset</c> falls to <see cref="Pose.TargetOffset"/> —
            /// positive is before the peak window, 0 is inside it, negative is after.</summary>
            AtPeakOffset,

            /// <summary>Deep inside the peak window, as late in the money shot as the rig can reliably get.
            /// This is the pose that settles Story 1.10's gating question: under the old
            /// <c>Mathf.Abs(TimeToPeak)</c> reading it would score like a shot 1.2–1.5 s EARLY.</summary>
            LateInPeak,

            /// <summary>
            /// TWO captures from one camera position on CONSECUTIVE frames, inside the peak window: the
            /// same subject in the same world, framed dead centre and then on a thirds intersection.
            ///
            /// Consecutive frames is the whole point. Every other pose waits 1.8 s between shots, during
            /// which the drunk walks several units and the background behind him changes completely — so a
            /// "centred vs thirds" comparison assembled from two ordinary poses is really a comparison of
            /// two different photographs, and any judgement of it is worthless. Shooting during the peak
            /// also means he is standing still (Peak has advanceAlongRoute off), so the pair is as close to
            /// a controlled A/B as this game can produce.
            /// </summary>
            PeakPair,

            /// <summary>
            /// The same matched pair, but fired as soon as the camera settles instead of waiting for the
            /// peak. The pair is still two consecutive frames, which is the only property that matters for
            /// comparing PLACEMENT — timing is not under study here, and waiting for a peak costs a full
            /// 24.5 s lifecycle per vantage point, which is what made a twelve-direction sweep impractical.
            /// </summary>
            PairNow,
        }

        /// <summary>One vantage point. Note there is no "expected" field — the photograph is the result.</summary>
        private readonly struct Pose
        {
            public readonly string Name;
            public readonly float Distance;    // in subject heights
            public readonly float YawOffset;   // degrees off the line to the subject; 180 = looking away

            /// <summary>Which SIDE of the subject the camera stands on, as a world yaw: 0 = in front
            /// (+Z), 180 = behind him, 90/270 = either flank. Generalises the old FromBehind flag, which
            /// could only ever put the camera on one of two sides — not enough to find a vantage point
            /// with actual town behind him.</summary>
            public readonly float ViewYaw;

            public readonly bool Zoom;
            public readonly bool Wall;
            public readonly float WallAt;      // 0..1 along camera→subject; 0 = no wall
            public readonly string Intent;

            /// <summary>Where in the frame the subject's centre is aimed, normalized (0.5, 0.5 = dead
            /// centre, 1/3 or 2/3 = a thirds line, 0 = half out of the left edge).</summary>
            public readonly float TargetX, TargetY;

            public readonly Shutter Trigger;
            public readonly float TargetOffset;   // seconds of PeakOffset, for Shutter.AtPeakOffset

            public Pose(string name, float distance, string intent, float yaw = 0f,
                        bool zoom = false, bool wall = false, float viewYaw = 0f,
                        float targetX = 0.5f, float targetY = 0.5f,
                        Shutter trigger = Shutter.OnSettle, float targetOffset = 0f,
                        float wallAt = 0.45f)
            {
                Name = name; Distance = distance; Intent = intent;
                YawOffset = yaw; Zoom = zoom; Wall = wall; ViewYaw = viewYaw;
                TargetX = targetX; TargetY = targetY;
                Trigger = trigger; TargetOffset = targetOffset; WallAt = wallAt;
            }
        }

        private static readonly Pose[] Poses =
        {
            // --- Story 1.9's original vantage points (distance and gates) --------------------------------
            new Pose("a_close",        1.3f, "Standing right in front of him."),
            new Pose("b_mid",          2.5f, "A few paces back — 2.5 subject heights."),
            new Pose("c_far",          6f,   "Across the street."),
            new Pose("d_very_far",     14f,  "Far down the road."),
            new Pose("e_far_zoomed",   6f,   "Same spot as c_far, zoomed all the way in.", zoom: true),
            // Close enough to clear the size gate, so the shot actually reaches the occlusion test.
            new Pose("f_behind_wall",  1.2f, "A wall dropped between the camera and him.", wall: true),
            new Pose("g_looking_away", 2f,   "Close, but facing the opposite way.", yaw: 180f),
            new Pose("h_off_to_side",  2f,   "Close, but he is off past the edge of the frame.", yaw: 55f),
            new Pose("i_from_behind",  2f,   "Photographing him from behind.", viewYaw: 180f),

            // Added by the 2026-07-26 code review. Nothing here previously came closer than 1.2 subject
            // heights (~11 units against a ~2.2-unit box half-depth), so NO pose ever put an AABB corner
            // behind the near plane — the near-plane clipping branch and the BehindCamera early-out had no
            // coverage at all in this rig once the T-pose bench was deleted.
            new Pose("k_straddling",   0.12f, "Nose-to-nose — his box straddles the lens."),
            // The bug the review found: an AABB that ENCLOSES the camera is outside no frustum plane, so a
            // photo taken facing away used to score 100% and five stars. Must now read BehindCamera.
            new Pose("l_inside_away",  0.05f, "Standing inside him, facing the other way.", yaw: 180f),

            // --- Story 1.10: composition (AC1) ---------------------------------------------------------
            // The pair that isolates PLACEMENT — and it has to be a MATCHED pair (two consecutive frames
            // from one frozen camera position) to isolate anything at all.
            //
            // ⚠️ This was two ordinary poses, `m_thirds` then `n_centred`, 1.8 s apart. That is long enough
            // for the drunk to walk, so the two shots differed in HEIGHT as well as placement — 43.4% vs
            // 39.2% on the run that exposed it — and prominence swamped the term the pair existed to
            // measure, reporting the off-centre shot as the better composition immediately after the
            // placement rule had been reversed to say the opposite. A "controlled" comparison that is not
            // actually controlled produces confident, well-formed, wrong evidence.
            //
            // ⚠️ AND THE MATCHED PAIR STILL WAS NOT ENOUGH AT 2.5 HEIGHTS. Placement and prominence are not
            // independent: a rectilinear projection stretches toward the frame edges, so re-aiming the same
            // subject from centre to a thirds point genuinely makes him project LARGER — 40.4% of the frame
            // height became 45.8% across two consecutive frames from a frozen camera. At 2.5 heights that
            // straddles the sweet spot's floor (0.45), so the off-centre half cleared it while the centred
            // half did not, and PROMINENCE decided a comparison that exists to measure PLACEMENT.
            //
            // Shot at 2 heights instead, where both halves land comfortably inside the sweet spot and
            // prominence is 1.0 for each — leaving placement as the only term that can differ. The coupling
            // is real and not a defect (an off-centre subject IS bigger on screen); it just has to be kept
            // out of the one measurement that cannot tolerate it.
            new Pose("m_placement", 2f, "Placement pair at 2 heights: centred, then off-centre.",
                     trigger: Shutter.PairNow),

            // The anti-regression for the thirds weighting: a centred CLOSE-UP is a good photograph and the
            // GDD's bad case is "dead-centre TINY", not "dead-centre". If this comes back 2 stars, the
            // weighting is wrong — but that judgement belongs in the review of the photograph, NOT in the
            // caption, which is why the caption below describes only what the camera did.
            new Pose("o_centred_closeup", 1.5f, "A centred close-up at 1.5 subject heights."),

            // Cut off by the frame edge: aimed so his centre sits on the left edge, i.e. half of him is
            // outside the picture.
            new Pose("p_cut_off",  1.3f, "Half out of the frame — the edge slices through him.",
                     targetX: 0f),

            // --- Story 1.10: the retuned gate and the small end of the curve (AC1, AC4) ----------------
            // A distance LADDER across the band nothing else covers. The first shoot jumped straight from
            // 15.8% of the frame height to 37.6% with nothing in between, so both the gate boundary and the
            // whole lower half of the prominence ramp went unphotographed — and a rig that never exercises
            // a branch reports on it exactly as confidently as one that does.
            //
            // ⚠️ The captions say what the CAMERA did, never what the grade should be. The first version of
            // these two read "just INSIDE / just OUTSIDE the retuned size gate" — and the inside one came
            // back rejected, producing a perfectly well-formed log line that contradicted itself. An
            // expectation written before the shot is an assertion wearing a caption's clothes.
            new Pose("q_size_3h",   3.0f, "Three subject heights back."),
            new Pose("r_size_3h5",  3.5f, "Three and a half subject heights back."),
            new Pose("s_size_4h",   4.0f, "Four subject heights back."),
            new Pose("t_size_5h",   5.0f, "Five subject heights back."),

            // AC4's second half. Under the old AREA gate this shot was rejected TooSmall (4.5% < 8%) before
            // the occlusion linecasts ever ran, which left half of Story 1.9's subject gate effectively
            // dead. It now passes on size, so the occlusion test genuinely decides it.
            new Pose("u_wall_mid", 2.5f, "A wall dropped in at 2.5 heights, where the size gate passes.",
                     wall: true, wallAt: 0.3f),

            // --- Story 1.10: timing (AC2) --------------------------------------------------------------
            // Same framing as b_mid every time, so the only thing varying across these five is WHEN the
            // shutter fell. Positive offsets are before the peak window, negative after it.
            new Pose("v_time_early_2s",   2.5f, "Shutter pulled two seconds before the peak window.",
                     trigger: Shutter.AtPeakOffset, targetOffset: 2f),
            new Pose("w_time_early_05s",  2.5f, "Shutter pulled half a second before the peak window.",
                     trigger: Shutter.AtPeakOffset, targetOffset: 0.5f),
            new Pose("x_time_peak_start", 2.5f, "Shutter pulled on the first frame of the peak window.",
                     trigger: Shutter.AtPeakOffset, targetOffset: 0f),
            // The Story 1.10 question, photographed: deep inside the money shot, TimeToPeak reads about -1.2
            // while PeakOffset reads 0. Both numbers are printed for this shot so the reader can see for
            // themselves what the old Mathf.Abs(TimeToPeak) scheme would have made of it.
            new Pose("y_time_peak_late",  2.5f, "Shutter pulled deep inside the peak window.",
                     trigger: Shutter.LateInPeak),
            new Pose("z1_time_late_05s",  2.5f, "Shutter pulled half a second after the peak window ended.",
                     trigger: Shutter.AtPeakOffset, targetOffset: -0.5f),
            new Pose("z2_time_late_2s",   2.5f, "Shutter pulled two seconds after the peak window ended.",
                     trigger: Shutter.AtPeakOffset, targetOffset: -2f),

            // --- Story 1.10: both axes at once (AC3) ---------------------------------------------------
            // THE MONEY SHOT — the photograph this whole event exists to produce, and the only pose that
            // asks for everything at the same time: well-sized, well-placed, and inside the peak window.
            // Added after the second shoot, where the highest grade in twenty-seven photographs was four
            // stars: the timing ladder ran at 2.5 heights and the best-composed shot fired seven seconds
            // early, so nothing ever had both axes high AT ONCE. Five stars being reachable was true by
            // arithmetic and unphotographed, which is precisely the kind of "proof" this rig exists to
            // refuse.
            //
            // Aimed DEAD CENTRE since the placement term was reversed. It was on a thirds intersection
            // before, which is what "best possible placement" meant while the code followed the GDD's
            // original FR6 — the pose has to move with the definition or it stops being the money shot.
            new Pose("z3_money_shot",  2f, "Two heights back, dead centre, inside the peak window.",
                     trigger: Shutter.LateInPeak),

            // The same framing on the middle of the timing ramp, so the star ladder shows what being LATE
            // costs when nothing else is wrong with the photograph.
            new Pose("z4_money_late_12s", 2f, "Same framing, shutter pulled 1.2 s after the peak window.",
                     trigger: Shutter.AtPeakOffset, targetOffset: -1.2f),
        };

        /// <summary>
        /// The placement study (Story 1.10 review, 2026-07-26). Four matched centred-vs-thirds pairs, one
        /// per side of the subject, shot in the REAL TOWN rather than the rig's private world.
        ///
        /// ⚠️ WHY THIS EXISTS. The main shoot's world is a featureless grey plane, so its thirds pose put
        /// the drunk in the corner of an entirely EMPTY frame — and Alexv, looking at that photograph,
        /// preferred the centred one. That is a completely reasonable reaction to that picture, but the
        /// rule of thirds earns its keep by giving the subject somewhere to look or walk INTO, and an empty
        /// plane has nothing to offer. The photograph was honest; the world it was taken in was not
        /// representative. Suspect the rig — including when the rig has produced a perceptual result.
        ///
        /// Four sides because the town is not uniform: some vantage points have a building behind him and
        /// some have open sky, and which is which cannot be predicted from here. Produce all four, then
        /// look at them and judge which pairs are worth comparing.
        /// </summary>
        /// ⚠️ TWELVE directions, not four. The first attempt used the four compass points and TWO of them
        /// came back with the drunk entirely hidden behind a pine tree — the grader dutifully drew its box
        /// over the tree and scored the shot 98 % / 5★. The route runs through woodland, so whether a
        /// vantage point can see him at all is a matter of luck, and the rig cannot find out by asking
        /// physics (see the occlusion note in the story: this scene's trees carry no colliders on the
        /// occluder mask, so a linecast through one reports a clear view). Sweeping wide and then LOOKING
        /// at the results is the honest way to find the usable ones.
        private static readonly Pose[] PlacementPoses = BuildPlacementSweep();

        private static Pose[] BuildPlacementSweep()
        {
            const int Directions = 12;
            var poses = new Pose[Directions];

            for (int i = 0; i < Directions; i++)
            {
                float yaw = i * (360f / Directions);
                poses[i] = new Pose($"dir{i * 30:D3}", 2f,
                                    $"Camera on the {yaw:F0}° side of him, two subject heights back.",
                                    viewYaw: yaw, trigger: Shutter.PairNow);
            }
            return poses;
        }

        /// <summary>When true, run <see cref="PlacementPoses"/> in whatever scene is already loaded
        /// instead of the full battery in the rig's private world.</summary>
        [HideInInspector] public bool placementStudy;

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

            // Bind the render target BEFORE anything is graded. ShotGrader measures against
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
            _log.AppendLine("Green box = counted, red = rejected. Faint grey dashes = the frame CENTRE, which");
            _log.AppendLine("the placement half of the composition score measures distance from.");
            _log.AppendLine();
            LogConfig();

            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "Wall";
            _wall.SetActive(false);

            if (!placementStudy)
            {
                // The empty-world shot goes FIRST, while the manager is held off. Waiting for an empty
                // world later never works: the manager respawns half a second after each despawn, so the
                // wait just burns a minute watching him walk.
                yield return ShootEmptyWorld();
                manager.enabled = true;
            }

            float t = 0f;
            while (manager.ActiveActors.Count == 0 && t < 30f) { t += Time.deltaTime; yield return null; }
            if (manager.ActiveActors.Count == 0)
            {
                _log.AppendLine("No actor ever spawned — nothing else to photograph.");
                yield break;
            }

            foreach (Pose p in placementStudy ? PlacementPoses : Poses)
            {
                if (p.Trigger == Shutter.OnSettle) yield return Shoot(p);
                else if (p.Trigger == Shutter.PeakPair) yield return ShootPair(p, waitForPeak: true);
                else if (p.Trigger == Shutter.PairNow) yield return ShootPair(p, waitForPeak: false);
                else yield return ShootTimed(p);
            }
        }

        /// <summary>
        /// Prints the tunables these photographs were graded against, read back from the LIVE asset.
        ///
        /// ⚠️ This is not decoration. Adding a field to a ScriptableObject class does NOT add it to an
        /// existing asset, and this project hand-authors those assets as YAML — so a key that was mistyped,
        /// or a field that was renamed without the asset following, silently reads as 0. A zeroed
        /// minSubjectHeight disables the gate; a zeroed sweet spot caps every grade. Both produce a clean
        /// console and a plausible-looking set of photographs. Printing them beside the results is what
        /// makes "the rig was measuring against nothing" visible instead of inferred.
        /// </summary>
        private void LogConfig()
        {
            if (gradingConfig == null)
            {
                _log.AppendLine("GRADING CONFIG: NONE WIRED — every shot below is a placeholder grade. (!)");
                _log.AppendLine();
                return;
            }

            gradingConfig.ResolveProminenceCurve(out float below, out float idealMin,
                                                 out float idealMax, out float above);
            gradingConfig.ResolveTimingWindow(out float full, out float zero);

            _log.AppendLine("GRADING CONFIG read back from the live asset:");
            _log.AppendLine($"    gate:       minSubjectHeight {gradingConfig.SafeMinSubjectHeight:0.###} " +
                            $"(frame-height fraction)  minVisibleSamples {gradingConfig.SafeMinVisibleSamples:0.###} " +
                            $"over {gradingConfig.SafeOcclusionSamples} samples");
            _log.AppendLine($"    prominence: 0 below {below:0.###} → full marks {idealMin:0.###}..{idealMax:0.###} " +
                            $"→ 0 above {above:0.###}");
            _log.AppendLine($"    placement:  centreWeight {gradingConfig.SafeCentreWeight:0.###}  " +
                            $"cutoffWeight {gradingConfig.SafeCutoffWeight:0.###}");
            _log.AppendLine($"    timing:     full marks within ±{full:0.###}s of the peak WINDOW, zero at ±{zero:0.###}s");

            if (gradingConfig.TryGetConfigProblem(out string problem))
                _log.AppendLine($"    ⚠ PROBLEM: {problem}");

            _log.AppendLine();
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

        // =====================================================================
        // FRAMING POSES — fire as soon as the camera settles
        // =====================================================================

        private IEnumerator Shoot(Pose p)
        {
            if (!TryGetActor(out EventActor actor))
            {
                float w = 0f;
                while (!TryGetActor(out actor) && w < 40f) { w += Time.deltaTime; yield return null; }
                if (actor == null) yield break;
            }

            PlaceCamera(p, actor.Bounds);

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
            if (!TryGetActor(out actor))
            {
                _log.AppendLine($"{p.Name}  —  SKIPPED: he despawned mid-pose (pooled reuse), nothing to shoot.");
                _log.AppendLine();
                yield break;
            }

            // Re-aim only (not reposition): the camera distance was set against the bounds we had, and the
            // aim is what has to track him as he staggers.
            PlaceCamera(p, actor.Bounds, aimOnly: true);

            yield return FireAndRecord(p, actor);
            yield return new WaitForSecondsRealtime(1.8f);
        }

        // =====================================================================
        // TIMING POSES — track the live actor and fire at a chosen moment
        // =====================================================================

        /// <summary>
        /// Holds the framing steady on the live actor and pulls the shutter when the lifecycle reaches the
        /// moment this pose is about. This is the axis the rig had no notion of before Story 1.10: every
        /// pose used to fire wherever the lifecycle happened to be, so the timing term was photographed at
        /// an arbitrary, unrecorded moment.
        ///
        /// ⚠️ The actor is re-read from the manager on EVERY poll, never held across the wait. These waits
        /// are the longest in the rig — a full lifecycle is 24.5 s — so a cached reference here would be
        /// almost guaranteed to become a different, pooled event mid-wait.
        /// </summary>
        private IEnumerator ShootTimed(Pose p)
        {
            photo.SetPhotoMode(true);
            photo.SetZoom(p.Zoom ? 1f : 0f);

            // --- Phase A: wait for a lifecycle that has not yet reached the moment ----------------------
            // Without this the rig would join mid-lifecycle, find the crossing already behind it, and fire
            // immediately at completely the wrong instant — while writing a caption saying otherwise.
            const float Margin = 0.75f;
            float waited = 0f;
            bool armed = false;
            while (waited < 60f)
            {
                if (TryGetActor(out EventActor a))
                {
                    bool beforeMoment = p.Trigger == Shutter.LateInPeak
                        ? a.PeakOffset > 0f                       // still ahead of the peak window
                        : a.PeakOffset > p.TargetOffset + Margin;

                    if (beforeMoment) { armed = true; break; }

                    PlaceCamera(p, a.Bounds);                     // keep framing while we wait
                }
                waited += Time.deltaTime;
                yield return null;
            }

            if (!armed)
            {
                _log.AppendLine($"{p.Name}  —  SKIPPED: no lifecycle reached the arming point within 60 s.");
                _log.AppendLine();
                yield break;
            }

            // --- Phase B: track him and fire at the moment ---------------------------------------------
            float tracked = 0f;
            float peakHeld = 0f;
            while (tracked < 60f)
            {
                if (!TryGetActor(out EventActor a))
                {
                    // He despawned before the moment arrived. Bail rather than shoot the next lifecycle at
                    // an untracked point — a photograph captioned with the wrong instant is worse than none.
                    _log.AppendLine($"{p.Name}  —  SKIPPED: he despawned before the moment arrived.");
                    _log.AppendLine();
                    yield break;
                }

                PlaceCamera(p, a.Bounds);

                bool fire;
                if (p.Trigger == Shutter.LateInPeak)
                {
                    // Hold inside the peak as long as we dare, then fire. Either branch lands deep in the
                    // money shot, which is the point: both would have scored ~0.2–0.35 under the old
                    // Mathf.Abs(TimeToPeak) reading.
                    if (a.IsAtPeak) peakHeld += Time.deltaTime;
                    fire = peakHeld > 0f && (peakHeld >= 1.2f || !a.IsAtPeak);
                }
                else
                {
                    fire = a.PeakOffset <= p.TargetOffset;
                }

                if (fire)
                {
                    yield return FireAndRecord(p, a);
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield break;
                }

                tracked += Time.deltaTime;
                yield return null;
            }

            _log.AppendLine($"{p.Name}  —  SKIPPED: the moment never arrived within 60 s of tracking.");
            _log.AppendLine();
        }

        /// <summary>
        /// Two captures from ONE camera position on consecutive frames, inside the peak window: dead centre,
        /// then on a thirds intersection. The pair is the artefact — either photograph alone says nothing
        /// about placement, because the only honest comparison is one where everything else is identical.
        /// </summary>
        private IEnumerator ShootPair(Pose p, bool waitForPeak)
        {
            photo.SetPhotoMode(true);
            photo.SetZoom(0f);

            if (waitForPeak)
            {
                // --- Arm: wait for a lifecycle that has not reached the peak yet -----------------------
                float waited = 0f;
                bool armed = false;
                while (waited < 90f)
                {
                    if (TryGetActor(out EventActor a))
                    {
                        if (a.PeakOffset > 0f) { armed = true; break; }
                        PlaceCamera(p, a.Bounds);
                    }
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (!armed)
                {
                    _log.AppendLine($"{p.Name}  —  SKIPPED: no lifecycle reached the arming point within 90 s.");
                    _log.AppendLine();
                    yield break;
                }
            }

            // --- Track him until he is ready to be shot ------------------------------------------------
            float tracked = 0f;
            int settleFrames = 0;
            while (tracked < 90f)
            {
                if (!TryGetActor(out EventActor a))
                {
                    // Wait for the next one rather than bailing: without a peak to wait for, an unlucky
                    // arrival during the respawn gap would otherwise drop a whole vantage point.
                    tracked += Time.deltaTime;
                    yield return null;
                    continue;
                }

                PlaceCamera(p, a.Bounds);

                // Two settled frames so the camera transform and the animation have both been applied
                // before the shutter, exactly as the framing poses do.
                if (waitForPeak ? a.IsAtPeak : ++settleFrames >= 2) break;

                tracked += Time.deltaTime;
                yield return null;
            }

            if (!TryGetActor(out EventActor actor))
            {
                _log.AppendLine($"{p.Name}  —  SKIPPED: he vanished before the pair could be taken.");
                _log.AppendLine();
                yield break;
            }

            // Freeze the camera POSITION here and only re-aim from now on, so the two frames differ in
            // exactly one thing. Re-placing between the shots would move the camera as he sways, and the
            // backgrounds would no longer match.
            PlaceCamera(p, actor.Bounds);
            Vector3 frozen = cam.transform.position;

            // Shot 1 — dead centre.
            PlaceCamera(p, actor.Bounds, aimOnly: true, targetX: 0.5f, targetY: 0.5f);
            yield return FireAndRecord(p, actor, p.Name + "_centred",
                                       p.Intent + "  [CENTRED half of the pair]");

            // Shot 2 — thirds intersection, next frame, same spot. Re-read the actor (pooled) but do not
            // let the camera move.
            if (!TryGetActor(out actor))
            {
                _log.AppendLine($"{p.Name}  —  thirds half SKIPPED: he despawned between the two frames.");
                _log.AppendLine();
                yield break;
            }

            cam.transform.position = frozen;
            PlaceCamera(p, actor.Bounds, aimOnly: true, targetX: 1f / 3f, targetY: 2f / 3f);
            yield return FireAndRecord(p, actor, p.Name + "_thirds",
                                       p.Intent + "  [THIRDS half of the pair]");
        }

        // =====================================================================
        // SHARED
        // =====================================================================

        /// <summary>The live actor, or false. Mirrors <c>PhotoModeController.GradeBestSubject</c>'s checks:
        /// ActiveActors tracks the LIFECYCLE, not activation, so an inactive entry reports plausible bounds
        /// while being invisible in the picture.</summary>
        private bool TryGetActor(out EventActor actor)
        {
            actor = null;
            if (manager == null || manager.ActiveActors.Count == 0) return false;

            EventActor a = manager.ActiveActors[0];
            if (a == null || !a.isActiveAndEnabled) return false;

            actor = a;
            return true;
        }

        /// <summary>
        /// Positions and aims the camera for a pose. <paramref name="aimOnly"/> keeps the camera where it
        /// is and only re-points it, for when the subject has walked since the distance was chosen.
        ///
        /// The aim puts the subject's centre at the pose's normalized screen target, which is what makes a
        /// "on the thirds line" pose and a "dead centre" pose differ in EXACTLY one thing.
        /// </summary>
        private void PlaceCamera(Pose p, Bounds b, bool aimOnly = false,
                                 float targetX = float.NaN, float targetY = float.NaN)
        {
            // NaN means "use the pose's own aim". A nullable would say the same thing without the sentinel,
            // but this runs every frame of a tracking loop and a float pair costs nothing.
            if (float.IsNaN(targetX)) targetX = p.TargetX;
            if (float.IsNaN(targetY)) targetY = p.TargetY;

            float height = Mathf.Max(0.01f, b.size.y);

            if (!aimOnly)
            {
                Vector3 dir = Quaternion.Euler(0f, p.ViewYaw, 0f) * Vector3.forward;
                cam.transform.position = b.center + dir * (p.Distance * height);
            }

            Vector3 camPos = cam.transform.position;
            Vector3 toSubject = b.center - camPos;
            if (toSubject.sqrMagnitude < 1e-6f) toSubject = Vector3.forward;

            // Where the subject would land if we simply looked straight at him: dead centre. Rotating the
            // camera by these two angles slides him to the pose's target instead. Derived from the camera's
            // own FOV and aspect so it stays correct at any zoom or render size.
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float tanV = Mathf.Tan(vHalf);
            float tanH = tanV * cam.aspect;

            float fx = (targetX - 0.5f) * 2f;      // -1 = left edge, +1 = right edge
            float fy = (targetY - 0.5f) * 2f;

            // Yawing the camera RIGHT slides the subject LEFT in the frame, hence the negation. Pitching
            // DOWN (positive X euler) slides him UP.
            float yawDeg = -Mathf.Atan(fx * tanH) * Mathf.Rad2Deg;
            float pitchDeg = Mathf.Atan(fy * tanV) * Mathf.Rad2Deg;

            cam.transform.rotation = Quaternion.LookRotation(toSubject, Vector3.up)
                                     * Quaternion.Euler(pitchDeg, yawDeg + p.YawOffset, 0f);

            _wall.SetActive(p.Wall);
            if (p.Wall)
            {
                _wall.transform.position = Vector3.Lerp(camPos, b.center, p.WallAt);
                _wall.transform.localScale = new Vector3(height * 5f, height * 5f, 0.4f);
                _wall.transform.rotation = Quaternion.LookRotation(b.center - camPos, Vector3.up);
            }

            Physics.SyncTransforms();
        }

        /// <summary>
        /// Pulls the real shutter, saves the photograph, and writes the numbers behind it.
        ///
        /// <c>TimeToPeak</c> is read here as well as <c>PeakOffset</c> and printed side by side, because the
        /// gap between them IS Story 1.10's timing decision made visible: inside the money shot the first
        /// reads about -1.2 while the second reads 0. Reading them one line apart, in the same frame the
        /// shutter fired, is what turns that from an argument into evidence.
        /// </summary>
        private IEnumerator FireAndRecord(Pose p, EventActor actor, string nameOverride = null,
                                          string intentOverride = null)
        {
            string shotName = nameOverride ?? p.Name;
            string intent = intentOverride ?? p.Intent;
            Bounds b = actor.Bounds;
            float timeToPeak = actor.TimeToPeak;
            bool atPeak = actor.IsAtPeak;
            float distance = Vector3.Distance(cam.transform.position, b.center);

            photo.Capture();          // the real shutter
            yield return new WaitForEndOfFrame();

            ShotGrade grade = photo.LastGrade;
            GradeDetail detail = photo.LastDetail;
            Save(Path.Combine(outputDir, shotName + ".png"), detail);

            // Where the grader's box actually landed, normalized — so an aim that silently went to the wrong
            // place shows up as a number as well as in the picture. (A rig whose aim is subtly wrong looks
            // exactly like a scoring bug.)
            Rect r = detail.ScreenRect;
            float boxX = r.width > 0f ? r.center.x / ShotWidth : float.NaN;
            float boxY = r.height > 0f ? r.center.y / ShotHeight : float.NaN;

            _log.AppendLine($"{shotName}  —  {intent}");
            _log.AppendLine($"    VERDICT: {(detail.Miss == GradeMiss.None ? $"counted — {grade.Percent01:P0}  {grade.Stars}★" : $"rejected — {detail.Miss}")}");

            if (detail.Miss == GradeMiss.None)
                _log.AppendLine($"    axes: composition {grade.Composition01:P0} x timing {grade.Timing01:P0}" +
                                $"   (subject seen {grade.Subject01:P0})");

            // Through the *Text accessors: a shot rejected before it was projected has no size measurement,
            // and since the review made the sentinel explicit (NotEvaluated = -1) printing the raw field
            // would read "height -100.0%". "n/a" is what those gates actually know.
            _log.AppendLine($"    size: height {detail.HeightText}  framed {detail.FramedText}  " +
                            $"area {detail.CoverageText}  box {r.width:F0}x{r.height:F0}px");
            _log.AppendLine($"    placement: box centre ({boxX:F3}, {boxY:F3})  " +
                            $"[dead centre = 0.500, 0.500 — closer is better]");
            _log.AppendLine($"    timing: PeakOffset {detail.PeakOffsetText}  TimeToPeak {timeToPeak:+0.00;-0.00;0.00}s  " +
                            $"IsAtPeak {atPeak}");
            _log.AppendLine($"    camera: {distance:F1} units ({p.Distance:F1} heights)  fov {cam.fieldOfView:F0}°  " +
                            $"yaw {p.YawOffset:F0}°  wall {p.Wall}  line-of-sight {detail.VisibleText}");
            _log.AppendLine();
        }

        /// <summary>
        /// Renders the camera to a texture and draws the grader's own box on it, plus the thirds grid the
        /// composition score is measured against.
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

                // The centre guide goes down FIRST so the grader's box draws over it, not under it. Drawing
                // the invisible thing the score is computed from is the same move that made 1.9's box
                // overlay decisive: it turns "does 62% look right?" into "how far off centre is he?".
                //
                // ⚠️ This drew a rule-of-thirds grid until the placement term was reversed to reward
                // centring. A guide marking lines that nothing scores against is worse than none.
                DrawCentreGuide(tex, new Color(0.75f, 0.75f, 0.78f));

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

        /// <summary>Dashed lines through the frame's centre — the point the placement term measures
        /// distance from. Dashed so they cannot be mistaken for the grader's solid box.</summary>
        private static void DrawCentreGuide(Texture2D tex, Color c)
        {
            int cx = tex.width / 2;
            int cy = tex.height / 2;

            for (int y = 0; y < tex.height; y++)
                if ((y / 6) % 2 == 0) Px(tex, cx, y, c);

            for (int x = 0; x < tex.width; x++)
                if ((x / 6) % 2 == 0) Px(tex, x, cy, c);
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
