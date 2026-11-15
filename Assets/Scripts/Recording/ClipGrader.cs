using System;
using System.Collections.Generic;
using UnityEngine;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.Grading;

namespace CameraGame.Recording
{
    /// <summary>
    /// Grades a clip while it records (Story 1.16, FR4–FR7). Owned and driven by <see cref="ClipRecorder"/>
    /// exactly as it owns its <see cref="ReplayBackend"/>: <see cref="Begin"/> with the clip,
    /// <see cref="SampleTick"/> on every footage tick the backend takes, <see cref="Finish"/> inside
    /// <c>ClipRecorder.Stop</c> before the clip is announced, so the grade travels on the clip itself
    /// (game-architecture.md §Clip Recording: "ClipRecorder.Stop → ClipGrader → ClipRecordedChannel.Raise").
    ///
    /// ⚠️ IT DOES NOT GRADE ANYTHING ITSELF. Every sample is a call to the unchanged per-frame grader,
    /// <see cref="ShotGrader.Grade(Camera, ISubject, GradingConfig, out GradeDetail)"/>, through the recorder's
    /// camera, for every live actor (AC1). From each verdict it keeps only the gate (Counted, MissReason) and
    /// Composition01; the photo's Timing01 and Percent01 are NOT used, because a clip replaces timing with peak
    /// coverage (sprint-change-proposal-2026-11-10.md:101). The aggregation and the blend are the pure
    /// <see cref="ClipGradeBuilder"/> in Grading, fed plain values, so the dependency stays Recording → Grading
    /// and the EditMode tests and the measuring sweep run the very code the game runs.
    ///
    /// ONE SAMPLE PER FOOTAGE TICK, weighted by the tick's repeats (decision 1b), so the grade describes exactly
    /// the frames the footage holds: the seconds it graded equal <c>IFootage.Seconds</c>. Above 30 fps it grades
    /// no frame the footage does not contain; below, one tick stands for several slots and weighs as much.
    ///
    /// Main thread only (ShotGrader's static buffers). No allocation per tick: no LINQ, boxing, strings,
    /// GetComponent or closures (AC5). Fail-soft: a missing config or event manager grades every clip "not
    /// graded"; anything that throws is caught, logged once, and that clip reads "not graded". Nothing throws
    /// into the recorder's LateUpdate.
    /// </summary>
    public sealed class ClipGrader
    {
        private readonly Camera _camera;
        private readonly EventManager _events;
        private readonly GradingConfig _config;
        private readonly int _maxActors;
        private readonly ClipGradeBuilder _builder;
        private readonly bool _canGrade;

        private bool _failed;
        private bool _failureLogged;
        private bool _overflowWarned;
        private bool _actorCapWarned;
        private bool _noPeakWarned;

        /// <summary>False when grading cannot run at all (no config, no event manager or no camera): every clip
        /// then reads "not graded". The recorder said why, once, at Awake.</summary>
        public bool CanGrade => _canGrade;

        /// <summary>Footage ticks graded since <see cref="Begin"/>.</summary>
        public int TicksGraded => _builder.TicksGraded;

        /// <summary>Seconds of footage graded since <see cref="Begin"/>.</summary>
        public double GradedSeconds => _builder.GradedSeconds;

        /// <summary>The config clips are graded against (PhotoModeController checks it is its own).</summary>
        public GradingConfig Config => _config;

#if UNITY_EDITOR
        /// <summary>One graded sample, for the verification rig only (editor builds). Raised after the sample is
        /// folded; with no subscriber it costs one null check.</summary>
        public struct TickRecord
        {
            public int Tick;
            public int Repeats;
            public EventActor Actor;
            public ShotGrade Grade;
            public GradeDetail Detail;
            public ClipSample Sample;
            public float PeakOffset;
        }

        /// <summary>Raised for every graded sample (editor only; the Grade Clip rig logs ticks with it).</summary>
        public event Action<TickRecord> TickGraded;

        /// <summary>Raised once per graded tick after it closes, with (tick number, repeats, samples on it), so a
        /// rig also sees the ticks on which nobody was live (editor only).</summary>
        public event Action<int, int, int> TickClosed;
#endif

        public ClipGrader(Camera camera, EventManager events, GradingConfig config, int maxActorsPerTick)
        {
            _camera = camera;
            _events = events;
            _config = config;
            _maxActors = maxActorsPerTick < 1 ? 1 : maxActorsPerTick;
            _builder = new ClipGradeBuilder(Mathf.Max(ClipGradeBuilder.DefaultCapacity, _maxActors * 8));
            _canGrade = config != null && events != null && camera != null;
        }

        /// <summary>Starts grading a new clip: every accumulator back to zero.</summary>
        public void Begin()
        {
            _builder.Begin();
            _failed = false;
        }

        /// <summary>Grades one footage tick of <paramref name="repeats"/> 1/30-s slots: every live actor through
        /// <see cref="ShotGrader"/>. Call it on exactly the frames the backend took a tick.</summary>
        public void SampleTick(int repeats)
        {
            if (!_canGrade || _failed || repeats <= 0) return;

            try
            {
                _builder.BeginTick(repeats / (double)FootageFormat.TickRate);
                int graded = 0;

                // Read LIVE every tick and never cached: actors are pooled (ISubject's liveness contract). `_events
                // != null` is Unity's check, so a manager destroyed first at a scene unload grades nobody.
                if (_events != null)
                {
                    IReadOnlyList<EventActor> actors = _events.ActiveActors;
                    for (int i = 0; i < actors.Count; i++)
                    {
                        EventActor actor = actors[i];

                        // ActiveActors tracks the lifecycle, not activation: an inactive pooled actor still
                        // reports plausible bounds (PhotoModeController.GradeBestSubject, the same filter).
                        if (actor == null || !actor.isActiveAndEnabled) continue;

                        if (graded >= _maxActors)
                        {
                            if (!_actorCapWarned)
                            {
                                _actorCapWarned = true;
                                GameLog.Warn("Grading", $"More event actors are live than a clip grades per tick ({_maxActors}); " +
                                                        "the extra ones are not graded (RecordingConfig.maxActorsPerTick). Said once.");
                            }
                            break;
                        }

                        ShotGrade g = ShotGrader.Grade(_camera, actor, _config, out GradeDetail detail);
                        float peak = actor.PeakDuration;
                        var s = new ClipSample
                        {
                            Lifecycle = actor.LifecycleSerial,
                            SubjectId = actor.SubjectId,
                            Counted = g.Counted,
                            Miss = g.MissReason,
                            Composition01 = g.Composition01,
                            ActorTime = -actor.TimeToPeak,
                            PeakDuration = peak,
                            AtPeak = actor.IsAtPeak,
                        };
                        _builder.Add(in s);
                        graded++;

                        if (!(peak > 0f) && !_noPeakWarned)
                        {
                            _noPeakWarned = true;
                            GameLog.Warn("Grading", $"'{s.SubjectId}' has no peak window (its Peak phase lasts {peak} s): " +
                                                    "clips of it are graded on framing only, with no peak coverage. Said once.");
                        }

#if UNITY_EDITOR
                        TickGraded?.Invoke(new TickRecord
                        {
                            Tick = _builder.TicksGraded, Repeats = repeats, Actor = actor, Grade = g, Detail = detail,
                            Sample = s, PeakOffset = actor.PeakOffset,
                        });
#endif
                    }
                }

                _builder.EndTick();
#if UNITY_EDITOR
                TickClosed?.Invoke(_builder.TicksGraded, repeats, graded);
#endif

                if (_builder.Overflowed && !_overflowWarned)
                {
                    _overflowWarned = true;
                    GameLog.Warn("Grading", $"A clip held more lifecycles than its grader can tell apart ({_builder.Capacity}); " +
                                            "the weakest were dropped from the grade. Said once.");
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>The clip's grade. "Not graded" when grading cannot run or failed during this clip.</summary>
        public ClipGrade Finish()
        {
            if (!_canGrade) return ClipGrade.NotGraded();
            if (_failed) return ClipGrade.NotGraded((float)_builder.GradedSeconds);
            try
            {
                return _builder.Finish(_config);
            }
            catch (Exception e)
            {
                Fail(e);
                return ClipGrade.NotGraded((float)_builder.GradedSeconds);
            }
        }

        private void Fail(Exception e)
        {
            _failed = true;
            if (_failureLogged) return;
            _failureLogged = true;
            GameLog.Error("Grading", "Clip grading failed: this clip is recorded and announced, and reads NOT GRADED. " +
                                     $"Said once per session. {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
    }
}
