using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CameraGame.Events;
using CameraGame.PhotoMode;
using CameraGame.Recording;
using CameraGame.UI;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 1.15's verification rig (Task 10): <c>Tools/Recording/Record Clip Shoot (Play)</c>. Runs
    /// <see cref="RecordClipShootRunner"/> in the REAL SampleScene (the scene wiring is part of what is under
    /// test) at the Game View's built-in 1920x1080 preset, and puts everything back when play mode ends: the
    /// scene is reopened from disk (the runner was added in edit mode), the Game View size is restored, and
    /// the RecordingConfig asset is read back and reset if a run died before restoring it.
    ///
    /// Run a subset by setting the SessionState string "RecordClipShoot.Phases" (e.g. "0A") before the menu;
    /// it is erased at the start of every run so a later full run cannot silently run a subset (loop lessons).
    /// </summary>
    public static class RecordClipShootRig
    {
        public const string OutputDir = "_bmad-output/verification/1-15-record-a-clip/rig";
        public const string PhasesKey = "RecordClipShoot.Phases";
        private const string ReturnSceneKey = "RecordClipShoot.ReturnScene";
        private const string PrevGameViewKey = "RecordClipShoot.PrevGameView";
        private const string PrevMaxClipKey = "RecordClipShoot.PrevMaxClip";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ConfigPath = "Assets/Data/Recording/RecordingConfig.asset";
        private const string ChannelPath = "Assets/Data/Channels/ClipRecordedChannel.asset";
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
            var cfg = AssetDatabase.LoadAssetAtPath<RecordingConfig>(ConfigPath);
            if (cfg != null && float.TryParse(prevMax, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float max) && !Mathf.Approximately(cfg.maxClipSeconds, max))
            {
                cfg.maxClipSeconds = max;
                EditorUtility.SetDirty(cfg);
                AssetDatabase.SaveAssetIfDirty(cfg);
                Debug.LogWarning($"[RecordClipShoot] RecordingConfig.maxClipSeconds was left at a rig value; put back to {max}.");
            }

            if (File.Exists(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            Debug.Log($"[RecordClipShoot] Restored: reopened {previous} from disk, Game View size and config read back " +
                      $"(maxClipSeconds {(cfg != null ? cfg.maxClipSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?")}).");
        }

        [MenuItem("Tools/Recording/Record Clip Shoot (Play)")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[RecordClipShoot] Already in play mode."); return; }
            string phases = SessionState.GetString(PhasesKey, "0ABCDEF");
            SessionState.EraseString(PhasesKey);

            Scene active = SceneManager.GetActiveScene();
            string previousScene = active.path;   // copied before any scene swap (loop lessons)
            if (string.IsNullOrEmpty(previousScene) || active.isDirty)
            {
                Debug.LogError("[RecordClipShoot] Save the open scene first: the rig reopens it from disk afterwards. Nothing was changed.");
                return;
            }
            foreach (string p in new[] { ScenePath, ConfigPath, ChannelPath })
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)))
                {
                    Debug.LogError($"[RecordClipShoot] Required asset missing: {p}. Nothing was changed.");
                    return;
                }

            if (active.path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            SessionState.SetString(ReturnSceneKey, previousScene);

            var recorder = Object.FindAnyObjectByType<ClipRecorder>(FindObjectsInactive.Include);
            var photo = Object.FindAnyObjectByType<PhotoModeController>(FindObjectsInactive.Include);
            var hud = Object.FindAnyObjectByType<RecordingHud>(FindObjectsInactive.Include);
            var manager = Object.FindAnyObjectByType<EventManager>(FindObjectsInactive.Include);
            var player = Object.FindAnyObjectByType<ThirdPersonController>(FindObjectsInactive.Include);
            Camera cam = Camera.main;
            if (recorder == null || photo == null || hud == null || manager == null || player == null || cam == null)
            {
                Debug.LogError("[RecordClipShoot] SampleScene is missing the recorder, the controller, the REC HUD, the " +
                               "EventManager, the player or Main Camera. Nothing to film.");
                Abort(previousScene);
                return;
            }

            var cfg = AssetDatabase.LoadAssetAtPath<RecordingConfig>(ConfigPath);
            SessionState.SetString(PrevMaxClipKey, cfg.maxClipSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            SelectFullHdGameView();

            var go = new GameObject("RecordClipShootRunner");
            var runner = go.AddComponent<RecordClipShootRunner>();
            runner.photo = photo;
            runner.recorder = recorder;
            runner.hud = hud;
            runner.manager = manager;
            runner.cam = cam;
            runner.player = player;
            runner.channel = AssetDatabase.LoadAssetAtPath<ClipRecordedChannel>(ChannelPath);
            runner.shippedConfig = cfg;
            runner.actorPrefab = new SerializedObject(manager).FindProperty("actorPrefab").objectReferenceValue as EventActor;
            runner.outputDir = OutputDir;
            runner.phases = phases;
            PhotoShootRig.ReadPlayerEye(out runner.eyeHeight, out _, out _);

            Debug.Log($"[RecordClipShoot] Runner added (phases {phases}); entering play mode. Output: {OutputDir}");
            EditorApplication.EnterPlaymode();
        }

        private static void Abort(string previousScene)
        {
            SessionState.EraseString(ReturnSceneKey);
            SessionState.EraseString(PrevMaxClipKey);
            RestoreGameView();
            if (File.Exists(previousScene)) EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
        }

        // --- Game View size: the built-in 1920x1080 preset, selected by reflection and put back (loop lessons, 1-19) ---

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
                if (gv == null || target < 0) { Debug.Log("[RecordClipShoot] No Game View or no 1920x1080 preset; the report says the size."); return; }
                string pending = SessionState.GetString(PrevGameViewKey, string.Empty);
                int previous = string.IsNullOrEmpty(pending)
                    ? (int)GameViewType.GetProperty("selectedSizeIndex", Any).GetValue(gv, null)
                    : int.Parse(pending);
                SessionState.SetString(PrevGameViewKey, previous.ToString());
                GameViewType.GetMethod("SizeSelectionCallback", Any).Invoke(gv, new object[] { target, null });
            }
            catch (System.Exception e)
            {
                Debug.Log($"[RecordClipShoot] Could not select 1920x1080 ({e.GetType().Name}); the report says the size.");
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
                Debug.LogWarning($"[RecordClipShoot] Could not put the Game View size back ({e.Message}).");
            }
        }
    }
}
