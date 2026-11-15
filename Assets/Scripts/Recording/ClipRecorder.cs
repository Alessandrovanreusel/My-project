using System;
using System.Globalization;
using UnityEngine;
using CameraGame.Core;
using CameraGame.Events;

namespace CameraGame.Recording
{
    /// <summary>
    /// The camcorder's recorder (Story 1.15, FR3): starts and stops a clip, enforces its length, and announces
    /// every finished clip on <see cref="ClipRecordedChannel"/> exactly once. Footage is captured by an
    /// <see cref="IClipBackend"/>: ADR-4's <see cref="ReplayBackend"/>, a 30 Hz track plus the listener's PCM.
    ///
    /// ================================================================================================
    /// WHO DRIVES IT, AND WHAT IT KNOWS (decision 1b)
    /// ================================================================================================
    /// <c>PhotoModeController</c> calls <see cref="Begin"/> and <see cref="Stop"/>: Record toggles it, and every
    /// way the camera leaves the Viewfinder stops it with <see cref="StopReason.CameraLowered"/>. The recorder
    /// knows NOTHING about PhotoMode, UI, Gallery or the beeps: the direction is PhotoMode → Recording →
    /// Events and Core (game-architecture.md §Clip Recording). The REC indicator READS
    /// <see cref="IsRecording"/> and <see cref="ElapsedSeconds"/>, and plays the beeps from
    /// <see cref="Started"/> and <see cref="Stopped"/>, so every stop beeps, including the ones the recorder
    /// decides itself (the length limit, a device change).
    ///
    /// ================================================================================================
    /// THE CLIP'S CLOCK (decision 1d)
    /// ================================================================================================
    /// At <see cref="Begin"/> it reads <c>AudioSettings.dspTime</c> (the clip's time zero on the audio
    /// sample clock, which the <see cref="ListenerTap"/> places every PCM block against) and, in the same
    /// instant, <c>Time.realtimeSinceStartupAsDouble</c>. Each frame's clip time is the realtime clock since
    /// that anchor: <c>dspTime</c> only advances in whole DSP blocks on the main thread (~21 ms at 1024
    /// samples and 48 kHz), so it would quantise the picture's clock, while the realtime clock does not
    /// (measured by the rig). The PCM is padded or trimmed to the ticks at the end.
    ///
    /// ⚠️ THE CLIP FOLLOWS REAL TIME, NOT <c>Time.timeScale</c>, because the sound does. A
    /// <c>timeScale = 0</c> freeze (only rigs do that today) records a frozen world with live sound, which is
    /// truthful. A future PAUSE MENU is a screen owner like the gallery: it must suppress the raise
    /// (<c>PhotoModeController.SetRaiseSuppressed</c>), which lowers the camera, which stops the recording and
    /// keeps the clip. It must not leave a recording running behind a menu.
    ///
    /// ================================================================================================
    /// OWNERSHIP OF THE FOOTAGE (decision 1c)
    /// ================================================================================================
    /// See <see cref="RecordedClip"/>: a listener that keeps a clip claims it during the raise; the recorder
    /// claims the rest and holds ONE (<see cref="LastClip"/>), disposing the one before. Before a memory card
    /// exists (Story 1.17) at most two clips' storage is live: the one recording and the one held.
    ///
    /// Fail-soft (NFR8): a missing camera stands the recorder down with one error; a missing channel, tap,
    /// event manager or config degrades with one line each. Nothing throws into Update.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder)]
    public class ClipRecorder : MonoBehaviour
    {
        /// <summary>Samples in LateUpdate AFTER ThirdPersonCamera (-100), which writes the camera's final pose,
        /// and after every Update (PhotoModeController writes the FOV there). Earlier would record last
        /// frame's camera. The rig proves this against the pose the camera actually rendered with.</summary>
        public const int ExecutionOrder = 10000;

        [Header("Wiring")]
        [Tooltip("Raised once per finished clip (Assets/Data/Channels/ClipRecordedChannel.asset). If unassigned, " +
                 "clips are still recorded and held as LastClip, but nothing hears about them.")]
        [SerializeField] private ClipRecordedChannel clipRecordedChannel;

        [Tooltip("Designer tunables (Assets/Data/Recording/RecordingConfig.asset). If unassigned, the recorder " +
                 "uses the design defaults.")]
        [SerializeField] private RecordingConfig config;

        [Tooltip("The camera the player films through: the SAME camera PhotoModeController zooms and grades " +
                 "through (Main Camera). Required. Not Camera.main: two cameras would give a plausible-looking, " +
                 "completely wrong clip.")]
        [SerializeField] private Camera recordCamera;

        [Tooltip("The EventManager whose live actors the track records, read live every tick. If unassigned, " +
                 "clips record the camera only.")]
        [SerializeField] private EventManager eventManager;

        [Tooltip("The PCM tap on the AudioListener's GameObject (Main Camera). If unassigned, clips are " +
                 "recorded silent (their footage says so).")]
        [SerializeField] private ListenerTap listenerTap;

        private ReplayBackend _backend;
        private bool _ready;
        private bool _channelReady;
        private bool _initialised;
        private bool _refusalLogged;

        private double _rt0;
        private DateTime _startUtc;
        private int _clipIndex;
        private volatile bool _audioConfigChanged;

        /// <summary>True from <see cref="Begin"/> until the clip stops.</summary>
        public bool IsRecording { get; private set; }

        /// <summary>Seconds of footage recorded so far, on the clip's own timeline (the sum of its ticks'
        /// repeats ÷ 30). This is what the REC timecode shows, so the number on screen IS the clip's length.
        /// 0 when not recording.</summary>
        public double ElapsedSeconds => IsRecording && _backend != null ? _backend.RecordedSeconds : 0d;

        /// <summary>The last finished clip nobody else claimed, which the recorder holds until the next one
        /// replaces it (and then disposes it). Null until then.</summary>
        public RecordedClip LastClip { get; private set; }

        /// <summary>The camera the recorder films through (PhotoModeController checks it is its own).</summary>
        public Camera RecordCamera => recordCamera;

        /// <summary>False when the recorder cannot record at all (no camera). Begin then refuses.</summary>
        public bool CanRecord => _ready;

        /// <summary><c>AudioSettings.dspTime</c> and the realtime clock at the current (or last) clip's start.</summary>
        public double StartDspTime { get; private set; }
        public double StartRealtime => _rt0;

        /// <summary>Diagnostics for the verification rig: what the backend preallocated and has allocated.</summary>
        public long CapacityBytesPerClip => _backend != null ? _backend.CapacityBytesPerClip : 0;
        public int StoragesCreated => _backend != null ? _backend.StoragesCreated : 0;
        public int StoragesFree => _backend != null ? _backend.StoragesFree : 0;

        /// <summary>Raised synchronously inside <see cref="Begin"/>, once recording has started.</summary>
        public event Action Started;

        /// <summary>Raised synchronously inside <see cref="Stop"/>, BEFORE the clip is announced on the channel,
        /// so the stop beep never waits on a listener. Every stop raises it, whatever the reason.</summary>
        public event Action<StopReason> Stopped;

        private void Awake()
        {
            Initialise();
        }

        private void Initialise()
        {
            if (_initialised) return;
            _initialised = true;

            _channelReady = clipRecordedChannel != null;
            if (!_channelReady)
                GameLog.Error("Recording", "ClipRecordedChannel unassigned: clips are recorded and held as LastClip, " +
                                           "but nothing (memory card, HUD) will hear about them.", this);

            if (recordCamera == null)
            {
                GameLog.Error("Recording", "No camera to film through (recordCamera unassigned): recording is disabled. " +
                                           "Assign Main Camera, the camera PhotoModeController zooms through.", this);
                _ready = false;
                return;
            }

            if (config == null)
                GameLog.Error("Recording", "RecordingConfig unassigned: recording with the design defaults " +
                                           $"({RecordingConfig.DefaultMaxClipSeconds} s clips).", this);
            else if (config.TryGetConfigProblem(out string problem))
                GameLog.Warn("Recording", $"RecordingConfig: {problem}");

            if (listenerTap == null)
                GameLog.Warn("Recording", "No ListenerTap assigned: clips are recorded with no sound (their footage is flagged silent).");

            if (eventManager == null)
                GameLog.Warn("Recording", "No EventManager assigned: clips record the camera only, with no event actors.");

            _backend = new ReplayBackend(recordCamera, eventManager, listenerTap,
                RecordingConfig.MaxActorsPerTickOf(config), RecordingConfig.MaxAnimatorLayersOf(config));

            // PREALLOCATE NOW, never on the press frame: a 120-s stereo clip is ~23 MB of PCM, and allocating it
            // inside the < 0.2 s record budget would also churn memory on every take (NFR2, NFR3).
            _backend.Prepare(RecordingConfig.MaxClipSecondsOf(config));
            _ready = true;
        }

        private void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;

            // Disabled or destroyed mid-clip (scene unload, leaving play mode, a rig): the clip is KEPT and
            // announced like any other, because footage recorded so far is always kept. Listeners follow the
            // subscribe-in-OnEnable / unsubscribe-in-OnDisable rule, so a listener already torn down has already
            // unsubscribed and cannot be called.
            Stop(StopReason.Disabled);
        }

        private void OnDestroy()
        {
            Stop(StopReason.Disabled);

            RecordedClip held = LastClip;
            LastClip = null;
            if (held != null && held.Owner == (object)this) held.Footage?.Dispose();

            _backend?.Destroy();
        }

        /// <summary>
        /// Starts a clip. False, with nothing started (no beep, no REC), when it is already recording, the
        /// recorder cannot record, or it is disabled; a refusal is logged once per session, not per press.
        /// </summary>
        public bool Begin()
        {
            if (IsRecording) return false;
            if (!_initialised) Initialise();

            // A recorder that cannot record (no camera) already said so ONCE, at startup: a press adds no second line
            // for the same fault (AC6, "one clear log line"; code review 2026-11-15).
            if (!_ready) return false;

            if (!isActiveAndEnabled)
            {
                if (!_refusalLogged)
                {
                    _refusalLogged = true;
                    GameLog.Warn("Recording", "Record pressed while the recorder is disabled: nothing was recorded. Said once.");
                }
                return false;
            }

            // The anchor pair, read back to back: the audio clock (the PCM's truth) and the realtime clock (the
            // picture's). Nothing heavy runs between them.
            double dsp = AudioSettings.dspTime;
            _rt0 = Time.realtimeSinceStartupAsDouble;
            _audioConfigChanged = false;

            if (!_backend.Begin(dsp, RecordingConfig.MaxClipSecondsOf(config)))
            {
                if (!_refusalLogged)
                {
                    _refusalLogged = true;
                    GameLog.Warn("Recording", "The clip backend could not start a clip: nothing was recorded. Said once.");
                }
                return false;
            }

            StartDspTime = dsp;
            _startUtc = DateTime.UtcNow;
            _clipIndex++;
            IsRecording = true;

            GameLog.Info("Recording", "Recording started.");
            Raise(Started);
            return true;
        }

        /// <summary>
        /// Stops the clip, keeps it, and announces it once on <see cref="ClipRecordedChannel"/>. Idempotent:
        /// does nothing when not recording.
        /// </summary>
        public void Stop(StopReason reason)
        {
            if (!IsRecording) return;
            IsRecording = false;

            IFootage footage = _backend.End();
            float seconds = footage != null ? footage.Seconds : 0f;
            var clip = new RecordedClip(RecordedClip.MakeId(_clipIndex, _startUtc), footage, _startUtc, seconds, reason);

            // Milestone, once per clip. Says whether the clip has sound, so a silent clip is never a surprise.
            var replay = footage as ReplayFootage;
            GameLog.Info("Recording", string.Format(CultureInfo.InvariantCulture,
                "Clip recorded: {0}, {1:0.000} s, {2} ticks, {3}, {4:0.0} MB, stopped by {5}.",
                clip.Id, seconds, replay != null ? replay.TickCount : 0,
                replay != null && replay.HasSound ? $"sound {replay.SampleRate} Hz x{replay.Channels}" : "NO SOUND",
                footage != null ? footage.Bytes / (1024.0 * 1024.0) : 0.0, reason));

            // Feedback first (the stop beep, REC off), then the announcement.
            Raise(Stopped, reason);

            if (_channelReady) clipRecordedChannel.Raise(clip);

            // The claim window closes here: whatever nobody claimed, the recorder holds, and lets go of the one
            // it held before.
            if (!clip.IsClaimed && clip.Claim(this))
            {
                RecordedClip previous = LastClip;
                LastClip = clip;
                if (previous != null && previous.Owner == (object)this) previous.Footage?.Dispose();
            }
        }

        private void LateUpdate()
        {
            if (!IsRecording) return;

            if (_audioConfigChanged)
            {
                _audioConfigChanged = false;
                GameLog.Warn("Recording", "The audio device or its configuration changed mid-clip: the clip was stopped and kept.");
                Stop(StopReason.AudioDeviceChanged);
                return;
            }

            _backend.Sample(Time.realtimeSinceStartupAsDouble - _rt0);

            if (_backend.TryGetStopRequest(out StopReason reason))
            {
                if (reason == StopReason.AudioDeviceChanged)
                    GameLog.Warn("Recording", "The listener's channel count changed mid-clip: the clip was stopped and kept.");
                Stop(reason);
            }
        }

        // May be raised off the main thread on some platforms: only set a flag here; LateUpdate acts on it.
        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            _audioConfigChanged = true;
        }

        // A throwing feedback handler must not abort the stop (the clip must still be announced and kept) and
        // must never throw into LateUpdate. Same isolation EventChannel gives channel listeners.
        private void Raise(Action handlers)
        {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action)d).Invoke(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }

        private void Raise(Action<StopReason> handlers, StopReason reason)
        {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action<StopReason>)d).Invoke(reason); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }
    }
}
