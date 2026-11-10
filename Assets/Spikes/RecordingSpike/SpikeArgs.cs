// THROWAWAY SPIKE CODE (Story 1.14). Never referenced by game code. See README.md in this folder.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace CameraGame.Spikes.RecordingSpike
{
    /// <summary>
    /// The spike's whole configuration, read from the command line in a build and from a SessionState
    /// string in the Editor. There are no assets and no scene edits: the harness arms itself only when
    /// one of those two sources asks it to, so a normal play session or a normal build never sees it.
    /// </summary>
    public sealed class SpikeArgs
    {
        public const string SessionKey = "CameraGame.RecordingSpike.Args";
        // When the editor menu armed the flag (EditorApplication.timeSinceStartup). A flag older than this window
        // is STALE - e.g. Play() wrote it but EnterPlaymode was refused (compile errors) - and is discarded, so it
        // can never hijack a normal Play press (code review 2026-11-10 reproduced exactly that).
        public const string ArmedAtKey = "CameraGame.RecordingSpike.ArmedAt";
        public const double ArmWindowSeconds = 120;

        public string Mode = "";          // baseline | A | B | C | survey | control | failsoft
        public string OutDir = "";
        public string Tag = "run";        // file prefix, e.g. measure-A-run1
        public string FfmpegPath = "C:/ProgramData/chocolatey/lib/ffmpeg/tools/ffmpeg/bin/ffmpeg.exe";
        public string Encoder = "h264_mf";
        public string Transport = "tcp";  // tcp | pipe
        public int FpsCap = -1;           // sensitivity only: Application.targetFrameRate (the game ships uncapped)
        public int Width = 1920, Height = 1080, Fps = 30;
        public int Cycles = 1;            // record (+ play) cycles; cycle 1 is the measured one
        public bool Prove;                // evidence run: play the clip back full-window after recording
        public bool Play = true;          // measure the in-game playback segment
        // The 2026-11-10 control shot proved AsyncGPUReadback rows are bottom-up (Texture2D order), so every
        // ffmpeg encode of tap frames needs vflip. On by default; -novflip exists only to re-prove it.
        public bool VFlip = true;
        public bool Export = true;
        public bool EditExport;
        public bool Solo;                 // (B) full-screen playback with the live camera off: ONE town render           // (B) Edit Suite probe: trim clip 1, join clip 2, export ONE file
        public bool Fidelity;             // review 2026-11-10: per-tick ghost-vs-live bone drift over the whole take (B only)
        public float Timeout = 240f;
        public Vector3 Vantage = new Vector3(float.NaN, 0, 0);
        public float Zoom = 0f;
        public string FailCase = "";      // failsoft: badpath | kill | ring
        public readonly List<string> Raw = new List<string>();

        public bool Armed => !string.IsNullOrEmpty(Mode);
        public bool IsCandidate => Mode == "A" || Mode == "B" || Mode == "C";

        public static SpikeArgs Read()
        {
            var a = new SpikeArgs();
            string[] argv = Environment.GetCommandLineArgs();
#if UNITY_EDITOR
            string s = UnityEditor.SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(s)) return a; // never arm from the editor's own command line
            double armedAt = UnityEditor.SessionState.GetFloat(ArmedAtKey, -1f);
            double age = UnityEditor.EditorApplication.timeSinceStartup - armedAt;
            if (armedAt < 0 || age < 0 || age > ArmWindowSeconds)
            {
                UnityEditor.SessionState.EraseString(SessionKey);
                UnityEditor.SessionState.EraseFloat(ArmedAtKey);
                Debug.Log($"[RecordingSpike] discarded a stale arming flag (armed {(armedAt < 0 ? "never" : age.ToString("0") + " s ago")}); this play session runs normally.");
                return a;
            }
            argv = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
#endif
            for (int i = 0; i < argv.Length; i++)
            {
                string k = argv[i];
                string v = i + 1 < argv.Length ? argv[i + 1] : "";
                switch (k)
                {
                    case "-recordingSpike": a.Mode = v; i++; break;
                    case "-spikeOut": a.OutDir = v.Replace('|', ' '); i++; break;
                    case "-spikeTag": a.Tag = v; i++; break;
                    case "-ffmpeg": a.FfmpegPath = v.Replace('|', ' '); i++; break;
                    case "-encoder": a.Encoder = v; i++; break;
                    case "-transport": a.Transport = v; i++; break;
                    case "-fpscap": a.FpsCap = int.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "-res": if (v == "720") { a.Width = 1280; a.Height = 720; } i++; break;
                    case "-cycles": a.Cycles = Mathf.Max(1, int.Parse(v, CultureInfo.InvariantCulture)); i++; break;
                    case "-prove": a.Prove = true; break;
                    case "-noplay": a.Play = false; break;
                    case "-vflip": a.VFlip = true; break;
                    case "-novflip": a.VFlip = false; break;
                    case "-noexport": a.Export = false; break;
                    case "-editexport": a.EditExport = true; break;
                    case "-solo": a.Solo = true; break;
                    case "-fidelity": a.Fidelity = true; break;
                    case "-timeout": a.Timeout = float.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "-zoom": a.Zoom = float.Parse(v, CultureInfo.InvariantCulture); i++; break;
                    case "-failcase": a.FailCase = v; i++; break;
                    case "-vantage":
                        var p = v.Split(',');
                        a.Vantage = new Vector3(F(p[0]), F(p[1]), F(p[2])); i++; break;
                }
            }
            if (a.Armed && string.IsNullOrEmpty(a.OutDir))
                a.OutDir = Path.Combine(Application.persistentDataPath, "recording-spike");
            a.Raw.AddRange(argv);
            return a;
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}
