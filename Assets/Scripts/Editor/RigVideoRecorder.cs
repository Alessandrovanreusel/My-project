using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using CameraGame.Core;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Films a verification rig while it runs, and drops an MP4 beside its stills.
    ///
    /// ================================================================================================
    /// WHY THIS EXISTS
    /// ================================================================================================
    /// The photo-shoot and gallery rigs answer questions about single frames extremely well — is the box
    /// on him, is the grade right, did the thumbnail follow the window. They cannot answer a question
    /// about MOTION, and CLAUDE.md names exactly those as the ones needing a human: whether movement
    /// feels right, whether the drunk's walk cycle reads, whether the shutter was pulled at the peak. A
    /// still cannot show a peak being missed by a third of a second. A clip can.
    ///
    /// This is a supplement to the photographs, never a replacement. If recording fails the shoot must
    /// still produce its stills, so every failure path here is caught and logged, and none of them
    /// propagate into the rig.
    ///
    /// ================================================================================================
    /// TWO THINGS THAT WOULD SILENTLY CORRUPT A RUN, AND ARE DELIBERATELY TURNED OFF
    /// ================================================================================================
    /// 1. <c>FrameRatePlayback.Constant</c> — the Recorder DEFAULT — drives <c>Time.captureDeltaTime</c>
    ///    to pin the game clock to a fixed step. The gallery rig's Phase E measures the real cost of the
    ///    shutter in milliseconds, and the drunk's lifecycle is timed in seconds; recording under a forced
    ///    clock would change the very numbers the rig exists to report, and it would do it invisibly —
    ///    the run would look normal and the figures would be fiction. <see cref="FrameRatePlayback.Variable"/>
    ///    leaves the clock alone. <c>CapFrameRate</c> is off for the same reason: throttling to hit a
    ///    target rate would slow the rig it is measuring.
    ///
    /// 2. <c>RecorderControllerSettings.ExitPlayMode</c> defaults to TRUE, which means stopping the
    ///    recorder would ALSO leave play mode. Both rigs stop play mode themselves after restoring the
    ///    scene; a second actor doing it would race them and could tear the world down mid-restore,
    ///    stranding the editor in the test world — the exact outcome CLAUDE.md says must never happen.
    ///
    /// Both are verified rather than assumed: the recorder logs Time.captureDeltaTime at stop, and it
    /// should read 0.
    /// </summary>
    [InitializeOnLoad]
    public static class RigVideoRecorder
    {
        private static RecorderController _controller;
        private static RecorderControllerSettings _settings;
        private static MovieRecorderSettings _movie;

        /// <summary>The feed generation currently being filmed, or -1 when idle. Comparing generations
        /// rather than texture references is what makes a second run start a second clip.</summary>
        private static int _filmingGeneration = -1;

        private static string _clipPath;

        static RigVideoRecorder()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>
        /// Watches the feed. Starting is driven by the feed appearing rather than by the rig calling in,
        /// so the runners stay entirely ignorant of Recorder — see <see cref="RigVideoFeed"/> for why they
        /// cannot call it directly even if they wanted to.
        /// </summary>
        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                // Nothing to film outside play mode, and a controller left running here would hold a
                // texture that no longer exists.
                if (_controller != null) Stop("left play mode");
                return;
            }

            bool live = RigVideoFeed.IsLive;

            if (live && _filmingGeneration != RigVideoFeed.Generation)
            {
                // A new run — stop any previous clip first so two rigs in one session produce two files
                // rather than one truncated one.
                if (_controller != null) Stop("a new run started");
                Start();
            }
            else if (!live && _controller != null)
            {
                Stop("the rig finished");
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            // ExitingPlayMode is the last moment the RenderTexture is still alive. Waiting for
            // EnteredEditMode would mean flushing the encoder against freed native memory.
            if (state == PlayModeStateChange.ExitingPlayMode && _controller != null)
                Stop("play mode is ending");
        }

        private static void Start()
        {
            try
            {
                RenderTexture rt = RigVideoFeed.Texture;
                if (rt == null) return;

                Directory.CreateDirectory(RigVideoFeed.OutputDir);

                // Absolute, because Recorder resolves a relative OutputFile against the project folder and
                // the rigs' own paths are already project-relative — combining them twice would bury the
                // clip somewhere neither the rig nor Alexv would look.
                _clipPath = Path.GetFullPath(Path.Combine(RigVideoFeed.OutputDir, RigVideoFeed.ClipName));

                _settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                _settings.SetRecordModeToManual();

                // See the class comment — these two lines are the whole reason recording cannot corrupt
                // what the rig measures.
                _settings.FrameRatePlayback = FrameRatePlayback.Variable;
                _settings.CapFrameRate = false;
                _settings.ExitPlayMode = false;

                _movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                _movie.name = "RigClip";
                _movie.Enabled = true;
                _movie.OutputFile = _clipPath;          // extension is added by the encoder
                _movie.CaptureAlpha = false;
                _movie.CaptureAudio = false;
                _movie.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
                };

                // Film the rig's OWN render target: the exact frames that were graded, and non-invasive —
                // Recorder reads this texture, it never rebinds the camera the rig depends on.
                _movie.ImageInputSettings = new RenderTextureInputSettings
                {
                    RenderTexture = rt,
                    FlipFinalOutput = false,
                };

                _settings.AddRecorderSettings(_movie);

                _controller = new RecorderController(_settings);
                _controller.PrepareRecording();

                if (!_controller.StartRecording())
                {
                    Debug.LogWarning("[RigVideo] Recorder declined to start — this run is stills-only.");
                    Cleanup();
                    return;
                }

                _filmingGeneration = RigVideoFeed.Generation;
                Debug.Log($"[RigVideo] Filming {rt.width}x{rt.height} → {_clipPath}.mp4");
            }
            catch (Exception e)
            {
                // The stills are the primary evidence. A recording failure may never take a shoot down.
                Debug.LogWarning($"[RigVideo] Could not start recording — this run is stills-only. " +
                                 $"{e.GetType().Name}: {e.Message}");
                Cleanup();
            }
        }

        private static void Stop(string why)
        {
            try
            {
                // Read BEFORE stopping: this is the check that the Variable/CapFrameRate settings above
                // actually held. A non-zero captureDeltaTime means the recorder drove the game clock, and
                // every timing figure the rig printed is suspect.
                float captureDelta = Time.captureDeltaTime;

                if (_controller != null && _controller.IsRecording())
                {
                    _controller.StopRecording();
                    Debug.Log($"[RigVideo] Stopped ({why}) → {_clipPath}.mp4  " +
                              $"[Time.captureDeltaTime {captureDelta:0.####} — 0 means the rig's own clock " +
                              "was never driven by the recorder]");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RigVideo] Error while stopping: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        /// <summary>Destroys the ScriptableObjects the controller was built from. They are created with
        /// CreateInstance and belong to nobody, so without this each run leaks three of them for the rest
        /// of the editor session.</summary>
        private static void Cleanup()
        {
            _controller = null;
            _filmingGeneration = -1;

            if (_movie != null) { UnityEngine.Object.DestroyImmediate(_movie); _movie = null; }
            if (_settings != null) { UnityEngine.Object.DestroyImmediate(_settings); _settings = null; }
        }
    }
}
