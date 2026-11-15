using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using CameraGame.Recording;
using Object = UnityEngine.Object;

namespace CameraGame.Tests
{
    /// <summary>
    /// Story 1.15 (Task 8): the recorder's pure logic, pinned with no scene. Nothing here judges whether a clip
    /// LOOKS or SOUNDS right; that is the Record Clip Shoot rig's job and, for the feel, Alexv's. What is pinned
    /// is the arithmetic a rig cannot see failing: tick repeats at every frame rate, the PCM length rule, the
    /// 16-bit clamp, the timecode under foreign cultures, the config's Safe accessors and validator, the claim
    /// rule, footage disposal, and the AC6 requirement that sampling allocates nothing.
    /// </summary>
    public class RecordingTests
    {
        // --- Tick scheduling ----------------------------------------------------------------------------

        /// <summary>Runs frames at a fixed rate (with a small phase offset, as a real first frame has) and
        /// returns the repeat count of every tick taken.</summary>
        private static int[] Simulate(double fps, double seconds, double firstFrame = 0.004, long max = long.MaxValue)
        {
            var repeats = new System.Collections.Generic.List<int>();
            long emitted = 0;
            for (double t = firstFrame; t <= seconds; t += 1.0 / fps)
            {
                int r = FootageFormat.RepeatsDue(t, emitted, max);
                if (r > 0) { repeats.Add(r); emitted += r; }
            }
            return repeats.ToArray();
        }

        [Test]
        public void TickZero_IsTakenOnTheStartFrame_WithOneRepeat()
        {
            Assert.AreEqual(1, FootageFormat.RepeatsDue(0.004, 0, long.MaxValue));
            Assert.AreEqual(1, FootageFormat.RepeatsDue(0.0, 0, long.MaxValue), "a frame at exactly 0 s still takes tick 0");
        }

        [TestCase(60.0)]
        [TestCase(144.0)]
        public void AboveThirtyFps_EveryTickRepeatsOnce(double fps)
        {
            int[] r = Simulate(fps, 10.0);
            foreach (int x in r) Assert.AreEqual(1, x, $"at {fps} fps a tick covered more than one slot");
            Assert.AreEqual(300, r.Length, "10 s at 30 Hz is 300 ticks (boundaries 0 … 299/30)");
        }

        [Test]
        public void AtFifteenFps_EveryTickAfterTheFirstStandsForTwoSlots()
        {
            int[] r = Simulate(15.0, 10.0);
            Assert.AreEqual(1, r[0], "tick 0 covers only the start boundary");
            for (int i = 1; i < r.Length; i++) Assert.AreEqual(2, r[i], $"tick {i}");
        }

        [TestCase(15.0)]
        [TestCase(30.0)]
        [TestCase(60.0)]
        [TestCase(144.0)]
        public void RepeatsAlwaysSumToTheBoundariesCrossed(double fps)
        {
            long emitted = 0;
            double last = 0;
            for (double t = 0.004; t <= 7.3; t += 1.0 / fps)
            {
                emitted += FootageFormat.RepeatsDue(t, emitted, long.MaxValue);
                last = t;
            }
            Assert.AreEqual(FootageFormat.BoundariesThrough(last), emitted, "true-duration clip whatever the frame rate");
        }

        [Test]
        public void AHalfSecondHitch_IsOneTickOfFifteenRepeats()
        {
            long emitted = 0;
            emitted += FootageFormat.RepeatsDue(0.99, emitted, long.MaxValue);   // boundaries 0..29: 30 slots
            Assert.AreEqual(30, emitted);
            Assert.AreEqual(15, FootageFormat.RepeatsDue(1.49, emitted, long.MaxValue), "boundaries 30..44 crossed in one frame");
        }

        [Test]
        public void TheLengthLimit_ClampsTheRepeats()
        {
            long max = FootageFormat.MaxBoundariesFor(3.0);
            Assert.AreEqual(90, max);
            Assert.AreEqual(10, FootageFormat.RepeatsDue(10.0, 80, max), "only the 10 slots left under the limit");
            Assert.AreEqual(0, FootageFormat.RepeatsDue(10.0, 90, max), "a full clip takes no more ticks");
        }

        [TestCase(double.NaN)]
        [TestCase(-0.5)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void BadClipTimes_CrossNoBoundary(double t)
        {
            Assert.AreEqual(0, FootageFormat.BoundariesThrough(t));
            Assert.AreEqual(0, FootageFormat.RepeatsDue(t, 0, long.MaxValue));
        }

        // Code review 2026-11-15: an unchecked (long) of a double past ~9.2e18 wrapped to long.MinValue, so a huge time
        // counted a NEGATIVE number of boundaries and a clip could never reach its limit.
        [TestCase(1e300)]
        [TestCase(double.MaxValue)]
        public void HugeClipTimes_Saturate_AndStillReachTheLimit(double t)
        {
            Assert.AreEqual(long.MaxValue, FootageFormat.BoundariesThrough(t));
            Assert.AreEqual(90, FootageFormat.RepeatsDue(t, 0, 90), "a huge time fills the clip to its limit");
            Assert.AreEqual(long.MaxValue, FootageFormat.MaxBoundariesFor(t));
        }

        // --- The PCM length rule ------------------------------------------------------------------------

        [TestCase(1L, 48000, 1600L)]
        [TestCase(30L, 48000, 48000L)]
        [TestCase(3600L, 48000, 5760000L)]
        [TestCase(1L, 44100, 1470L)]
        [TestCase(7L, 44100, 10290L)]
        [TestCase(0L, 48000, 0L)]
        [TestCase(10L, 0, 0L)]
        public void PcmFrames_AreExactlyTheTickDuration(long boundaries, int rate, long expected)
        {
            Assert.AreEqual(expected, FootageFormat.PcmFramesFor(boundaries, rate));
        }

        [Test]
        public void PcmFrames_RoundToTheNearestFrame()
        {
            // 1 slot at 22050 Hz is 735 frames; at 11025 Hz, 367.5, which rounds half up to 368.
            Assert.AreEqual(735, FootageFormat.PcmFramesFor(1, 22050));
            Assert.AreEqual(368, FootageFormat.PcmFramesFor(1, 11025));
        }

        // --- Float → 16-bit ----------------------------------------------------------------------------

        [TestCase(0f, (short)0)]
        [TestCase(1f, (short)32767)]
        [TestCase(-1f, (short)-32767)]
        [TestCase(1.7f, (short)32767)]
        [TestCase(-3f, (short)-32767)]
        [TestCase(float.PositiveInfinity, (short)32767)]
        [TestCase(float.NegativeInfinity, (short)-32767)]
        [TestCase(float.NaN, (short)0)]
        [TestCase(0.25f, (short)8192)]
        public void ToPcm16_ClampsAndSilencesNaN(float sample, short expected)
        {
            Assert.AreEqual(expected, FootageFormat.ToPcm16(sample));
        }

        // --- Timecode ------------------------------------------------------------------------------------

        [TestCase(0.0, "0:00:00")]
        [TestCase(0.999, "0:00:00")]
        [TestCase(12.4, "0:00:12")]
        [TestCase(59.999, "0:00:59")]
        [TestCase(60.0, "0:01:00")]
        [TestCase(3599.999, "0:59:59")]
        [TestCase(3600.0, "1:00:00")]
        [TestCase(36061.0, "10:01:01")]
        [TestCase(double.NaN, "0:00:00")]
        [TestCase(-5.0, "0:00:00")]
        [TestCase(double.PositiveInfinity, "0:00:00")]
        public void Timecode_IsFlooredHMMSS_InEveryCulture(double seconds, string expected)
        {
            foreach (string culture in new[] { "", "en-US", "fr-FR", "de-DE", "tr-TR", "ar-SA", "fa-IR" })
            {
                CultureInfo previous = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = culture.Length == 0 ? CultureInfo.InvariantCulture : new CultureInfo(culture);
                    Assert.AreEqual(expected, ClipTimecode.Format(seconds), $"culture '{culture}'");
                }
                finally
                {
                    CultureInfo.CurrentCulture = previous;
                }
            }
        }

        [Test]
        public void TimecodeWholeSeconds_ChangesOnlyOnTheSecond()
        {
            Assert.AreEqual(4, ClipTimecode.WholeSeconds(4.0));
            Assert.AreEqual(4, ClipTimecode.WholeSeconds(4.999));
            Assert.AreEqual(5, ClipTimecode.WholeSeconds(5.0));
        }

        // --- RecordingConfig ----------------------------------------------------------------------------

        private static RecordingConfig NewConfig() => ScriptableObject.CreateInstance<RecordingConfig>();

        [Test]
        public void Config_Defaults_AreClean()
        {
            RecordingConfig c = NewConfig();
            try
            {
                Assert.IsFalse(c.TryGetConfigProblem(out string p), p);
                Assert.AreEqual(RecordingConfig.DefaultMaxClipSeconds, c.SafeMaxClipSeconds);
                Assert.AreEqual(RecordingConfig.DefaultMaxActorsPerTick, c.SafeMaxActorsPerTick);
                Assert.AreEqual(RecordingConfig.DefaultMaxAnimatorLayers, c.SafeMaxAnimatorLayers);
                Assert.AreEqual(RecordingConfig.DefaultBeepVolume, c.SafeBeepVolume);
            }
            finally { Object.DestroyImmediate(c); }
        }

        [TestCase(0f, RecordingConfig.MinClipSeconds)]
        [TestCase(-5f, RecordingConfig.MinClipSeconds)]
        [TestCase(float.NaN, RecordingConfig.DefaultMaxClipSeconds)]
        [TestCase(float.PositiveInfinity, RecordingConfig.DefaultMaxClipSeconds)]
        [TestCase(float.NegativeInfinity, RecordingConfig.DefaultMaxClipSeconds)]
        [TestCase(1e9f, RecordingConfig.MaxClipSecondsCap)]
        [TestCase(301f, RecordingConfig.MaxClipSecondsCap)]
        public void Config_BadMaxClipSeconds_IsMadeSafeAndReported(float authored, float safe)
        {
            RecordingConfig c = NewConfig();
            try
            {
                c.maxClipSeconds = authored;
                Assert.AreEqual(safe, c.SafeMaxClipSeconds);
                Assert.IsTrue(c.TryGetConfigProblem(out string p));
                StringAssert.Contains("maxClipSeconds", p);
            }
            finally { Object.DestroyImmediate(c); }
        }

        [Test]
        public void Config_InRangeMaxClipSeconds_IsKeptAndClean()
        {
            RecordingConfig c = NewConfig();
            try
            {
                c.maxClipSeconds = 3f;
                Assert.AreEqual(3f, c.SafeMaxClipSeconds);
                Assert.IsFalse(c.TryGetConfigProblem(out string p), p);
            }
            finally { Object.DestroyImmediate(c); }
        }

        [Test]
        public void Config_BadCountsAndVolume_AreClampedAndReportedTogether()
        {
            RecordingConfig c = NewConfig();
            try
            {
                c.maxActorsPerTick = 0;
                c.maxAnimatorLayers = 99;
                c.beepVolume = float.NaN;
                Assert.AreEqual(1, c.SafeMaxActorsPerTick);
                Assert.AreEqual(RecordingConfig.MaxAnimatorLayersCap, c.SafeMaxAnimatorLayers);
                Assert.AreEqual(RecordingConfig.DefaultBeepVolume, c.SafeBeepVolume);
                Assert.IsTrue(c.TryGetConfigProblem(out string p));
                StringAssert.Contains("maxActorsPerTick", p);
                StringAssert.Contains("maxAnimatorLayers", p);
                StringAssert.Contains("beepVolume", p);

                c.beepVolume = 4f;
                Assert.AreEqual(1f, c.SafeBeepVolume);
            }
            finally { Object.DestroyImmediate(c); }
        }

        [Test]
        public void Config_Missing_FallsBackToDefaults()
        {
            Assert.AreEqual(RecordingConfig.DefaultMaxClipSeconds, RecordingConfig.MaxClipSecondsOf(null));
            Assert.AreEqual(RecordingConfig.DefaultBeepVolume, RecordingConfig.BeepVolumeOf(null));
        }

        [Test]
        public void Config_TheShippedAsset_PassesItsOwnValidator()
        {
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + nameof(RecordingConfig));
            Assert.IsNotEmpty(guids, "no RecordingConfig asset found; it lives at Assets/Data/Recording/RecordingConfig.asset");
            foreach (string g in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<RecordingConfig>(path);
                Assert.IsFalse(asset.TryGetConfigProblem(out string p), $"{path}: {p}");
            }
        }

        // --- RecordedClip --------------------------------------------------------------------------------

        private sealed class FakeFootage : IFootage
        {
            public int Disposals;
            public float Seconds => IsDisposed ? 0f : 1f;
            public long Bytes => IsDisposed ? 0 : 100;
            public bool IsDisposed { get; private set; }
            public void Dispose() { if (IsDisposed) return; IsDisposed = true; Disposals++; }
        }

        [Test]
        public void Claim_FirstOwnerWins_SecondIsRefused()
        {
            var clip = new RecordedClip("clip_0001", new FakeFootage(), DateTime.UtcNow, 1f, StopReason.PlayerPressed);
            var a = new object();
            var b = new object();
            Assert.IsFalse(clip.IsClaimed);
            Assert.IsFalse(clip.Claim(null), "a null owner is not an owner");
            Assert.IsTrue(clip.Claim(a));
            Assert.IsFalse(clip.Claim(b), "a clip never has two owners");
            Assert.IsFalse(clip.Claim(a), "nor the same owner twice");
            Assert.AreSame(a, clip.Owner);
        }

        [Test]
        public void ClipId_IsFileNameSafeAndUtc()
        {
            string id = RecordedClip.MakeId(7, new DateTime(2026, 11, 15, 14, 3, 9, DateTimeKind.Utc));
            Assert.AreEqual("clip_0007_20261115T140309Z", id);
            Assert.AreEqual(-1, id.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()));
        }

        // --- ReplayBackend / ReplayFootage, edit mode, no scene -----------------------------------------

        private static Camera NewCamera(out GameObject go)
        {
            go = new GameObject("RecordingTests Camera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            return cam;
        }

        [Test]
        public void Footage_IsTheTicksTimeline_AndDisposeIsIdempotentAndReturnsTheStorage()
        {
            Camera cam = NewCamera(out GameObject go);
            var backend = new ReplayBackend(cam, null, null, 2, 2);
            try
            {
                backend.Prepare(2f);
                Assert.AreEqual(2, backend.StoragesFree);
                Assert.IsTrue(backend.Begin(0d, 2f));
                Assert.AreEqual(1, backend.StoragesFree, "a recording holds one storage block");

                cam.fieldOfView = 42f;
                backend.Sample(0.01);
                backend.Sample(0.5);    // 15 slots in one frame
                backend.Sample(0.51);   // nothing due
                var f = (ReplayFootage)backend.End();

                Assert.AreEqual(2, f.TickCount);
                Assert.AreEqual(1, f.TickAt(0).Repeats);
                Assert.AreEqual(15, f.TickAt(1).Repeats);
                Assert.AreEqual(16, f.Boundaries);
                Assert.AreEqual(16f / 30f, f.Seconds, 1e-6f);
                Assert.AreEqual(42f, f.TickAt(1).Camera.FieldOfView);
                Assert.IsFalse(f.HasSound, "no tap: the clip is flagged silent");
                Assert.AreEqual(0, f.PcmFrames);
                Assert.Greater(f.Bytes, 0);

                f.Dispose();
                Assert.IsTrue(f.IsDisposed);
                Assert.AreEqual(0, f.Bytes);
                Assert.AreEqual(0, f.TickCount, "a disposed footage reads as empty, never as a later clip");
                Assert.AreEqual(0f, f.Seconds);
                Assert.AreEqual(2, backend.StoragesFree, "disposal hands the block back");
                f.Dispose();
                Assert.AreEqual(2, backend.StoragesFree, "a second Dispose does nothing");
            }
            finally
            {
                backend.Destroy();
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Backend_StopsAtTheLimit_WithTheExactDuration()
        {
            Camera cam = NewCamera(out GameObject go);
            var backend = new ReplayBackend(cam, null, null, 2, 2);
            try
            {
                backend.Prepare(10f);
                Assert.IsTrue(backend.Begin(0d, 3f));
                for (double t = 0.01; t < 5.0; t += 1.0 / 60.0) backend.Sample(t);
                Assert.IsTrue(backend.TryGetStopRequest(out StopReason r));
                Assert.AreEqual(StopReason.LengthLimit, r);
                IFootage f = backend.End();
                Assert.AreEqual(3f, f.Seconds, 1e-6f, "a clip that reaches the limit is exactly the limit long");
                f.Dispose();
            }
            finally
            {
                backend.Destroy();
                Object.DestroyImmediate(go);
            }
        }

        // Code review 2026-11-15: a clip stopped in the frame it started (before any Sample) was announced with 0 ticks
        // and 0 s, and TickAt(0) threw (reproduced in SampleScene through ToggleRecording + SetPhotoMode(false)).
        [Test]
        public void Backend_ClipStoppedBeforeItsFirstSample_StillHasItsStartTick()
        {
            Camera cam = NewCamera(out GameObject go);
            var backend = new ReplayBackend(cam, null, null, 2, 2);
            try
            {
                backend.Prepare(2f);
                cam.fieldOfView = 37f;
                Assert.IsTrue(backend.Begin(0d, 2f));
                var f = (ReplayFootage)backend.End();   // no Sample in between

                Assert.AreEqual(1, f.TickCount, "the start tick");
                Assert.AreEqual(1, f.TickAt(0).Repeats);
                Assert.AreEqual(1, f.Boundaries);
                Assert.AreEqual(1f / 30f, f.Seconds, 1e-6f, "one 1/30-s slot, not 0 s");
                Assert.AreEqual(37f, f.TickAt(0).Camera.FieldOfView, "the camera as the player last saw it");
                f.Dispose();
            }
            finally
            {
                backend.Destroy();
                Object.DestroyImmediate(go);
            }
        }

        // Code review 2026-11-15: the tick array is reused clip after clip, and a tick sampled after the camera was
        // destroyed left the slot untouched, so it replayed an OLDER clip's camera.
        [Test]
        public void Backend_CameraDestroyedMidClip_NeverShowsAnOlderClipsCamera()
        {
            Camera cam = NewCamera(out GameObject go);
            var backend = new ReplayBackend(cam, null, null, 2, 2);
            try
            {
                backend.Prepare(2f);
                cam.fieldOfView = 42f;
                Assert.IsTrue(backend.Begin(0d, 2f));
                backend.Sample(0.01);
                backend.End().Dispose();               // clip 1 hands its block back: clip 2 reuses it

                Assert.IsTrue(backend.Begin(0d, 2f));
                Object.DestroyImmediate(go);
                backend.Sample(0.01);
                var f = (ReplayFootage)backend.End();
                Assert.AreEqual(1, f.TickCount);
                Assert.AreEqual(0f, f.TickAt(0).Camera.FieldOfView, "a defined 'no camera' sample, not clip 1's 42");
                f.Dispose();
            }
            finally
            {
                backend.Destroy();
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        // --- The listener tap through the backend's End (code review 2026-11-15) -----------------------------------
        //
        // The tap's OnAudioFilterRead is driven by reflection with a known block: it places the block by
        // AudioSettings.dspTime read inside the callback, so a clip armed at "now" receives it within a block or two of
        // its start, well inside the 1-s clip below.

        private const float BlockLevel = 0.5f;

        private static void FeedBlock(ListenerTap tap, int channels, int frames = 1024)
        {
            var data = new float[frames * channels];
            for (int i = 0; i < data.Length; i++) data[i] = BlockLevel;
            typeof(ListenerTap).GetMethod("OnAudioFilterRead",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(tap, new object[] { data, channels });
        }

        private static int NonZeroSamples(ReplayFootage f)
        {
            var buf = new short[f.PcmFrames * f.Channels];
            int frames = f.ReadPcm(0, buf);
            int nz = 0;
            for (int i = 0; i < frames * f.Channels; i++) if (buf[i] != 0) nz++;
            return nz;
        }

        private static ReplayBackend TapBackend(out GameObject camGo, out GameObject tapGo, out ListenerTap tap)
        {
            Camera cam = NewCamera(out camGo);
            tapGo = new GameObject("RecordingTests Tap");
            tap = tapGo.AddComponent<ListenerTap>();
            var backend = new ReplayBackend(cam, null, tap, 2, 2);
            backend.Prepare(2f);
            Assume.That(AudioSettings.outputSampleRate, Is.GreaterThan(0), "needs an audio device");
            Assume.That(backend.Begin(AudioSettings.dspTime, 2f), Is.True);
            Assume.That(tap.IsArmed, Is.True, "the backend armed the tap");
            return backend;
        }

        [Test]
        public void Tap_ChannelChangeMidClip_KeepsTheSoundRecordedBeforeIt()
        {
            ReplayBackend backend = TapBackend(out GameObject camGo, out GameObject tapGo, out ListenerTap tap);
            try
            {
                int ch = backend.Channels;
                FeedBlock(tap, ch);                    // real sound
                FeedBlock(tap, ch == 1 ? 2 : 1);       // the speaker mode changed
                Assert.IsTrue(tap.ChannelMismatch);
                backend.Sample(0.01);
                backend.Sample(1.0);
                var f = (ReplayFootage)backend.End();

                Assert.IsTrue(f.HasSound, "the sound before the change is kept");
                Assert.AreEqual(FootageFormat.PcmFramesFor(f.Boundaries, f.SampleRate), f.PcmFrames, "padded to the ticks");
                Assert.AreEqual(1024 * ch, NonZeroSamples(f), "exactly the block before the change, the rest silence");
                f.Dispose();
            }
            finally
            {
                backend.Destroy();
                Object.DestroyImmediate(tapGo);
                Object.DestroyImmediate(camGo);
            }
        }

        [Test]
        public void Tap_DestroyedBeforeTheStop_KeepsTheSoundItWrote()
        {
            ReplayBackend backend = TapBackend(out GameObject camGo, out GameObject tapGo, out ListenerTap tap);
            try
            {
                FeedBlock(tap, backend.Channels);
                Object.DestroyImmediate(tapGo);       // Main Camera torn down before the recorder
                backend.Sample(0.01);
                backend.Sample(1.0);
                var f = (ReplayFootage)backend.End();

                Assert.IsTrue(f.HasSound);
                Assert.AreEqual(1024 * backend.Channels, NonZeroSamples(f), "the written block survives the tap");
                f.Dispose();
            }
            finally
            {
                backend.Destroy();
                if (tapGo != null) Object.DestroyImmediate(tapGo);
                Object.DestroyImmediate(camGo);
            }
        }

        [Test]
        public void Tap_ThatHeardNothing_GivesAClipFlaggedSilent()
        {
            ReplayBackend backend = TapBackend(out GameObject camGo, out GameObject tapGo, out ListenerTap tap);
            try
            {
                backend.Sample(0.01);                  // no block ever arrives (the AudioListener is disabled)
                backend.Sample(1.0);
                var f = (ReplayFootage)backend.End();

                Assert.IsFalse(f.HasSound, "padding alone is not sound");
                Assert.AreEqual(0, f.PcmFrames);
                Assert.Greater(f.Seconds, 0.9f, "the timeline is still valid");
                f.Dispose();
            }
            finally
            {
                backend.Destroy();
                Object.DestroyImmediate(tapGo);
                Object.DestroyImmediate(camGo);
            }
        }

        [Test]
        public void Backend_SecondBeginWhileRecording_IsRefused()
        {
            Camera cam = NewCamera(out GameObject go);
            var backend = new ReplayBackend(cam, null, null, 1, 1);
            try
            {
                backend.Prepare(1f);
                Assert.IsTrue(backend.Begin(0d, 1f));
                Assert.IsFalse(backend.Begin(0d, 1f));
                backend.Abort();
                Assert.AreEqual(2, backend.StoragesFree, "an aborted clip hands its block back");
            }
            finally
            {
                backend.Destroy();
                Object.DestroyImmediate(go);
            }
        }

        // AC6: "recording allocates nothing on the managed heap per frame". This is the AC itself, not a performance
        // threshold.
        //
        // ⚠️ HOW IT CAN SEE AN ALLOCATION AT ALL. In Unity's Mono, GC.GetAllocatedBytesForCurrentThread() reads 0
        // around a new byte[1000] (measured 2026-11-15: the first version of this test used it and could not fail),
        // and GC.GetTotalMemory moves in 4 KB blocks. So the path runs 9,000 times, each taking a full tick: any
        // managed allocation is at least 16 B, so an allocating path would add at least 144 KB, ~35 blocks. The
        // positive control below proves the same measurement sees exactly that.
        private const int Calls = 9000;

        private static long HeapGrowth(Action body, out int collections)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gc0 = GC.CollectionCount(0);
            long before = GC.GetTotalMemory(false);
            body();
            long after = GC.GetTotalMemory(false);
            collections = GC.CollectionCount(0) - gc0;
            return after - before;
        }

        [Test]
        public void Sampling_AllocatesNothing()
        {
            Camera cam = NewCamera(out GameObject go);
            var backend = new ReplayBackend(cam, null, null, 2, 2);
            try
            {
                backend.Prepare(Calls / 30f + 1f);
                backend.Begin(0d, Calls / 30f + 1f);
                backend.Sample(0.001);   // first call outside the measurement (JIT)
                long grown = HeapGrowth(() =>
                {
                    for (int i = 1; i < Calls; i++)
                    {
                        backend.Sample(i / 30.0 + 0.001);   // a tick is due on every call: the full path
                        backend.TryGetStopRequest(out _);
                        _ = backend.RecordedSeconds;
                    }
                }, out int collections);
                Assert.AreEqual(0, collections, "a collection during the loop would hide allocations");
                Assert.Less(grown, 4096, $"heap grew {grown} B over {Calls} sampled frames (any allocation would be >= {Calls * 16} B)");
                IFootage f = backend.End();
                Assert.AreEqual(Calls, ((ReplayFootage)f).TickCount, "every call took a tick: the full path ran");
                f.Dispose();
            }
            finally
            {
                backend.Destroy();
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Sampling_AllocationCheck_PositiveControl()
        {
            // The same measurement around the same number of calls that DO allocate must move far beyond the 4 KB bound
            // the zero test uses. Mono's GetTotalMemory undercounts (it read 69,632 B for 9,000 new object(), ~144 KB
            // real, on 2026-11-15), so the bar is "clearly more than a few blocks", not the exact byte count.
            var keep = new object[Calls];
            long grown = HeapGrowth(() => { for (int i = 0; i < Calls; i++) keep[i] = new object(); }, out int collections);
            Assert.AreEqual(0, collections);
            Assert.Greater(grown, 4 * 4096, $"the measurement saw only {grown} B for {Calls} allocations");
            GC.KeepAlive(keep);
        }
    }
}
