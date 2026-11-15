using System.Collections.Generic;
using UnityEngine;
using CameraGame.Core;
using CameraGame.Events;

namespace CameraGame.Recording
{
    /// <summary>
    /// ADR-4's replay backend (Story 1.15): a 30 Hz track of the camera and every live event actor, plus the
    /// listener's PCM through a <see cref="ListenerTap"/>, into storage preallocated at startup.
    ///
    /// Written as new game code, not copied from the spike (ADR-4), and it fixes the spike's known defects:
    ///   - it records EVERY live actor, read live from <see cref="EventManager.ActiveActors"/> on every tick,
    ///     and names each one's <see cref="EventDefinition"/> and lifecycle (the spike cloned "the first
    ///     live actor");
    ///   - it reads each actor's cached Animator (no <c>GetComponentInChildren</c> per tick) and counts its
    ///     layers per actor, per tick (the spike fixed the count when the track was allocated);
    ///   - it stores each transition's duration WITH its unit;
    ///   - it stamps every tick with its clip time and repeat count, and the PCM with the block's DSP time.
    ///
    /// ⚠️ NO ALLOCATION PER FRAME (AC6). <see cref="Sample"/> writes into preallocated struct arrays and reads
    /// only properties and struct-returning Animator calls. Storage is allocated in <see cref="Prepare"/>
    /// (the recorder's Awake), two clips' worth: the clip being recorded and the one the recorder holds
    /// while nobody has claimed it. A third is allocated only if a listener has claimed and still holds both
    /// (Story 1.17's card), with one Info line.
    /// </summary>
    public sealed class ReplayBackend : IClipBackend
    {
        /// <summary>Storage blocks preallocated, and the most kept free for reuse.</summary>
        public const int PooledStorages = 2;

        // Extra PCM frames beyond the clip's length: the audio thread can run up to a few blocks ahead of the
        // main thread's last tick, and the PCM is trimmed to the tick timeline at the end anyway.
        private const int PcmSlackFrames = 8192;

        private readonly Camera _camera;
        private readonly EventManager _events;
        private readonly ListenerTap _tap;
        private readonly int _maxActors;
        private readonly int _maxLayers;

        private readonly List<ReplayStorage> _free = new List<ReplayStorage>(PooledStorages);
        private int _created;
        private float _preparedSeconds;
        private int _preparedRate;
        private int _preparedChannels;

        // The clip in progress.
        private ReplayStorage _cur;
        private long _maxBoundaries;
        private long _emitted;
        private int _ticks, _actors, _layers;
        private bool _tapArmed;
        private bool _full;
        private bool _actorOverflowWarned;
        private bool _recordabilityWarned;
        private bool _destroyed;

        public ReplayBackend(Camera camera, EventManager events, ListenerTap tap, int maxActorsPerTick, int maxAnimatorLayers)
        {
            _camera = camera;
            _events = events;
            _tap = tap;
            _maxActors = Mathf.Max(1, maxActorsPerTick);
            _maxLayers = Mathf.Max(1, maxAnimatorLayers);
        }

        /// <summary>The bytes one storage block can hold at most (the clip's preallocated capacity).</summary>
        public long CapacityBytesPerClip { get; private set; }

        /// <summary>How many storage blocks this backend has ever allocated (2 unless something holds clips).</summary>
        public int StoragesCreated => _created;

        /// <summary>Storage blocks free for the next clip.</summary>
        public int StoragesFree => _free.Count;

        /// <summary>The sample rate and channel count clips are recorded at.</summary>
        public int SampleRate => _preparedRate;
        public int Channels => _preparedChannels;

        /// <summary>The longest clip the preallocated storage can hold, in seconds.</summary>
        public float CapacitySeconds => _preparedSeconds;

        /// <summary>
        /// Allocates the storage for clips of up to <paramref name="maxSeconds"/>, at the current output sample
        /// rate and speaker mode. Called from the recorder's Awake, and again at a Begin whose audio settings no
        /// longer match (after a device change). Never per frame.
        /// </summary>
        public void Prepare(float maxSeconds)
        {
            int rate = AudioSettings.outputSampleRate;
            int channels = ChannelsOf(AudioSettings.speakerMode);
            if (rate <= 0) rate = 48000;   // no audio device: keep a valid timeline, the tap will not arm

            if (_free.Count == PooledStorages && Mathf.Approximately(maxSeconds, _preparedSeconds)
                && rate == _preparedRate && channels == _preparedChannels)
                return;

            foreach (ReplayStorage s in _free) s.Orphan();
            _free.Clear();

            _preparedSeconds = maxSeconds;
            _preparedRate = rate;
            _preparedChannels = channels;
            for (int i = 0; i < PooledStorages; i++) _free.Add(NewStorage());
            CapacityBytesPerClip = _free[0].CapacityBytes;
        }

        private ReplayStorage NewStorage()
        {
            int ticks = (int)FootageFormat.MaxBoundariesFor(_preparedSeconds) + 2;
            int actors = ticks * _maxActors;
            int layers = actors * _maxLayers;
            long pcmFrames = FootageFormat.PcmFramesFor(FootageFormat.MaxBoundariesFor(_preparedSeconds), _preparedRate)
                             + PcmSlackFrames;
            _created++;
            return new ReplayStorage(this, ticks, actors, layers, pcmFrames, _preparedChannels, _preparedRate);
        }

        internal void Return(ReplayStorage s)
        {
            if (_destroyed || s.SampleRate != _preparedRate || s.Channels != _preparedChannels
                || _free.Count >= PooledStorages || _free.Contains(s))
            {
                s.Orphan();   // dropped: the GC frees it (more than the pool keeps, or stale audio settings)
                return;
            }
            _free.Add(s);
        }

        public bool Begin(double startDspTime, float maxSeconds)
        {
            if (_cur != null || _destroyed || _camera == null) return false;

            // A device change since Prepare (sample rate, speaker mode) makes the pooled buffers the wrong shape.
            if (AudioSettings.outputSampleRate > 0
                && (AudioSettings.outputSampleRate != _preparedRate || ChannelsOf(AudioSettings.speakerMode) != _preparedChannels))
                Prepare(_preparedSeconds);

            if (_free.Count > 0)
            {
                _cur = _free[_free.Count - 1];
                _free.RemoveAt(_free.Count - 1);
            }
            else
            {
                GameLog.Info("Recording", "Allocating clip storage: every pooled block is held by a clip someone claimed.");
                _cur = NewStorage();
            }

            float seconds = Mathf.Min(maxSeconds, _preparedSeconds);
            _maxBoundaries = FootageFormat.MaxBoundariesFor(seconds);
            _emitted = 0;
            _ticks = _actors = _layers = 0;
            _full = false;

            _tapArmed = _tap != null && _tap.isActiveAndEnabled && AudioSettings.outputSampleRate > 0
                        && _tap.Arm(_cur.Pcm, _cur.Channels, _cur.SampleRate, startDspTime, _cur.PcmCapacityFrames);
            return true;
        }

        public double RecordedSeconds => FootageFormat.SecondsFor(_emitted);

        public bool TryGetStopRequest(out StopReason reason)
        {
            reason = StopReason.LengthLimit;
            if (_cur == null) return false;
            if (_tapArmed && _tap != null && _tap.ChannelMismatch)
            {
                reason = StopReason.AudioDeviceChanged;
                return true;
            }
            return _full;
        }

        public void Sample(double clipSeconds)
        {
            if (_cur == null || _full) return;

            int repeats = FootageFormat.RepeatsDue(clipSeconds, _emitted, _maxBoundaries);
            if (repeats <= 0) return;
            if (_ticks >= _cur.Ticks.Length) { _full = true; return; }

            ref TickSample t = ref _cur.Ticks[_ticks];
            t.Time = clipSeconds;
            t.Repeats = repeats;
            t.Frame = Time.frameCount;

            if (_camera != null)
            {
                Transform ct = _camera.transform;
                ct.GetPositionAndRotation(out t.Camera.Position, out t.Camera.Rotation);
                t.Camera.FieldOfView = _camera.fieldOfView;
            }

            t.ActorStart = _actors;
            t.ActorCount = 0;
            if (_events != null) SampleActors(ref t);

            _ticks++;
            _emitted += repeats;
            if (_emitted >= _maxBoundaries) _full = true;
        }

        private void SampleActors(ref TickSample t)
        {
            // Read LIVE every tick and never cached across frames: actors are pooled, so a held reference does
            // not go null when its event ends, it becomes a different event (ISubject's liveness contract).
            IReadOnlyList<EventActor> actors = _events.ActiveActors;
            for (int i = 0; i < actors.Count; i++)
            {
                EventActor actor = actors[i];
                if (actor == null) continue;

                if (t.ActorCount >= _maxActors || _actors >= _cur.Actors.Length)
                {
                    if (!_actorOverflowWarned)
                    {
                        _actorOverflowWarned = true;
                        GameLog.Warn("Recording", $"More event actors are live than a tick can hold ({_maxActors}); " +
                                                  "the extra ones are left out of clips (RecordingConfig.maxActorsPerTick). Said once.");
                    }
                    return;
                }

                ref ActorSample a = ref _cur.Actors[_actors];
                a.Definition = actor.Definition;
                a.Lifecycle = actor.LifecycleSerial;
                a.Live = actor.isActiveAndEnabled;   // ActiveActors tracks the lifecycle, not activation
                a.LayerStart = _layers;
                a.LayerCount = 0;
                if (a.Live)
                {
                    Transform at = actor.transform;
                    at.GetPositionAndRotation(out a.Position, out a.Rotation);
                    a.Scale = at.localScale;
                    SampleLayers(actor.Animator, ref a);
                }
                else
                {
                    a.Position = default;
                    a.Rotation = Quaternion.identity;
                    a.Scale = default;
                }

                _actors++;
                t.ActorCount++;
            }
        }

        private void SampleLayers(Animator animator, ref ActorSample a)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;

            int layerCount = animator.layerCount;
            if (!_recordabilityWarned && (layerCount > _maxLayers || animator.parameterCount > 0 || MoreThanOneWeighted(animator, layerCount)))
            {
                // The Recordability rule (game-architecture.md): the track carries state, time and transition per
                // layer. Parameters and layer weights are not in it, so a replay of this actor would differ.
                _recordabilityWarned = true;
                GameLog.Warn("Recording", $"'{(a.Definition != null ? a.Definition.Id : animator.name)}' has an Animator the track " +
                                          $"cannot fully carry ({layerCount} layers, {animator.parameterCount} parameters; the track " +
                                          $"records state, time and transition for up to {_maxLayers} layers). Its replay may differ. Said once.");
            }

            int n = Mathf.Min(layerCount, _maxLayers);
            for (int l = 0; l < n && _layers < _cur.Layers.Length; l++)
            {
                ref LayerSample s = ref _cur.Layers[_layers];
                AnimatorStateInfo cur = animator.GetCurrentAnimatorStateInfo(l);
                s.StateHash = cur.fullPathHash;
                s.StateTime = cur.normalizedTime;
                s.InTransition = animator.IsInTransition(l);
                if (s.InTransition)
                {
                    AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(l);
                    AnimatorTransitionInfo tr = animator.GetAnimatorTransitionInfo(l);
                    s.NextStateHash = next.fullPathHash;
                    s.NextStateTime = next.normalizedTime;
                    s.TransitionTime = tr.normalizedTime;
                    s.TransitionDuration = tr.duration;
                    s.TransitionUnit = tr.durationUnit;
                }
                else
                {
                    s.NextStateHash = 0;
                    s.NextStateTime = 0f;
                    s.TransitionTime = 0f;
                    s.TransitionDuration = 0f;
                    s.TransitionUnit = DurationUnit.Fixed;
                }
                _layers++;
                a.LayerCount++;
            }
        }

        private static bool MoreThanOneWeighted(Animator animator, int layerCount)
        {
            for (int l = 1; l < layerCount; l++)
                if (animator.GetLayerWeight(l) > 0f) return true;
            return false;
        }

        public IFootage End()
        {
            if (_cur == null) return null;
            ReplayStorage s = _cur;
            _cur = null;

            long written = 0;
            bool mismatch = false;
            bool armed = _tapArmed;
            _tapArmed = false;
            if (armed) written = _tap != null ? _tap.Disarm(out mismatch, out _) : 0;

            // PAD OR TRIM TO THE TICK TIMELINE, never truncate the picture to the sound (ADR-4 risk 3): the PCM
            // lasts exactly as long as the ticks, to the frame.
            long target = FootageFormat.PcmFramesFor(_emitted, s.SampleRate);
            if (target > s.PcmCapacityFrames) target = s.PcmCapacityFrames;
            bool hasSound = armed && !mismatch;
            if (hasSound && written < target)
                System.Array.Clear(s.Pcm, (int)(written * s.Channels), (int)((target - written) * s.Channels));

            return new ReplayFootage(s, _ticks, _actors, _layers, _emitted, target, s.SampleRate, s.Channels, hasSound);
        }

        public void Abort()
        {
            IFootage f = End();
            f?.Dispose();
        }

        /// <summary>Drops every pooled block. Called from the recorder's OnDestroy. Footage still held by a
        /// claimed clip stays valid; its storage is simply not reused.</summary>
        public void Destroy()
        {
            if (_cur != null) Abort();
            _destroyed = true;
            foreach (ReplayStorage s in _free) s.Orphan();
            _free.Clear();
        }

        /// <summary>The channel count of the listener's mix for a speaker mode.</summary>
        public static int ChannelsOf(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Stereo: return 2;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                case AudioSpeakerMode.Prologic: return 2;
                default: return 2;
            }
        }
    }
}
