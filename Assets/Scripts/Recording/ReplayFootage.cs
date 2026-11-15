using System;
using UnityEngine;
using CameraGame.Events;

namespace CameraGame.Recording
{
    /// <summary>The camera the player films through, as one tick saw it.</summary>
    public struct CameraSample
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public float FieldOfView;
    }

    /// <summary>One 30 Hz tick of the track.</summary>
    public struct TickSample
    {
        /// <summary>Clip time (seconds since the press, on the clip's own clock) of the frame the tick was
        /// sampled on. Ticks are taken on the first game frame past each 1/30-s boundary, so this jitters by up
        /// to one game frame around the boundary; it is stored so that a reader can tell (ADR-4 risk 3).</summary>
        public double Time;

        /// <summary>How many 1/30-s playback slots this tick stands for: 1 at 30 fps and above, more below.</summary>
        public int Repeats;

        /// <summary><c>Time.frameCount</c> of the sampled frame. Diagnostic only: it lets a rig match a tick to
        /// what was rendered on that frame. Not part of what a replay needs.</summary>
        public int Frame;

        public CameraSample Camera;

        /// <summary>This tick's actors are <c>ActorAt(ActorStart … ActorStart + ActorCount − 1)</c>.</summary>
        public int ActorStart;
        public int ActorCount;
    }

    /// <summary>One event actor in one tick.</summary>
    public struct ActorSample
    {
        /// <summary>What the actor was running. A replay builds its ghost from this definition's prefab, never
        /// from a live instance, so a clip loaded in a later session can rebuild it (ADR-4 Consequences).</summary>
        public EventDefinition Definition;

        /// <summary>The lifecycle the pooled instance was running (<see cref="EventActor.LifecycleSerial"/>).
        /// The drunk is the SAME instance every cycle, so this is what tells two lifecycles apart.</summary>
        public int Lifecycle;

        /// <summary>False when the actor was listed but not in the world (<c>!isActiveAndEnabled</c>): then
        /// nothing else in the sample was read.</summary>
        public bool Live;

        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        /// <summary>This actor's layers are <c>LayerAt(LayerStart … LayerStart + LayerCount − 1)</c>.</summary>
        public int LayerStart;
        public int LayerCount;
    }

    /// <summary>One Animator layer of one actor in one tick: the state, its time, and the transition.</summary>
    public struct LayerSample
    {
        public int StateHash;          // AnimatorStateInfo.fullPathHash
        public float StateTime;        // AnimatorStateInfo.normalizedTime
        public bool InTransition;
        public int NextStateHash;      // valid only InTransition
        public float NextStateTime;
        public float TransitionTime;   // AnimatorTransitionInfo.normalizedTime
        public float TransitionDuration;

        /// <summary>The unit of <see cref="TransitionDuration"/>. A replay must cross-fade by this unit
        /// (<c>CrossFadeInFixedTime</c> for Fixed): the spike passed seconds to the normalized CrossFade.</summary>
        public DurationUnit TransitionUnit;
    }

    /// <summary>
    /// ADR-4's footage: a 30 Hz track of the camera and of every live event actor, plus the sound the
    /// listener heard as 16-bit PCM on the same timeline (Story 1.15, AC5). The first <see cref="IFootage"/>.
    ///
    /// ⚠️ READ-ONLY. Nothing outside <see cref="ReplayBackend"/> writes footage. The read API is what the
    /// verification rig uses now and what Story 1.18's playback will use.
    ///
    /// ⚠️ A HANDLE ON POOLED STORAGE. The arrays belong to a storage block the backend preallocated at
    /// startup (no 23 MB allocation on the press frame). <see cref="Dispose"/> hands the block back for the
    /// next clip and empties this handle; a disposed footage reads as zero ticks and no sound, and never as a
    /// LATER clip's data, because the handle (not the storage) is what a listener holds.
    /// </summary>
    public sealed class ReplayFootage : IFootage
    {
        private ReplayStorage _storage;
        private readonly int _ticks;
        private readonly int _actors;
        private readonly int _layers;
        private readonly long _pcmFrames;
        private readonly long _boundaries;

        /// <summary>Format version of this footage (<see cref="FootageFormat.Version"/>).</summary>
        public int FormatVersion { get; } = FootageFormat.Version;

        /// <summary>Ticks per second (<see cref="FootageFormat.TickRate"/>).</summary>
        public int TickRate => FootageFormat.TickRate;

        /// <summary>The listener's output sample rate when the clip was recorded (Hz).</summary>
        public int SampleRate { get; }

        /// <summary>The listener's channel count (2 for stereo). The PCM is interleaved in this many channels.</summary>
        public int Channels { get; }

        /// <summary>False when the clip has no sound at all (no tap, or its sound was lost): the timeline is
        /// still valid, and it plays as silence for <see cref="Seconds"/>.</summary>
        public bool HasSound { get; }

        internal ReplayFootage(ReplayStorage storage, int ticks, int actors, int layers, long boundaries,
                               long pcmFrames, int sampleRate, int channels, bool hasSound)
        {
            _storage = storage;
            _ticks = ticks;
            _actors = actors;
            _layers = layers;
            _boundaries = boundaries;
            _pcmFrames = hasSound ? pcmFrames : 0;
            SampleRate = sampleRate;
            Channels = channels;
            HasSound = hasSound;
        }

        public bool IsDisposed => _storage == null;

        public float Seconds => IsDisposed ? 0f : (float)FootageFormat.SecondsFor(_boundaries);

        /// <summary>Slots of playback (the sum of every tick's repeats).</summary>
        public long Boundaries => IsDisposed ? 0 : _boundaries;

        public int TickCount => IsDisposed ? 0 : _ticks;
        public int ActorSampleCount => IsDisposed ? 0 : _actors;
        public int LayerSampleCount => IsDisposed ? 0 : _layers;

        /// <summary>PCM frames (samples per channel). Exactly <see cref="FootageFormat.PcmFramesFor"/> of the
        /// clip's boundaries when <see cref="HasSound"/>, else 0.</summary>
        public long PcmFrames => IsDisposed ? 0 : _pcmFrames;

        public long Bytes => IsDisposed ? 0 : BytesFor(_ticks, _actors, _layers, _pcmFrames, Channels);

        internal static long BytesFor(long ticks, long actors, long layers, long pcmFrames, int channels) =>
            ticks * ReplayStorage.TickBytes + actors * ReplayStorage.ActorBytes + layers * ReplayStorage.LayerBytes
            + pcmFrames * channels * FootageFormat.BytesPerSample;

        public TickSample TickAt(int i) { Check(i, TickCount); return _storage.Ticks[i]; }
        public ActorSample ActorAt(int i) { Check(i, ActorSampleCount); return _storage.Actors[i]; }
        public LayerSample LayerAt(int i) { Check(i, LayerSampleCount); return _storage.Layers[i]; }

        /// <summary>Copies interleaved 16-bit PCM from frame <paramref name="startFrame"/> into
        /// <paramref name="destination"/> (as many whole frames as fit). Returns the frames copied.</summary>
        public int ReadPcm(long startFrame, short[] destination)
        {
            if (destination == null || IsDisposed || !HasSound || startFrame < 0 || startFrame >= _pcmFrames) return 0;
            long frames = Math.Min(destination.Length / Channels, _pcmFrames - startFrame);
            Array.Copy(_storage.Pcm, startFrame * Channels, destination, 0, frames * Channels);
            return (int)frames;
        }

        /// <summary>The same as <see cref="ReadPcm(long, short[])"/>, converted to float samples in [−1, 1].</summary>
        public int ReadPcm(long startFrame, float[] destination)
        {
            if (destination == null || IsDisposed || !HasSound || startFrame < 0 || startFrame >= _pcmFrames) return 0;
            long frames = Math.Min(destination.Length / Channels, _pcmFrames - startFrame);
            long src = startFrame * Channels;
            int n = (int)(frames * Channels);
            short[] pcm = _storage.Pcm;
            for (int i = 0; i < n; i++) destination[i] = FootageFormat.FromPcm16(pcm[src + i]);
            return (int)frames;
        }

        /// <summary>Hands the storage back for the next clip and empties this handle. Idempotent.</summary>
        public void Dispose()
        {
            ReplayStorage s = _storage;
            if (s == null) return;
            _storage = null;
            s.Release();
        }

        private static void Check(int i, int count)
        {
            if ((uint)i >= (uint)count) throw new ArgumentOutOfRangeException(nameof(i), i, $"0..{count - 1}");
        }
    }

    /// <summary>
    /// One clip's worth of preallocated arrays. Owned by a <see cref="ReplayBackend"/>'s pool while free, and
    /// by exactly one <see cref="ReplayFootage"/> while it holds a clip.
    /// </summary>
    internal sealed class ReplayStorage
    {
        // Approximate managed sizes of the structs, for Bytes accounting (Marshal.SizeOf would differ for the
        // EventDefinition reference). Close enough to compare against capacity; not used for any offset.
        internal const int TickBytes = 72;
        internal const int ActorBytes = 64;
        internal const int LayerBytes = 32;

        internal readonly TickSample[] Ticks;
        internal readonly ActorSample[] Actors;
        internal readonly LayerSample[] Layers;
        internal readonly short[] Pcm;
        internal readonly int Channels;
        internal readonly int SampleRate;
        internal readonly long PcmCapacityFrames;

        private ReplayBackend _pool;

        internal ReplayStorage(ReplayBackend pool, int ticks, int actors, int layers, long pcmFrames, int channels, int sampleRate)
        {
            _pool = pool;
            Ticks = new TickSample[ticks];
            Actors = new ActorSample[actors];
            Layers = new LayerSample[layers];
            Channels = channels;
            SampleRate = sampleRate;
            PcmCapacityFrames = pcmFrames;
            Pcm = new short[pcmFrames * channels];
        }

        internal long CapacityBytes => ReplayFootage.BytesFor(Ticks.Length, Actors.Length, Layers.Length, PcmCapacityFrames, Channels);

        /// <summary>The backend is gone (destroyed): a storage released afterwards is simply dropped for the GC.</summary>
        internal void Orphan() => _pool = null;

        internal void Release()
        {
            // Drop the definition references so a held-but-unused storage keeps no assets alive.
            Array.Clear(Actors, 0, Actors.Length);
            ReplayBackend pool = _pool;
            if (pool != null) pool.Return(this);
        }
    }
}
