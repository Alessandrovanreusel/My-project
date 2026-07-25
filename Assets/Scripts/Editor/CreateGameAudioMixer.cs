using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// One-shot helper (Story 1.8) that creates <c>Assets/Audio/Mixers/GameAudio.mixer</c> with a single
    /// SFX group, per the architecture's ADR row 8 ("3D AudioSource per actor, ~25 m rolloff, 1 mixer").
    ///
    /// Why a tool instead of just clicking Assets > Create > Audio > Audio Mixer: the built-in menu item
    /// only commits the file once the Project window's inline rename ends, so it creates nothing when
    /// driven headlessly. Unity's own construction path is
    /// <c>UnityEditor.Audio.AudioMixerController.CreateMixerControllerAtPath</c>, which is internal —
    /// hence the reflection. Using Unity's factory (rather than hand-writing the .mixer YAML) guarantees
    /// the master group, snapshot, and attenuation effect are wired the way the Audio Mixer window expects.
    ///
    /// It writes a report to disk rather than the console because MCP's read_console does not surface
    /// Debug.Log, which would leave this running blind.
    /// </summary>
    public static class CreateGameAudioMixer
    {
        private const string MixerPath = "Assets/Audio/Mixers/GameAudio.mixer";
        private const string SfxGroupName = "SFX";
        private const string ReportPath = "Temp/game_audio_mixer_report.txt";

        private static readonly StringBuilder Report = new StringBuilder();

        /// <summary>Set by any Log line starting with FAIL/EXCEPTION, so Create() can surface the outcome.</summary>
        private static bool _failed;

        [MenuItem("Tools/Audio/Create GameAudio Mixer")]
        public static void Create()
        {
            Report.Clear();
            _failed = false;
            try
            {
                Run();
            }
            catch (Exception e)
            {
                Log("EXCEPTION: " + e);
            }
            finally
            {
                // The report file is a debugging convenience, NOT the result channel. It used to be the only
                // output, which meant every failure path — a missing internal API, a null controller, a
                // Unity-version change — looked exactly like "the menu item does nothing". Worse, Temp/ is
                // cleared when the project reopens, so the evidence could be gone before anyone looked.
                // The console now always gets a verdict; the file keeps the detail.
                try
                {
                    string dir = Path.GetDirectoryName(ReportPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(ReportPath, Report.ToString());
                }
                catch (Exception e)
                {
                    // Never let the report write swallow the diagnosis it exists to preserve.
                    Debug.LogWarning($"[Audio] Could not write {ReportPath}: {e.Message}\n{Report}");
                }

                if (_failed)
                    Debug.LogError($"[Audio] Create GameAudio Mixer FAILED. Details in {ReportPath}:\n{Report}");
                else
                    Debug.Log($"[Audio] GameAudio mixer ready at {MixerPath}. Details in {ReportPath}.");
            }
        }

        private static void Run()
        {
            Type controllerType = typeof(EditorApplication).Assembly.GetType("UnityEditor.Audio.AudioMixerController");
            if (controllerType == null)
            {
                Log("FAIL: UnityEditor.Audio.AudioMixerController not found.");
                return;
            }

            // Typed as AudioMixer, NOT object. UnityEngine.Object overloads == / != so that a destroyed or
            // native-side-null object still compares equal to null. Through an `object` reference those
            // operators are bypassed and every null check below silently degrades into a plain reference
            // comparison — which would let a half-created mixer walk straight past the explicit FAIL branch
            // and into a NullReferenceException inside EnsureSfxGroup.
            var controller = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (controller != null)
            {
                Log($"Mixer already exists at {MixerPath}; ensuring the {SfxGroupName} group is present.");
            }
            else
            {
                MethodInfo create = controllerType.GetMethod(
                    "CreateMixerControllerAtPath",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (create == null)
                {
                    DumpApi(controllerType, "CreateMixerControllerAtPath not found", "Mixer");
                    return;
                }

                // Unity's factory will not create the folder for us, and a fresh clone that has the committed
                // .mixer deleted has no Assets/Audio/Mixers/ either.
                string folder = Path.GetDirectoryName(MixerPath).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    Directory.CreateDirectory(folder);
                    AssetDatabase.Refresh();
                    Log($"Created missing folder {folder}.");
                }

                controller = create.Invoke(null, new object[] { MixerPath }) as AudioMixer;
                Log($"Created mixer at {MixerPath}.");
            }

            if (controller == null)
            {
                Log("FAIL: controller is null after create/load.");
                return;
            }

            EnsureSfxGroup(controllerType, controller);

            // The group "views" drive the Audio Mixer WINDOW's layout, not playback. Building the mixer
            // through the factory rather than the window leaves that list empty, so ask Unity to repair it —
            // otherwise the first person to open Window > Audio > Audio Mixer meets an empty panel.
            MethodInfo sanitize = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "SanitizeGroupViews" && m.GetParameters().Length == 0);
            if (sanitize != null)
            {
                sanitize.Invoke(controller, null);
                Log("Called SanitizeGroupViews().");
            }
            else
            {
                Log("NOTE: SanitizeGroupViews() not found — mixer window may need a manual group-view fix.");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                Log("FAIL: mixer did not reload after save.");
                return;
            }

            AudioMixerGroup[] all = mixer.FindMatchingGroups(string.Empty);
            Log("Groups now on the mixer: " + string.Join(", ", all.Select(g => g.name)));
            Log(all.Any(g => g.name == SfxGroupName)
                ? "OK: SFX group present."
                : "FAIL: SFX group missing.");
        }

        /// <summary>Adds the single SFX group under Master, mirroring what the Audio Mixer window does.</summary>
        private static void EnsureSfxGroup(Type controllerType, AudioMixer controller)
        {
            AudioMixer mixer = controller;
            if (mixer.FindMatchingGroups(string.Empty).Any(g => g.name == SfxGroupName))
            {
                Log("SFX group already present — skipping creation.");
                return;
            }

            MethodInfo createGroup = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CreateNewGroup");
            MethodInfo addChild = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "AddChildToParent");
            MethodInfo addToView = controllerType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "AddGroupToCurrentView");

            Log($"CreateNewGroup={Sig(createGroup)}  AddChildToParent={Sig(addChild)}  AddGroupToCurrentView={Sig(addToView)}");

            if (createGroup == null || addChild == null)
            {
                DumpApi(controllerType, "group-creation API not found", "Group");
                return;
            }

            object group = Invoke(createGroup, controller, SfxGroupName);
            if (group == null)
            {
                Log("FAIL: CreateNewGroup returned null.");
                return;
            }

            // The group must live inside the .mixer file as a sub-asset, like Master and the snapshot.
            // Do this BEFORE parenting so the reference the parent stores already points at a saved object.
            AssetDatabase.AddObjectToAsset((UnityEngine.Object)group, (UnityEngine.Object)controller);

            object master = controllerType
                .GetProperty("masterGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(controller);
            Log("masterGroup resolved: " + (master != null));

            // Bail rather than parent to nothing. AddObjectToAsset has ALREADY run, so continuing with a null
            // parent leaves an SFX group inside the .mixer file that routes nowhere — a mixer that looks
            // created and is quietly broken, which is worse than one that visibly failed.
            if (master == null)
            {
                Log("FAIL: masterGroup could not be resolved — refusing to parent the SFX group to nothing.");
                return;
            }

            Invoke(addChild, controller, group, master);
            if (addToView != null) Invoke(addToView, controller, group);

            Log("SFX group created and parented under Master.");
        }

        /// <summary>Invokes a reflected method, padding trailing optional/bool args (e.g. storeUndoState).</summary>
        private static object Invoke(MethodInfo m, object target, params object[] args)
        {
            ParameterInfo[] ps = m.GetParameters();
            var full = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                if (i < args.Length) { full[i] = args[i]; continue; }
                full[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                        : ps[i].ParameterType == typeof(bool) ? (object)false
                        : null;
            }
            Log($"  invoking {m.Name}({string.Join(", ", full.Select(a => a?.ToString() ?? "null"))})");
            return m.Invoke(target, full);
        }

        private static string Sig(MethodInfo m) =>
            m == null ? "<null>" : m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ")";

        /// <summary>
        /// Logs the reflected surface so a future Unity version change is diagnosable, not mysterious.
        /// <paramref name="nameFilter"/> must match the members the CALLER was looking for — a fixed
        /// "Group"/"Snapshot" filter used to exclude CreateMixerControllerAtPath, i.e. the one method whose
        /// disappearance this dump exists to explain, leaving a header and an empty list.
        /// </summary>
        private static void DumpApi(Type t, string why, string nameFilter)
        {
            Log($"FAIL: {why}. Candidate members matching '{nameFilter}' on {t.FullName}:");
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                                      .Where(m => m.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                                      .OrderBy(m => m.Name))
                Log("  " + m.ReturnType.Name + " " + Sig(m));
        }

        private static void Log(string line)
        {
            if (line.StartsWith("FAIL") || line.StartsWith("EXCEPTION")) _failed = true;
            Report.AppendLine(line);
        }
    }
}
