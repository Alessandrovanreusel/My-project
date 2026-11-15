#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using CameraGame.Events;
using CameraGame.Grading;

namespace CameraGame.UI
{
    /// <summary>
    /// PHASE L — Story 1.19's legibility measurement (AC4), as a part of <see cref="GradeHudShootRunner"/>
    /// rather than a fork of it: the same shutter, the same scene, the same capture technique that Phase 0
    /// proves before anything else runs.
    ///
    /// ⚠️ WHY FOUR PICTURES OF ONE FROZEN FRAME. "Text against outline" and "text against panel" are
    /// questions about WHICH PIXELS are glyph, outline and panel — and every auto-detector the 1.12 tooling
    /// tried for that produced plausible wrong numbers (tools/verification/build_hud_panel_sheet.py, its
    /// header). The runner knows where the text is, so it SAYS so, in pixels, by photographing the same
    /// instant four ways:
    ///   A  the readout as shipped;
    ///   M  every glyph painted the marker magenta, and the outline (if any) painted pure green — a mask of
    ///      exactly which pixels are glyph and which are outline, made by the renderer itself;
    ///   P  the labels switched off — the panel and the world each glyph is drawn on;
    ///   N  the shipped colours with the outline switched off — the "without outline" picture.
    /// The world must not move between them, so the frame is frozen (<c>Time.timeScale = 0</c>) first, and
    /// the run PROVES it was: outside the panel, A and P must be identical, and the max difference is
    /// written beside every set.
    ///
    /// ⚠️ THE HUD'S HOLD RUNS ON UNSCALED TIME (GradeHud.cs), so freezing the world does not freeze the
    /// readout's countdown. The four grabs take four frames; the alpha is recorded at every grab and a set
    /// that caught the fade is flagged, never silently measured.
    ///
    /// The maths is OFFLINE (tools/verification/measure_hud_legibility.py) so it can be iterated without
    /// re-running Play mode. This file only takes the pictures and writes a JSON sidecar per set.
    /// </summary>
    public partial class GradeHudShootRunner
    {
        /// <summary>Set by the "Legibility at 1080p" menu item: run Phase 0 and Phase L only.</summary>
        [HideInInspector] public bool legibilityOnly;

        /// <summary>Also sweep candidate reds and outline thicknesses (decision 1d). Rig-side probes on the
        /// live labels; labelled "probe" in every caption and sidecar.</summary>
        [HideInInspector] public bool runCandidates;

        /// <summary>While true, <see cref="CaptureAndPhotograph"/> also photographs the variants of its
        /// settled frame — Phase A's real-shutter states, measured.</summary>
        private bool _legibility;

        /// <summary>The outline's marker colour in variant M. Pure green: nothing in the readout is green
        /// in that variant (the glyphs are magenta), so a green pixel can only be outline.</summary>
        private static readonly Color OutlineMarker = new Color(0f, 1f, 0f, 1f);

        /// <summary>Pixels kept around the panel in the cropped variants — enough for the freeze control
        /// to see the world beside the panel, small enough to keep each set a few hundred KB.</summary>
        private const int CropMargin = 24;

        private readonly List<BackgroundPose> _chosenBackgrounds = new List<BackgroundPose>();

        private string LegibilityDir => Path.Combine(outputDir, "legibility");

        private static readonly string[] LabelFields = { "ratingLabel", "axesLabel", "whyLabel" };

        // --- The sidecar, one per set --------------------------------------------------------------------

        [System.Serializable]
        public class LabelInfo
        {
            public string field;
            public string text;
            public float[] color;            // the Text.color as shipped (A and N), 0..1
            public float[] rect;             // screen pixels, Unity convention: x, y from the BOTTOM, w, h
            public bool hasOutline;
            public float[] outlineColor;
            public float[] outlineDistance;
        }

        [System.Serializable]
        public class VariantSet
        {
            public string id;
            public string source;            // "real-shutter" | "channel-raised" | "channel-raised probe"
            public string background;
            public string scenario;
            public string caption;
            public string grade;
            public string probe;             // rig-side tuning probe applied to the live labels, if any
            public int screenW, screenH, captureW, captureH;
            public float scaleFactor;
            public bool at1080;
            public float[] panelRect;        // screen pixels, Unity convention
            public float[] panelColor;       // the panel Image's colour, as authored
            public int[] crop;               // x, y (from the bottom), w, h of the cropped variants
            public LabelInfo[] labels;
            public float[] alphaAtGrab;      // A, M, P, N
            public bool heldFullAlpha;
            public int freezeMaxDiff;        // 0..255, A vs P outside the panel rect
            public float freezeFractionDiff; // fraction of those pixels differing by more than 2/255
            public int unfrozenMaxDiff;      // 0..255, the SAME measure between A and a frame grabbed just
            public float unfrozenFractionDiff; // before the freeze: the positive control for the zero above
            public string fileA, fileM, fileP, fileN, fileFull;
        }

        private struct BackgroundPose
        {
            public string Name;
            public Vector3 Position;
            public Quaternion Rotation;
            public float MeanLuminance;
            public float P95Luminance;
            public int Spot;
            public float Yaw, Pitch;
        }

        // =====================================================================
        // PHASE L
        // =====================================================================

        private IEnumerator PhaseL_Legibility()
        {
            Section("PHASE L — legibility at 1080p: frozen frames, glyph masks, every colour (Story 1.19 AC4)");
            Directory.CreateDirectory(LegibilityDir);

            bool at1080 = Screen.width == 1920 && Screen.height == 1080
                          && Mathf.Abs(hudCanvas.scaleFactor - 1f) < 0.001f;
            _log.AppendLine($"Resolution: Screen {Screen.width}x{Screen.height}, HUD canvas scaleFactor " +
                            $"{hudCanvas.scaleFactor:0.0000}.");
            _log.AppendLine(at1080
                ? "    => 1920x1080 at scale 1.0: these numbers ARE the shipping resolution."
                : "    => ⚠ NOT 1920x1080 / 1.0 — DO NOT REPORT ANY NUMBER BELOW AS 1080p.");
            _log.AppendLine();
            _log.AppendLine("Every set below is FOUR pictures of ONE frozen frame (timeScale 0): A as shipped,");
            _log.AppendLine("M glyphs magenta + outline green, P labels off, N outline off. 'freeze' is the max");
            _log.AppendLine("difference between A and P OUTSIDE the panel (0 = the world did not move).");
            _log.AppendLine("Source: 'real-shutter' = photo.Capture(), the player's own path. 'channel-raised' =");
            _log.AppendLine("a grade built by the real ShotGrade factories and raised on the real channel, so the");
            _log.AppendLine("real GradeHud handler, Format and colour choice run; only the grader is bypassed.");
            _log.AppendLine();

            Section("PHASE L.1 — every Phase A state, real shutter, measured");
            _legibility = true;
            try
            {
                yield return PhaseA_EveryHudState();
            }
            finally
            {
                _legibility = false;
            }

            yield return WaitForHudToClear();
            yield return SurveyBackgrounds();

            Section("PHASE L.3 — every readout colour over the SAME frozen background (channel-raised)");
            foreach (BackgroundPose bg in _chosenBackgrounds)
                yield return EveryColourOn(bg);

            if (runCandidates)
                yield return CandidateSweep();
        }

        // --- L.2: where are the brightest backgrounds? ---------------------------------------------------

        /// <summary>
        /// Turns the camera through spots, headings and pitches around the subject's route with the readout
        /// HIDDEN, and measures the world's luminance inside the panel's rect. Keeps the three brightest
        /// (distinct spots) and the darkest. The camera stands at the player's eye height (PlayerConfig, via
        /// the rig) on ground found by a ray — a pose a player could stand in, not a camera inside a wall.
        /// </summary>
        private IEnumerator SurveyBackgrounds()
        {
            Section("PHASE L.2 — survey: the brightest real backgrounds behind the panel");
            _chosenBackgrounds.Clear();

            Vector3 centre;
            if (TryGetActor(out EventActor a)) centre = a.Bounds.center;
            else if (player != null) centre = player.transform.position;
            else centre = cam.transform.position;

            float groundY = centre.y;
            if (Physics.Raycast(centre + Vector3.up * 2f, Vector3.down, out RaycastHit under, 50f, ~0,
                                QueryTriggerInteraction.Ignore))
                groundY = under.point.y;

            photo.SetPhotoMode(true);   // the viewfinder overlay is part of what a player sees there
            yield return null;
            yield return null;

            Rect panel = HudRect();
            float[] radii = { 5f, 14f };
            float[] pitches = { -12f, 0f, 30f };       // + is UP (the camera's x rotation is negated below)
            var results = new List<BackgroundPose>();
            int refused = 0;

            for (int r = 0; r < radii.Length; r++)
            {
                for (int b = 0; b < 8; b++)
                {
                    float ang = b * 45f * Mathf.Deg2Rad;
                    Vector3 xz = centre + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * radii[r];

                    // Ground under the spot, close to the subject's ground: not a roof, not a pine top.
                    if (!Physics.Raycast(new Vector3(xz.x, groundY + 40f, xz.z), Vector3.down, out RaycastHit hit,
                                         120f, ~0, QueryTriggerInteraction.Ignore)
                        || hit.normal.y < 0.7f || Mathf.Abs(hit.point.y - groundY) > 4f)
                    {
                        refused++;
                        continue;
                    }

                    Vector3 eye = hit.point + Vector3.up * eyeHeight;
                    if (Physics.CheckSphere(eye, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        refused++;
                        continue;
                    }

                    int spot = r * 8 + b;
                    for (int y = 0; y < 8; y++)
                    {
                        foreach (float pitch in pitches)
                        {
                            Quaternion rot = Quaternion.Euler(-pitch, y * 45f, 0f);
                            cam.transform.SetPositionAndRotation(eye, rot);

                            Texture2D t = null;
                            yield return Grab(x => t = x);
                            LuminanceInside(t, panel, out float mean, out float p95);
                            Destroy(t);

                            results.Add(new BackgroundPose
                            {
                                Position = eye, Rotation = rot, MeanLuminance = mean, P95Luminance = p95,
                                Spot = spot, Yaw = y * 45f, Pitch = pitch,
                            });
                        }
                    }
                }
            }

            _log.AppendLine($"Centre (the subject, or the player): {centre}. Spots at {radii[0]} m and {radii[1]} m " +
                            $"on 8 bearings, 8 headings, pitches -12/0/+30 deg. {refused} spot(s) refused " +
                            "(no ground there, too steep, a roof, or inside a collider).");
            _log.AppendLine($"Poses measured: {results.Count}. Luminance = WCAG relative luminance of the WORLD " +
                            "inside the panel rect, readout hidden.");

            if (results.Count == 0)
            {
                _log.AppendLine("⚠ NO POSE MEASURED — SUSPECT THE RIG. L.3 has nothing to stand on.");
                yield break;
            }

            results.Sort((x, y) => y.MeanLuminance.CompareTo(x.MeanLuminance));

            _log.AppendLine();
            _log.AppendLine("  brightest 10:");
            for (int i = 0; i < Mathf.Min(10, results.Count); i++) _log.AppendLine("    " + Describe(results[i]));
            _log.AppendLine("  darkest 3:");
            for (int i = Mathf.Max(0, results.Count - 3); i < results.Count; i++)
                _log.AppendLine("    " + Describe(results[i]));

            var usedSpots = new HashSet<int>();
            foreach (BackgroundPose p in results)
            {
                if (_chosenBackgrounds.Count >= 3) break;
                if (!usedSpots.Add(p.Spot)) continue;
                BackgroundPose named = p;
                named.Name = "bright" + (_chosenBackgrounds.Count + 1);
                _chosenBackgrounds.Add(named);
            }

            BackgroundPose dark = results[results.Count - 1];
            dark.Name = "dark1";
            _chosenBackgrounds.Add(dark);

            _log.AppendLine();
            _log.AppendLine("  chosen for L.3:");
            foreach (BackgroundPose p in _chosenBackgrounds) _log.AppendLine($"    {p.Name}: {Describe(p)}");
            _log.AppendLine();
        }

        private static string Describe(BackgroundPose p) =>
            $"mean L {p.MeanLuminance:0.000}  p95 L {p.P95Luminance:0.000}  at ({p.Position.x:0.0}, " +
            $"{p.Position.y:0.0}, {p.Position.z:0.0}) heading {p.Yaw:0} pitch {p.Pitch:+0;-0;0}  spot {p.Spot}";

        // --- L.3: every colour on one frozen background --------------------------------------------------

        /// <summary>The six readout states, built by the REAL factories and raised on the REAL channel.
        /// Stars come from the shipped GradingConfig's own scale, so each grade lands in the band it would
        /// in play.</summary>
        private List<KeyValuePair<string, ShotGrade>> RaisedGrades()
        {
            StarScale scale = gradingConfig != null ? gradingConfig.SafeStarScale : StarScale.Default;
            return new List<KeyValuePair<string, ShotGrade>>
            {
                new KeyValuePair<string, ShotGrade>("1_five_star",
                    ShotGrade.Scored(1f, 1f, 1f, scale, "TownDrunk", 0f)),
                new KeyValuePair<string, ShotGrade>("2_three_star",
                    ShotGrade.Scored(1f, 1f, 0.55f, scale, "TownDrunk", -0.9f)),
                new KeyValuePair<string, ShotGrade>("3_one_percent",
                    ShotGrade.Scored(1f, 1f, 0.012f, scale, "TownDrunk", -2.0f)),
                new KeyValuePair<string, ShotGrade>("4_counted_zero",
                    ShotGrade.Scored(1f, 0.92f, 0f, scale, "TownDrunk", -2.4f)),
                new KeyValuePair<string, ShotGrade>("5_missed",
                    ShotGrade.Missed(GradeMiss.TooSmall, "TownDrunk")),
                new KeyValuePair<string, ShotGrade>("6_not_graded", ShotGrade.Placeholder),
            };
        }

        private IEnumerator EveryColourOn(BackgroundPose bg)
        {
            yield return WaitForHudToClear();
            photo.SetPhotoMode(true);
            cam.transform.SetPositionAndRotation(bg.Position, bg.Rotation);
            yield return null;
            yield return null;

            _log.AppendLine($"{bg.Name}: {Describe(bg)}");

            float prevTs = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                yield return null;
                foreach (KeyValuePair<string, ShotGrade> g in RaisedGrades())
                {
                    channel.Raise(g.Value);      // the real handler, the real Format, the real colour choice
                    yield return null;
                    yield return CaptureVariants($"{bg.Name}__{g.Key}", "channel-raised", bg.Name, g.Key,
                        $"channel-raised {g.Key} over {bg.Name}", grade: g.Value.ToString());
                }
            }
            finally
            {
                Time.timeScale = prevTs;
            }

            _log.AppendLine();
        }

        // --- L.4: candidate reds and thicknesses (decision 1d) -------------------------------------------

        private static readonly KeyValuePair<string, Color>[] CandidateReds =
        {
            new KeyValuePair<string, Color>("salmon", new Color(1f, 0.55f, 0.45f)),
            new KeyValuePair<string, Color>("pure", new Color(1f, 0f, 0f)),
            new KeyValuePair<string, Color>("redB", new Color(1f, 0.25f, 0.20f)),
            new KeyValuePair<string, Color>("redC", new Color(1f, 0.30f, 0.24f)),
            new KeyValuePair<string, Color>("redE", new Color(1f, 0.33f, 0.28f)),
        };

        private static readonly float[] CandidateDistances = { 1f, 1.5f, 2f, 3f };
        private static readonly float[] CandidateOutlineAlphas = { 1f, 0.85f };

        /// <summary>
        /// Every candidate red, at every candidate thickness, on the brightest and the darkest background.
        ///
        /// ⚠️ A RIG-SIDE PROBE, AND LABELLED AS ONE. The thickness is written straight onto the live labels'
        /// Outline components and the red onto a throwaway copy of the shipped config; the shipped asset is
        /// never touched. The CHOSEN values are then put in the asset and re-photographed through the real
        /// Awake path, which is the run whose numbers are reported as AC4's.
        /// </summary>
        private IEnumerator CandidateSweep()
        {
            Section("PHASE L.4 — candidate reds x outline thickness (decision 1d; rig-side PROBES)");

            Text[] labels = Labels();
            var outlines = new Outline[labels.Length];
            var prevDist = new Vector2[labels.Length];
            var prevCol = new Color[labels.Length];
            bool any = false;
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] == null) continue;
                outlines[i] = labels[i].GetComponent<Outline>();
                if (outlines[i] == null) continue;
                any = true;
                prevDist[i] = outlines[i].effectDistance;
                prevCol[i] = outlines[i].effectColor;
            }

            if (!any)
            {
                _log.AppendLine("No Outline on the labels (the code before Story 1.19) — the sweep needs one. Skipped.");
                yield break;
            }

            GradeHudConfig shipped = GetHudConfig(hud);
            var probes = new List<BackgroundPose>();
            if (_chosenBackgrounds.Count > 0) probes.Add(_chosenBackgrounds[0]);
            if (_chosenBackgrounds.Count > 1) probes.Add(_chosenBackgrounds[_chosenBackgrounds.Count - 1]);

            try
            {
                foreach (BackgroundPose bg in probes)
                {
                    yield return WaitForHudToClear();
                    cam.transform.SetPositionAndRotation(bg.Position, bg.Rotation);
                    yield return null;
                    yield return null;

                    float prevTs = Time.timeScale;
                    Time.timeScale = 0f;
                    try
                    {
                        yield return null;
                        foreach (float alpha in CandidateOutlineAlphas)
                        {
                            foreach (float d in CandidateDistances)
                            {
                                // Alpha below 1 only at the middle thickness: it answers "does a softer
                                // outline cost contrast", not a full grid.
                                if (alpha < 1f && !Mathf.Approximately(d, 1.5f)) continue;

                                for (int i = 0; i < outlines.Length; i++)
                                {
                                    if (outlines[i] == null) continue;
                                    outlines[i].effectDistance = new Vector2(d, -d);
                                    Color c = prevCol[i];
                                    c.a = alpha;
                                    outlines[i].effectColor = c;
                                }

                                foreach (KeyValuePair<string, Color> red in CandidateReds)
                                {
                                    GradeHudConfig probeCfg = Instantiate(shipped);
                                    _tempConfigs.Add(probeCfg);
                                    probeCfg.missColor = red.Value;
                                    SetHudConfig(hud, probeCfg);

                                    channel.Raise(ShotGrade.Missed(GradeMiss.TooSmall, "TownDrunk"));
                                    yield return null;

                                    string probe = $"missColor {red.Key} ({red.Value.r:0.00}, {red.Value.g:0.00}, " +
                                                   $"{red.Value.b:0.00}); outline distance {d:0.0}, alpha {alpha:0.00}";
                                    yield return CaptureVariants(
                                        $"cand_{bg.Name}__{red.Key}__d{d:0.0}__a{alpha:0.00}",
                                        "channel-raised probe", bg.Name, "5_missed",
                                        "PROBE " + probe, probe: probe, grade: "Missed(TooSmall)");
                                }
                            }
                        }
                    }
                    finally
                    {
                        Time.timeScale = prevTs;
                        SetHudConfig(hud, shipped);
                    }
                }
            }
            finally
            {
                for (int i = 0; i < outlines.Length; i++)
                {
                    if (outlines[i] == null) continue;
                    outlines[i].effectDistance = prevDist[i];
                    outlines[i].effectColor = prevCol[i];
                }
                SetHudConfig(hud, shipped);
            }
        }

        // --- The four variants of one frozen frame -------------------------------------------------------

        private Text[] Labels()
        {
            var labels = new Text[LabelFields.Length];
            for (int i = 0; i < LabelFields.Length; i++) labels[i] = GetPrivate(hud, LabelFields[i]) as Text;
            return labels;
        }

        private float CurrentAlpha() =>
            hud.IsShowing ? (hudGroup != null ? hudGroup.alpha : 1f) : 0f;

        private static float[] Rgba(Color c) => new[] { c.r, c.g, c.b, c.a };

        private static Rect ScreenRectOf(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float xMin = Mathf.Min(corners[0].x, corners[2].x), xMax = Mathf.Max(corners[0].x, corners[2].x);
            float yMin = Mathf.Min(corners[0].y, corners[2].y), yMax = Mathf.Max(corners[0].y, corners[2].y);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        private IEnumerator CaptureVariants(string id, string source, string background, string scenario,
                                            string caption, string probe = null, string grade = null)
        {
            Text[] labels = Labels();
            int n = labels.Length;
            var outlines = new Outline[n];
            var prevColor = new Color[n];
            var prevEnabled = new bool[n];
            var prevOutColor = new Color[n];
            var prevOutEnabled = new bool[n];

            var set = new VariantSet
            {
                id = id, source = source, background = background, scenario = scenario, caption = caption,
                probe = probe ?? string.Empty,
                grade = grade ?? photo.LastGrade.ToString(),
                screenW = Screen.width, screenH = Screen.height,
                scaleFactor = hudCanvas.scaleFactor,
                at1080 = Screen.width == 1920 && Screen.height == 1080 && Mathf.Abs(hudCanvas.scaleFactor - 1f) < 0.001f,
                labels = new LabelInfo[n],
            };

            Rect panel = HudRect();
            set.panelRect = new[] { panel.x, panel.y, panel.width, panel.height };
            set.panelColor = hudPanel != null ? Rgba(hudPanel.color) : new float[0];

            for (int i = 0; i < n; i++)
            {
                Text l = labels[i];
                var info = new LabelInfo { field = LabelFields[i] };
                set.labels[i] = info;
                if (l == null) continue;

                prevColor[i] = l.color;
                prevEnabled[i] = l.enabled;
                outlines[i] = l.GetComponent<Outline>();
                if (outlines[i] != null)
                {
                    prevOutColor[i] = outlines[i].effectColor;
                    prevOutEnabled[i] = outlines[i].enabled;
                }

                Rect r = ScreenRectOf(l.rectTransform);
                info.text = l.text;
                info.color = Rgba(l.color);
                info.rect = new[] { r.x, r.y, r.width, r.height };
                info.hasOutline = outlines[i] != null && outlines[i].enabled;
                info.outlineColor = outlines[i] != null ? Rgba(outlines[i].effectColor) : new float[0];
                info.outlineDistance = outlines[i] != null
                    ? new[] { outlines[i].effectDistance.x, outlines[i].effectDistance.y }
                    : new float[0];
            }

            Texture2D a = null, m = null, p = null, nn = null, unfrozen = null;
            var alphas = new List<float>(4);
            float prevTs = Time.timeScale;

            try
            {
                // ⚠️ THE POSITIVE CONTROL FOR THE FREEZE CHECK. A freeze control that reads 0/255 on every set
                // is exactly the "every case identical" shape that means a broken rig as often as a working
                // one. So one frame is grabbed BEFORE the world is frozen and measured the same way against
                // A: where anything moves (the drunk walking, at a real-shutter state) it must read above 0,
                // proving the measure can see motion. Skipped when the caller already froze the world.
                if (prevTs > 0f) yield return Grab(t => unfrozen = t);

                Time.timeScale = 0f;
                yield return null;                         // one frozen frame before the first grab

                yield return Grab(t => a = t);
                alphas.Add(CurrentAlpha());

                for (int i = 0; i < n; i++)
                {
                    if (labels[i] == null) continue;
                    labels[i].color = Marker;
                    if (outlines[i] != null) { outlines[i].enabled = true; outlines[i].effectColor = OutlineMarker; }
                }
                yield return Grab(t => m = t);
                alphas.Add(CurrentAlpha());

                for (int i = 0; i < n; i++)
                {
                    if (labels[i] == null) continue;
                    labels[i].color = prevColor[i];
                    if (outlines[i] != null) outlines[i].effectColor = prevOutColor[i];
                    labels[i].enabled = false;
                }
                yield return Grab(t => p = t);
                alphas.Add(CurrentAlpha());

                for (int i = 0; i < n; i++)
                {
                    if (labels[i] == null) continue;
                    labels[i].enabled = prevEnabled[i];
                    if (outlines[i] != null) outlines[i].enabled = false;
                }
                yield return Grab(t => nn = t);
                alphas.Add(CurrentAlpha());
            }
            finally
            {
                // Every label property back exactly as it was, whatever happened above.
                for (int i = 0; i < n; i++)
                {
                    if (labels[i] == null) continue;
                    labels[i].color = prevColor[i];
                    labels[i].enabled = prevEnabled[i];
                    if (outlines[i] != null)
                    {
                        outlines[i].effectColor = prevOutColor[i];
                        outlines[i].enabled = prevOutEnabled[i];
                    }
                }
                Time.timeScale = prevTs;
            }

            try
            {
                set.alphaAtGrab = alphas.ToArray();
                set.heldFullAlpha = alphas.TrueForAll(x => x >= 0.999f);
                set.captureW = a != null ? a.width : 0;
                set.captureH = a != null ? a.height : 0;

                FreezeControl(a, p, panel, out set.freezeMaxDiff, out set.freezeFractionDiff);
                if (unfrozen != null)
                    FreezeControl(a, unfrozen, panel, out set.unfrozenMaxDiff, out set.unfrozenFractionDiff);
                else
                    set.unfrozenMaxDiff = -1;   // world already frozen by the caller: no control taken

                int x0 = Mathf.Clamp(Mathf.FloorToInt(panel.xMin) - CropMargin, 0, set.captureW - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(panel.yMin) - CropMargin, 0, set.captureH - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(panel.xMax) + CropMargin, x0 + 1, set.captureW);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(panel.yMax) + CropMargin, y0 + 1, set.captureH);
                set.crop = new[] { x0, y0, x1 - x0, y1 - y0 };

                set.fileA = SaveCrop(a, set.crop, id + "_A.png");
                set.fileM = SaveCrop(m, set.crop, id + "_M.png");
                set.fileP = SaveCrop(p, set.crop, id + "_P.png");
                set.fileN = SaveCrop(nn, set.crop, id + "_N.png");
                if (a != null)
                {
                    set.fileFull = id + "_full.jpg";
                    File.WriteAllBytes(Path.Combine(LegibilityDir, set.fileFull), a.EncodeToJPG(90));
                }

                File.WriteAllText(Path.Combine(LegibilityDir, id + ".json"), JsonUtility.ToJson(set, true));

                _log.AppendLine($"  {id}  [{source}]  alpha {string.Join("/", alphas.ConvertAll(x => x.ToString("0.00")))}" +
                                (set.heldFullAlpha ? "" : "  ⚠ CAUGHT THE FADE") +
                                $"   freeze max diff {set.freezeMaxDiff}/255 ({set.freezeFractionDiff:P3} px)" +
                                (set.freezeMaxDiff > 8 ? "  ⚠ THE WORLD MOVED" : "") +
                                (set.unfrozenMaxDiff >= 0
                                    ? $"   control: unfrozen frame differs by {set.unfrozenMaxDiff}/255 ({set.unfrozenFractionDiff:P3} px)"
                                    : "") +
                                $"   capture {set.captureW}x{set.captureH}");
                if (!string.IsNullOrEmpty(probe)) _log.AppendLine($"      probe: {probe}");
                for (int i = 0; i < n; i++)
                    if (set.labels[i].text != null)
                        _log.AppendLine($"      {i + 1}| {set.labels[i].text}");
            }
            finally
            {
                if (a != null) Destroy(a);
                if (m != null) Destroy(m);
                if (p != null) Destroy(p);
                if (nn != null) Destroy(nn);
                if (unfrozen != null) Destroy(unfrozen);
            }
        }

        private string SaveCrop(Texture2D src, int[] crop, string file)
        {
            if (src == null) return string.Empty;
            Color32[] px = src.GetPixels32();
            int w = crop[2], h = crop[3];
            var outPx = new Color32[w * h];
            for (int y = 0; y < h; y++)
                System.Array.Copy(px, (crop[1] + y) * src.width + crop[0], outPx, y * w, w);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(outPx);
                tex.Apply(false);
                File.WriteAllBytes(Path.Combine(LegibilityDir, file), tex.EncodeToPNG());
            }
            finally
            {
                Destroy(tex);
            }
            return file;
        }

        /// <summary>A against P outside the panel (and a 4 px skirt for the outline's overhang). Nothing but
        /// the labels changed between the two grabs, so any difference out there means the world moved and
        /// the four pictures are not of one frame.</summary>
        private static void FreezeControl(Texture2D a, Texture2D p, Rect panel, out int maxDiff, out float fraction)
        {
            maxDiff = 255;
            fraction = 1f;
            if (a == null || p == null || a.width != p.width || a.height != p.height) return;

            Color32[] pa = a.GetPixels32(), pp = p.GetPixels32();
            int px0 = Mathf.FloorToInt(panel.xMin) - 4, px1 = Mathf.CeilToInt(panel.xMax) + 4;
            int py0 = Mathf.FloorToInt(panel.yMin) - 4, py1 = Mathf.CeilToInt(panel.yMax) + 4;

            int worst = 0, over = 0, counted = 0;
            for (int y = 0; y < a.height; y++)
            {
                bool rowInside = y >= py0 && y < py1;
                for (int x = 0; x < a.width; x++)
                {
                    if (rowInside && x >= px0 && x < px1) continue;
                    int i = y * a.width + x;
                    int d = Mathf.Max(Mathf.Abs(pa[i].r - pp[i].r),
                                      Mathf.Max(Mathf.Abs(pa[i].g - pp[i].g), Mathf.Abs(pa[i].b - pp[i].b)));
                    if (d > worst) worst = d;
                    if (d > 2) over++;
                    counted++;
                }
            }

            maxDiff = worst;
            fraction = counted > 0 ? over / (float)counted : 1f;
        }

        // --- Luminance ----------------------------------------------------------------------------------

        private static float[] _srgbToLinear;

        private static float Linear8(byte v)
        {
            if (_srgbToLinear == null)
            {
                _srgbToLinear = new float[256];
                for (int i = 0; i < 256; i++)
                {
                    float c = i / 255f;
                    _srgbToLinear[i] = c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
                }
            }
            return _srgbToLinear[v];
        }

        /// <summary>WCAG relative luminance inside a screen rect: the mean, and the 95th percentile (the
        /// bright patch a mean hides).</summary>
        private static void LuminanceInside(Texture2D t, Rect r, out float mean, out float p95)
        {
            mean = 0f;
            p95 = 0f;
            if (t == null) return;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(r.xMin), 0, t.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(r.xMax), x0 + 1, t.width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(r.yMin), 0, t.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(r.yMax), y0 + 1, t.height);

            Color32[] px = t.GetPixels32();
            var hist = new int[1001];
            double sum = 0d;
            int count = 0;
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    Color32 c = px[y * t.width + x];
                    float l = 0.2126f * Linear8(c.r) + 0.7152f * Linear8(c.g) + 0.0722f * Linear8(c.b);
                    sum += l;
                    hist[Mathf.Clamp(Mathf.RoundToInt(l * 1000f), 0, 1000)]++;
                    count++;
                }
            }

            mean = count > 0 ? (float)(sum / count) : 0f;
            int target = Mathf.CeilToInt(count * 0.95f), run = 0;
            for (int i = 0; i <= 1000; i++)
            {
                run += hist[i];
                if (run >= target) { p95 = i / 1000f; break; }
            }
        }
    }
}
#endif
