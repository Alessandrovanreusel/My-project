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

        [MenuItem("Tools/Audio/Create GameAudio Mixer")]
        public static void Create()
        {
            Report.Clear();
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
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, Report.ToString());
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

            object controller = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (controller != null)
            {
                Log($"Mixer already exists at {MixerPath}; ensuring the {SfxGroupName} group is present.");
            }
            else
            {
                MethodInfo create = controllerType.GetMethod(
                    "CreateMixerControllerAtPath", BindingFlags.Public | BindingFlags.Static);
                if (create == null)
                {
                    DumpApi(controllerType, "CreateMixerControllerAtPath not found");
                    return;
                }
                controller = create.Invoke(null, new object[] { MixerPath });
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

            EditorUtility.SetDirty((UnityEngine.Object)controller);
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
        private static void EnsureSfxGroup(Type controllerType, object controller)
        {
            var mixer = (AudioMixer)controller;
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
                DumpApi(controllerType, "group-creation API not found");
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

        /// <summary>Logs the reflected surface so a future Unity version change is diagnosable, not mysterious.</summary>
        private static void DumpApi(Type t, string why)
        {
            Log($"{why}. Candidate members on {t.FullName}:");
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                                      .Where(m => m.Name.Contains("Group") || m.Name.Contains("Snapshot"))
                                      .OrderBy(m => m.Name))
                Log("  " + m.ReturnType.Name + " " + Sig(m));
        }

        private static void Log(string line) => Report.AppendLine(line);
    }
}
