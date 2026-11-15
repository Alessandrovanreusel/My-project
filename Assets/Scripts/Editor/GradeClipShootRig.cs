using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CameraGame.Events;
using CameraGame.Gallery;
using CameraGame.Grading;
using CameraGame.PhotoMode;
using CameraGame.Recording;
using CameraGame.UI;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 1.16's verification rig (Task 8).
    ///   - <c>Tools/Grading/Grade Clip Shoot (Play)</c> runs <see cref="GradeClipShootRunner"/> in the REAL SampleScene
    ///     (the wiring is part of what is under test) at the Game View's built-in 1920x1080 preset, and puts everything
    ///     back when play mode ends: the scene reopened from disk, the Game View size, RecordingConfig.maxClipSeconds.
    ///   - <c>Tools/Grading/Grade Clip Sweep</c> (edit mode, Phase W) replays a run's logged ticks through the REAL
    ///     <see cref="ClipGradeBuilder"/> under every candidate blend, and checks the shipped candidate against the live
    ///     grades. No re-implementation anywhere: the sweep and the game run the same code.
    ///
    /// SessionState keys, erased at the start of every run (loop lessons: a stale option silently runs a subset):
    /// "GradeClipShoot.Phases" (default "0SHEF") and "GradeClipShoot.Run" (the output sub-folder, default "rig").
    /// </summary>
    public static class GradeClipShootRig
    {
        public const string Root = "_bmad-output/verification/1-16-grade-a-clip";
        public const string PhasesKey = "GradeClipShoot.Phases";
        public const string RunKey = "GradeClipShoot.Run";
        public const string SweepDirKey = "GradeClipShoot.SweepDir";
        private const string ReturnSceneKey = "GradeClipShoot.ReturnScene";
        private const string PrevGameViewKey = "GradeClipShoot.PrevGameView";
        private const string PrevMaxClipKey = "GradeClipShoot.PrevMaxClip";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string RecordingConfigPath = "Assets/Data/Recording/RecordingConfig.asset";
        private const string GradingConfigPath = "Assets/Data/Grading/GradingConfig.asset";
        private const string HudConfigPath = "Assets/Data/UI/GradeHudConfig.asset";
        private const string CameraConfigPath = "Assets/Data/Camera/CameraConfig.asset";
        private const string ClipChannelPath = "Assets/Data/Channels/ClipRecordedChannel.asset";
        private const string ShotChannelPath = "Assets/Data/Channels/ShotCapturedChannel.asset";
        private const System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            string previous = SessionState.GetString(ReturnSceneKey, string.Empty);
            if (string.IsNullOrEmpty(previous)) return;
            SessionState.EraseString(ReturnSceneKey);
            RestoreGameView();

            string prevMax = SessionState.GetString(PrevMaxClipKey, string.Empty);
            SessionState.EraseString(PrevMaxClipKey);
            var cfg = AssetDatabase.LoadAssetAtPath<RecordingConfig>(RecordingConfigPath);
            if (cfg != null && float.TryParse(prevMax, NumberStyles.Float, CultureInfo.InvariantCulture, out float max)
                && !Mathf.Approximately(cfg.maxClipSeconds, max))
            {
                cfg.maxClipSeconds = max;
                EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssetIfDirty(cfg);
                Debug.LogWarning($"[GradeClipShoot] RecordingConfig.maxClipSeconds was left at a rig value; put back to {max}.");
            }

            if (File.Exists(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            Debug.Log($"[GradeClipShoot] Restored: reopened {previous} from disk, Game View size put back, maxClipSeconds " +
                      $"{(cfg != null ? cfg.maxClipSeconds.ToString(CultureInfo.InvariantCulture) : "?")}.");
        }

        [MenuItem("Tools/Grading/Grade Clip Shoot (Play)")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[GradeClipShoot] Already in play mode."); return; }
            string phases = SessionState.GetString(PhasesKey, "0SHEF");
            string run = SessionState.GetString(RunKey, "rig");
            SessionState.EraseString(PhasesKey);
            SessionState.EraseString(RunKey);

            Scene active = SceneManager.GetActiveScene();
            string previousScene = active.path;   // copied before any scene swap (loop lesson 133)
            if (string.IsNullOrEmpty(previousScene) || active.isDirty)
            {
                Debug.LogError("[GradeClipShoot] Save the open scene first: the rig reopens it from disk afterwards. Nothing was changed.");
                return;
            }
            foreach (string p in new[] { ScenePath, RecordingConfigPath, GradingConfigPath, HudConfigPath, CameraConfigPath, ClipChannelPath, ShotChannelPath })
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)))
                {
                    Debug.LogError($"[GradeClipShoot] Required asset missing: {p}. Nothing was changed.");
                    return;
                }

            if (active.path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetString(ReturnSceneKey, previousScene);

            var recorder = Object.FindAnyObjectByType<ClipRecorder>(FindObjectsInactive.Include);
            var photo = Object.FindAnyObjectByType<PhotoModeController>(FindObjectsInactive.Include);
            var hud = Object.FindAnyObjectByType<GradeHud>(FindObjectsInactive.Include);
            var manager = Object.FindAnyObjectByType<EventManager>(FindObjectsInactive.Include);
            var player = Object.FindAnyObjectByType<ThirdPersonController>(FindObjectsInactive.Include);
            var gallery = Object.FindAnyObjectByType<GalleryView>(FindObjectsInactive.Include);
            Camera cam = Camera.main;
            if (recorder == null || photo == null || hud == null || manager == null || player == null || cam == null || gallery == null)
            {
                Debug.LogError("[GradeClipShoot] SampleScene is missing the recorder, the controller, the grade HUD, the EventManager, " +
                               "the player, the gallery or Main Camera.");
                Abort(previousScene);
                return;
            }

            var recCfg = AssetDatabase.LoadAssetAtPath<RecordingConfig>(RecordingConfigPath);
            SessionState.SetString(PrevMaxClipKey, recCfg.maxClipSeconds.ToString("R", CultureInfo.InvariantCulture));
            SelectFullHdGameView();

            string output = Root + "/" + run;
            var go = new GameObject("GradeClipShootRunner");
            var r = go.AddComponent<GradeClipShootRunner>();
            r.photo = photo;
            r.recorder = recorder;
            r.hud = hud;
            r.manager = manager;
            r.cam = cam;
            r.player = player;
            r.gallery = gallery;
            r.tap = cam.GetComponent<ListenerTap>();
            r.channel = AssetDatabase.LoadAssetAtPath<ClipRecordedChannel>(ClipChannelPath);
            r.shotChannel = AssetDatabase.LoadAssetAtPath<ShotCapturedChannel>(ShotChannelPath);
            r.recordingConfig = recCfg;
            r.gradingConfig = AssetDatabase.LoadAssetAtPath<GradingConfig>(GradingConfigPath);
            r.hudConfig = AssetDatabase.LoadAssetAtPath<GradeHudConfig>(HudConfigPath);
            r.cameraConfig = AssetDatabase.LoadAssetAtPath<CameraConfig>(CameraConfigPath);
            r.outputDir = output;
            r.phases = phases;
            PhotoShootRig.ReadPlayerEye(out r.eyeHeight, out _, out _);

            Debug.Log($"[GradeClipShoot] Runner added (phases {phases}); entering play mode. Output: {output}. Recorder grading config: " +
                      $"{(recorder.GradingConfig != null ? recorder.GradingConfig.name : "NONE")}.");
            EditorApplication.EnterPlaymode();
        }

        private static void Abort(string previousScene)
        {
            SessionState.EraseString(ReturnSceneKey);
            SessionState.EraseString(PrevMaxClipKey);
            RestoreGameView();
            if (File.Exists(previousScene)) EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
        }

        // =====================================================================================================
        // Phase W — the sweep, through the real builder
        // =====================================================================================================

        private struct TickRow
        {
            public string Clip;
            public int Tick, Repeats, Samples;
            public ClipSample Sample;
        }

        [MenuItem("Tools/Grading/Grade Clip Sweep")]
        public static void Sweep()
        {
            string dir = SessionState.GetString(SweepDirKey, Root + "/rig");
            SessionState.EraseString(SweepDirKey);
            Debug.Log("[GradeClipSweep] " + SweepDir(dir));
        }

        /// <summary>Replays every clip in <paramref name="dir"/>/ticks.csv through a fresh ClipGradeBuilder per candidate,
        /// writes sweep.csv, and returns the live-versus-replay check for the shipped candidate.</summary>
        public static string SweepDir(string dir)
        {
            var inv = CultureInfo.InvariantCulture;
            string ticksPath = Path.Combine(dir, "ticks.csv"), clipsPath = Path.Combine(dir, "clips.csv");
            if (!File.Exists(ticksPath) || !File.Exists(clipsPath)) return "no ticks.csv / clips.csv in " + dir;

            // Ticks, grouped by clip in file order.
            var order = new List<string>();
            var byClip = new Dictionary<string, List<TickRow>>();
            foreach (string line in File.ReadAllLines(ticksPath))
            {
                if (line.StartsWith("clip,")) continue;
                string[] c = line.Split(',');
                var row = new TickRow { Clip = c[0], Tick = int.Parse(c[1], inv), Repeats = int.Parse(c[2], inv), Samples = int.Parse(c[3], inv) };
                if (row.Samples > 0)
                    row.Sample = new ClipSample
                    {
                        Lifecycle = int.Parse(c[4], inv), SubjectId = c[5], Counted = c[6] == "1",
                        Miss = (GradeMiss)System.Enum.Parse(typeof(GradeMiss), c[7]),
                        Composition01 = float.Parse(c[8], inv), ActorTime = float.Parse(c[9], inv),
                        PeakDuration = float.Parse(c[10], inv), AtPeak = c[11] == "1",
                    };
                if (!byClip.TryGetValue(row.Clip, out var list)) { list = new List<TickRow>(); byClip[row.Clip] = list; order.Add(row.Clip); }
                list.Add(row);
            }

            // Live grades.
            var live = new Dictionary<string, string[]>();
            foreach (string line in File.ReadAllLines(clipsPath))
            {
                if (line.StartsWith("clip,")) continue;
                string[] c = line.Split(',');
                live[c[0]] = c;
            }

            var shipped = AssetDatabase.LoadAssetAtPath<GradingConfig>(GradingConfigPath);
            var hudCfg = AssetDatabase.LoadAssetAtPath<GradeHudConfig>(HudConfigPath);
            StarScale scale = shipped.SafeStarScale;

            var sb = new StringBuilder("candidate,shape,framing,presence,floor,absence,missRule,clip,percent,stars,miss,coverage,framing01,inShot,printed,colour\n");
            float[] floors = { 0.15f, 0.2f, 0.25f, 0.3f, 0.35f, 0.4f, 0.5f };
            float[] absences = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            var builder = new ClipGradeBuilder();
            int candidates = 0;
            foreach (ClipBlendShape shape in new[] { ClipBlendShape.FloorTimesCoverage, ClipBlendShape.Additive })
            foreach (ClipFramingSource source in new[] { ClipFramingSource.GatedTicks, ClipFramingSource.PeakTicksWhenCovered })
            foreach (ClipPresence presence in new[] { ClipPresence.AnySubject, ClipPresence.Lifecycle })
            foreach (float floor in floors)
            foreach (float absence in absences)
            foreach (ClipMissRule rule in new[] { ClipMissRule.MostTime, ClipMissRule.DeepestGate })
            {
                // The miss rule only matters for clips that never counted; sweep it on one shape/source/floor/absence only.
                if (rule == ClipMissRule.DeepestGate && !(shape == ClipBlendShape.FloorTimesCoverage && source == ClipFramingSource.GatedTicks
                                                          && presence == ClipPresence.AnySubject
                                                          && Mathf.Approximately(floor, 0.3f) && absence == 0f)) continue;
                var blend = new ClipBlend(floor, absence, shape, source, rule, presence);
                string name = $"{shape}/{source}/{presence}/f{floor.ToString("0.00", inv)}/a{absence.ToString("0.00", inv)}/{rule}";
                candidates++;
                foreach (string clip in order)
                {
                    ClipGrade g = Replay(builder, byClip[clip], blend, scale);
                    Color col = hudCfg.ColorFor(g.Counted, g.IsMiss, g.Stars, g.Percent01);
                    string printed = g.Counted ? GradeText.Percent(g.Percent01) : g.IsMiss ? "MISSED" : "NOT GRADED";
                    sb.Append(name).Append(',').Append(shape).Append(',').Append(source).Append(',').Append(presence).Append(',').Append(floor.ToString("R", inv)).Append(',')
                      .Append(absence.ToString("R", inv)).Append(',').Append(rule).Append(',').Append(clip).Append(',')
                      .Append(g.Percent01.ToString("R", inv)).Append(',').Append(g.Stars).Append(',').Append(g.MissReason).Append(',')
                      .Append(g.PeakCoverage01.ToString("R", inv)).Append(',').Append(g.Framing01.ToString("R", inv)).Append(',')
                      .Append(g.InShot01.ToString("R", inv)).Append(',').Append(printed).Append(',')
                      .Append($"{Mathf.RoundToInt(col.r * 255)} {Mathf.RoundToInt(col.g * 255)} {Mathf.RoundToInt(col.b * 255)}").Append('\n');
                }
            }
            File.WriteAllText(Path.Combine(dir, "sweep.csv"), sb.ToString());

            // The shipped candidate against the live grades: bit-equal percent, equal stars and reason, equal seconds.
            var check = new StringBuilder();
            int same = 0, diff = 0;
            ClipBlend sb0 = shipped.SafeClipBlend;
            foreach (string clip in order)
            {
                ClipGrade g = Replay(builder, byClip[clip], sb0, scale);
                if (!live.TryGetValue(clip, out string[] l)) { check.Append($"\n   {clip}: no live row"); diff++; continue; }
                bool eq = l[7] == g.Percent01.ToString("R", inv) && l[8] == g.Stars.ToString(inv) && l[9] == g.MissReason.ToString()
                          && l[12] == g.PeakCoverage01.ToString("R", inv) && l[5] == g.GradedSeconds.ToString("R", inv);
                if (eq) same++; else diff++;
                if (!eq) check.Append($"\n   {clip}: live {l[7]} {l[8]}★ {l[9]} cov {l[12]} secs {l[5]} | replay {g.Percent01.ToString("R", inv)} {g.Stars}★ {g.MissReason} cov {g.PeakCoverage01.ToString("R", inv)} secs {g.GradedSeconds.ToString("R", inv)}");
            }
            string summary = $"sweep: {candidates} candidates x {order.Count} clips -> {dir}/sweep.csv. Shipped blend (floor {sb0.Floor.ToString(inv)}, absence " +
                             $"{sb0.AbsenceWeight.ToString(inv)}, {sb0.Shape}) replayed against the live grades: {same} identical, {diff} different.{check}";
            File.WriteAllText(Path.Combine(dir, "sweep-check.txt"), summary + "\n");
            return summary;
        }

        private static ClipGrade Replay(ClipGradeBuilder b, List<TickRow> rows, ClipBlend blend, StarScale scale)
        {
            b.Begin();
            int open = int.MinValue;
            foreach (TickRow r in rows)
            {
                if (r.Tick != open)
                {
                    if (open != int.MinValue) b.EndTick();
                    b.BeginTick(r.Repeats / (double)FootageFormat.TickRate);
                    open = r.Tick;
                }
                if (r.Samples > 0) { ClipSample s = r.Sample; b.Add(in s); }
            }
            if (open != int.MinValue) b.EndTick();
            return b.Finish(in blend, scale);
        }

        // --- Game View size: the built-in 1920x1080 preset by reflection, put back afterwards (loop lessons, 1-19) ---

        private static System.Type GameViewType => typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        private static Object GameViewWindow()
        {
            Object[] all = Resources.FindObjectsOfTypeAll(GameViewType);
            return all.Length > 0 ? all[0] : null;
        }

        private static int FullHdIndex()
        {
            System.Type sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
            object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null, null);
            object group = sizesType.GetProperty("currentGroup").GetValue(sizes, null);
            System.Type g = group.GetType();
            int total = (int)g.GetMethod("GetTotalCount").Invoke(group, null);
            for (int i = 0; i < total; i++)
            {
                object s = g.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                System.Type st = s.GetType();
                if (st.GetProperty("sizeType").GetValue(s, null).ToString() == "FixedResolution"
                    && (int)st.GetProperty("width").GetValue(s, null) == 1920
                    && (int)st.GetProperty("height").GetValue(s, null) == 1080)
                    return i;
            }
            return -1;
        }

        private static void SelectFullHdGameView()
        {
            try
            {
                Object gv = GameViewWindow();
                int target = FullHdIndex();
                if (gv == null || target < 0) { Debug.Log("[GradeClipShoot] No Game View or no 1920x1080 preset; the report says the size."); return; }
                string pending = SessionState.GetString(PrevGameViewKey, string.Empty);
                int previous = string.IsNullOrEmpty(pending)
                    ? (int)GameViewType.GetProperty("selectedSizeIndex", Any).GetValue(gv, null)
                    : int.Parse(pending);
                SessionState.SetString(PrevGameViewKey, previous.ToString());
                GameViewType.GetMethod("SizeSelectionCallback", Any).Invoke(gv, new object[] { target, null });
            }
            catch (System.Exception e)
            {
                Debug.Log($"[GradeClipShoot] Could not select 1920x1080 ({e.GetType().Name}); the report says the size.");
            }
        }

        private static void RestoreGameView()
        {
            string stored = SessionState.GetString(PrevGameViewKey, string.Empty);
            if (string.IsNullOrEmpty(stored)) return;
            SessionState.EraseString(PrevGameViewKey);
            try
            {
                Object gv = GameViewWindow();
                if (gv != null) GameViewType.GetMethod("SizeSelectionCallback", Any).Invoke(gv, new object[] { int.Parse(stored), null });
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GradeClipShoot] Could not put the Game View size back ({e.Message}).");
            }
        }
    }
}
