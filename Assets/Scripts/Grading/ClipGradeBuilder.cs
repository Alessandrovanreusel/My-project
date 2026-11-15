using System;

namespace CameraGame.Grading
{
    /// <summary>How peak coverage and framing combine into a clip's percentage (Story 1.16, decision 1d).</summary>
    public enum ClipBlendShape
    {
        /// <summary><c>framing × (floor + (1 − floor) × coverage)</c>: coverage scales the framing, and a clip that
        /// missed the peak keeps <c>floor</c> of its framing. Story 1.13's proposal with coverage for timing.</summary>
        FloorTimesCoverage,

        /// <summary><c>floor × framing + (1 − floor) × coverage</c>: coverage earns marks of its own, whatever the
        /// framing was. Kept for the measuring sweep.</summary>
        Additive,
    }

    /// <summary>Which ticks the framing mean is taken over (decision 1d).</summary>
    public enum ClipFramingSource
    {
        /// <summary>Every tick on which this lifecycle passed the gate.</summary>
        GatedTicks,

        /// <summary>Only the gated ticks inside the peak window, when the clip covered any of it (otherwise every
        /// gated tick). Kept for the measuring sweep.</summary>
        PeakTicksWhenCovered,
    }

    /// <summary>How a clip that never caught anyone names its miss reason (decision 1e).</summary>
    public enum ClipMissRule
    {
        /// <summary>The reason that held for the most clip time, ties toward the gate closest to passing.</summary>
        MostTime,

        /// <summary>The gate closest to passing that any tick reached. Kept for the measuring sweep.</summary>
        DeepestGate,
    }

    /// <summary>What "footage without the subject" means for the presence term (Story 1.16, decided by measuring).</summary>
    public enum ClipPresence
    {
        /// <summary>The share of the clip in which ANY subject passed the gate: empty footage is footage with nobody
        /// gradeable in it. A clip filmed continuously across a respawn keeps its in-shot share.</summary>
        AnySubject,

        /// <summary>The share in which the graded lifecycle itself passed the gate: a respawn's earlier lifecycle,
        /// framed well, counts as absence. Kept for the measuring sweep.</summary>
        Lifecycle,
    }

    /// <summary>The resolved blend a clip is graded with. <see cref="GradingConfig.SafeClipBlend"/> builds the
    /// shipped one; the measuring sweep builds others to compare candidates through the same code.</summary>
    public readonly struct ClipBlend
    {
        public readonly float Floor;
        public readonly float AbsenceWeight;
        public readonly ClipBlendShape Shape;
        public readonly ClipFramingSource FramingSource;
        public readonly ClipMissRule MissRule;
        public readonly ClipPresence Presence;

        public ClipBlend(float floor, float absenceWeight, ClipBlendShape shape, ClipFramingSource framingSource,
                         ClipMissRule missRule, ClipPresence presence = ClipPresence.AnySubject)
        {
            Floor = ClipGrade.Sane(floor);
            AbsenceWeight = ClipGrade.Sane(absenceWeight);
            Shape = shape;
            FramingSource = framingSource;
            MissRule = missRule;
            Presence = presence;
        }
    }

    /// <summary>The clip blend as pure arithmetic (Story 1.16). No state, no Unity objects.</summary>
    public static class ClipScoring
    {
        /// <summary>The framing factor: mean composition times the presence term
        /// <c>1 − absenceWeight × (1 − inShot)</c>. Absence weight 0 makes footage without him cost nothing.</summary>
        public static float Framing(float composition01, float inShot01, float absenceWeight)
        {
            float c = ClipGrade.Sane(composition01);
            float w = ClipGrade.Sane(absenceWeight);
            float s = ClipGrade.Sane(inShot01);
            return ClipGrade.Sane(c * (1f - w * (1f - s)));
        }

        /// <summary>The clip's percentage from its framing factor and its peak coverage. See
        /// <see cref="ClipBlendShape"/>. Every input is sanitised, so NaN or an out-of-range value can never
        /// reach the readout.</summary>
        public static float Percent(float framing01, float coverage01, in ClipBlend blend)
        {
            float f = ClipGrade.Sane(framing01);
            float c = ClipGrade.Sane(coverage01);
            float floor = blend.Floor;
            float p = blend.Shape == ClipBlendShape.Additive
                ? floor * f + (1f - floor) * c
                : f * (floor + (1f - floor) * c);
            return ClipGrade.Sane(p);
        }

        /// <summary>How close a miss came to passing: the gates in the order <see cref="ShotGrader"/> runs them,
        /// deepest last. Used to break ties between miss reasons toward the one a player can act on.</summary>
        public static int GateDepth(GradeMiss miss)
        {
            switch (miss)
            {
                case GradeMiss.Occluded:         return 7;
                case GradeMiss.TooSmall:         return 6;
                case GradeMiss.BehindCamera:     return 5;
                case GradeMiss.OutsideFrustum:   return 4;
                case GradeMiss.DegenerateBounds: return 3;
                case GradeMiss.NoViewport:       return 2;
                case GradeMiss.NoSubject:        return 1;
                default:                         return 0;
            }
        }
    }

    /// <summary>
    /// One live subject on one footage tick, as plain values: what <see cref="ShotGrader"/> said about him and
    /// where his own clock stood (Story 1.16). Everything a clip grade is built from, and nothing that needs a
    /// scene, so the measuring sweep can replay a logged clip through <see cref="ClipGradeBuilder"/> exactly.
    /// </summary>
    public struct ClipSample
    {
        /// <summary><c>EventActor.LifecycleSerial</c>: unique across every actor and every cycle, so it is the
        /// candidate's whole key. A respawn of the same pooled actor is a new lifecycle and a new candidate.</summary>
        public int Lifecycle;

        public string SubjectId;

        /// <summary>The per-frame grader's verdict: passed every gate.</summary>
        public bool Counted;

        /// <summary>The gate that rejected him (<see cref="GradeMiss.None"/> when <see cref="Counted"/>).</summary>
        public GradeMiss Miss;

        /// <summary><see cref="ShotGrade.Composition01"/> on this tick.</summary>
        public float Composition01;

        /// <summary>The subject's own clock: <c>−ISubject.TimeToPeak</c>, so the peak window is
        /// [0, <see cref="PeakDuration"/>) on it. On the actor's clock, not the clip's: the peak runs on scaled time
        /// and the clip on real time (decision 1c).</summary>
        public float ActorTime;

        /// <summary><c>ISubject.PeakDuration</c>; 0 when he has no peak to cover.</summary>
        public float PeakDuration;

        /// <summary><c>ISubject.IsAtPeak</c> on this tick (only the "framing over the peak" sweep candidate reads it).</summary>
        public bool AtPeak;
    }

    /// <summary>
    /// Folds a clip's ticks into its <see cref="ClipGrade"/> (Story 1.16). Pure: plain numbers in, a grade out,
    /// no Unity object, no clock, no log line. <c>CameraGame.Recording.ClipGrader</c> feeds it live from the
    /// recorder; the EditMode tests and the measuring sweep feed it directly.
    ///
    /// ================================================================================================
    /// CANDIDATES: ONE PER LIFECYCLE, NEVER MERGED (decision 1e)
    /// ================================================================================================
    /// Every lifecycle seen live in the clip is graded on its own ticks only, keyed by its serial. The clip's
    /// grade is the best candidate by <see cref="GradeRanking.Beats"/>: counted beats a miss, then the higher
    /// percent, and the first one seen keeps a tie. A candidate counts if ANY of its ticks passed the gate.
    ///
    /// ================================================================================================
    /// PEAK COVERAGE ON THE SUBJECT'S OWN CLOCK (decision 1c)
    /// ================================================================================================
    /// Each tick of a lifecycle stands for the stretch of that lifecycle's clock since its previous tick
    /// (a footage tick shows the slots whose boundaries passed since the tick before it). Coverage is the length
    /// of those stretches, on ticks that passed the gate, that falls inside [0, peak length], divided by the
    /// peak length. Consecutive stretches join end to end, so a clip that holds the whole window with him framed
    /// throughout reads EXACTLY 1, whatever the frame rate and wherever the tick boundaries fall: counting
    /// 1/30-s ticks instead reads 44/45 or 46/45 depending on where the phase boundary lands between ticks, and
    /// 2.2 % costs a star at the 0.90 boundary. On the subject's clock, a timeScale other than 1 measures the
    /// window as the subject lives it. A lifecycle's first tick in the clip stands for one tick's length before it.
    ///
    /// Bounded: <see cref="Capacity"/> candidates, preallocated. No allocation per tick, nothing that grows with
    /// the clip's length. Reusable: <see cref="Begin"/> resets every accumulator (this project's bugs hide in the
    /// second cycle).
    /// </summary>
    public sealed class ClipGradeBuilder
    {
        private struct Candidate
        {
            public int Serial;
            public int Order;
            public string SubjectId;
            public float PeakDuration;
            public double LiveSeconds;
            public double GatedSeconds;
            public double CompositionSeconds;
            public double PeakGatedSeconds;
            public double PeakCompositionSeconds;
            public double Covered;
            public double Held;
            public double SpanStart;
            public double SpanEnd;
            public float PrevActorTime;
            public bool HasPrev;
            public int CountedTicks;
            public int TouchedTick;
        }

        /// <summary>The default number of lifecycles one clip can tell apart. A 120-s clip of the Town Drunk
        /// (a 26.5-s cycle) holds at most 5; the recorder sizes it from <c>RecordingConfig.maxActorsPerTick</c>.</summary>
        public const int DefaultCapacity = 32;

        private readonly Candidate[] _candidates;
        private int _count;
        private int _nextOrder;

        private readonly double[] _missSeconds;
        private readonly string[] _missSubject;

        private double _gradedSeconds;
        private double _anyGatedSeconds;
        private int _ticks;
        private bool _inTick;
        private double _tickSeconds;
        private bool _tickCounted;
        private bool _tickHasMiss;
        private GradeMiss _tickMiss;
        private string _tickMissSubject;

        /// <summary>How many lifecycles this builder can tell apart in one clip.</summary>
        public int Capacity => _candidates.Length;

        /// <summary>Ticks folded since <see cref="Begin"/>.</summary>
        public int TicksGraded => _ticks;

        /// <summary>Seconds of footage folded since <see cref="Begin"/> (the sum of the ticks' lengths).</summary>
        public double GradedSeconds => _gradedSeconds;

        /// <summary>Lifecycles seen since <see cref="Begin"/> (at most <see cref="Capacity"/>).</summary>
        public int CandidateCount => _count;

        /// <summary>True once a lifecycle had to replace the weakest candidate, or a sample was dropped, because
        /// more lifecycles were seen than <see cref="Capacity"/>. The caller warns once.</summary>
        public bool Overflowed { get; private set; }

        public ClipGradeBuilder(int capacity = DefaultCapacity)
        {
            _candidates = new Candidate[capacity < 1 ? 1 : capacity];
            int reasons = 0;
            foreach (GradeMiss m in Enum.GetValues(typeof(GradeMiss)))
                if ((int)m + 1 > reasons) reasons = (int)m + 1;
            _missSeconds = new double[reasons];
            _missSubject = new string[reasons];
            Begin();
        }

        /// <summary>Starts a new clip: every accumulator back to zero.</summary>
        public void Begin()
        {
            for (int i = 0; i < _candidates.Length; i++) _candidates[i] = default;
            _count = 0;
            _nextOrder = 0;
            Array.Clear(_missSeconds, 0, _missSeconds.Length);
            Array.Clear(_missSubject, 0, _missSubject.Length);
            _gradedSeconds = 0d;
            _anyGatedSeconds = 0d;
            _ticks = 0;
            _inTick = false;
            Overflowed = false;
        }

        /// <summary>Opens one footage tick of <paramref name="seconds"/> (its repeats ÷ 30). Non-finite or
        /// negative lengths count as 0.</summary>
        public void BeginTick(double seconds)
        {
            if (_inTick) EndTick();
            _inTick = true;
            _tickSeconds = seconds > 0d && !double.IsInfinity(seconds) ? seconds : 0d;
            _tickCounted = false;
            _tickHasMiss = false;
            _tickMiss = GradeMiss.Unevaluated;
            _tickMissSubject = null;
        }

        /// <summary>Folds one live subject's sample into the open tick. False when it was dropped (no tick open,
        /// or more lifecycles on this one tick than <see cref="Capacity"/>).</summary>
        public bool Add(in ClipSample s)
        {
            if (!_inTick) return false;

            int idx = Find(s.Lifecycle);
            if (idx < 0) idx = Open(s.Lifecycle, s.SubjectId);
            if (idx < 0) { Overflowed = true; return false; }

            ref Candidate c = ref _candidates[idx];
            c.TouchedTick = _ticks + 1;
            if (!string.IsNullOrEmpty(s.SubjectId)) c.SubjectId = s.SubjectId;

            float d = s.PeakDuration;
            c.PeakDuration = d > 0f && !float.IsInfinity(d) ? d : 0f;

            double w = _tickSeconds;
            double b = Finite(s.ActorTime);
            double a = c.HasPrev ? c.PrevActorTime : b - w;
            if (b < a) a = b;
            if (!c.HasPrev) c.SpanStart = a;
            c.SpanEnd = b;
            c.PrevActorTime = (float)b;
            c.HasPrev = true;

            double inWindow = 0d;
            if (c.PeakDuration > 0f)
            {
                double lo = a > 0d ? a : 0d;
                double hi = b < c.PeakDuration ? b : c.PeakDuration;
                if (hi > lo) inWindow = hi - lo;
            }
            c.Held += inWindow;
            c.LiveSeconds += w;

            if (s.Counted)
            {
                float comp = ClipGrade.Sane(s.Composition01);
                c.CountedTicks++;
                c.Covered += inWindow;
                c.GatedSeconds += w;
                c.CompositionSeconds += w * comp;
                if (s.AtPeak)
                {
                    c.PeakGatedSeconds += w;
                    c.PeakCompositionSeconds += w * comp;
                }
                _tickCounted = true;
            }
            else
            {
                GradeMiss m = s.Miss == GradeMiss.None ? GradeMiss.Unevaluated : s.Miss;
                if (!_tickHasMiss || ClipScoring.GateDepth(m) > ClipScoring.GateDepth(_tickMiss))
                {
                    _tickMiss = m;
                    _tickMissSubject = s.SubjectId;
                    _tickHasMiss = true;
                }
            }
            return true;
        }

        /// <summary>Closes the open tick. A tick on which nobody passed the gate adds its length to the miss
        /// reason closest to passing among its samples (decision 1e).</summary>
        public void EndTick()
        {
            if (!_inTick) return;
            _inTick = false;
            _ticks++;
            _gradedSeconds += _tickSeconds;
            if (_tickCounted) _anyGatedSeconds += _tickSeconds;

            if (!_tickCounted && _tickHasMiss)
            {
                int r = (int)_tickMiss;
                if (r >= 0 && r < _missSeconds.Length)
                {
                    _missSeconds[r] += _tickSeconds;
                    if (!string.IsNullOrEmpty(_tickMissSubject)) _missSubject[r] = _tickMissSubject;
                }
            }
        }

        /// <summary>The clip's grade under the config's shipped blend and star scale.</summary>
        public ClipGrade Finish(GradingConfig cfg) =>
            cfg == null ? ClipGrade.NotGraded((float)_gradedSeconds) : Finish(cfg.SafeClipBlend, cfg.SafeStarScale);

        /// <summary>The clip's grade under <paramref name="blend"/>. Leaves the accumulators untouched, so the
        /// measuring sweep can score one clip under several blends.</summary>
        public ClipGrade Finish(in ClipBlend blend, StarScale scale)
        {
            if (_inTick) EndTick();
            float clipSeconds = (float)_gradedSeconds;

            bool haveBest = false;
            ClipGrade best = default;
            for (int order = 0; order < _nextOrder; order++)
            {
                int i = IndexOfOrder(order);
                if (i < 0) continue;
                ref Candidate c = ref _candidates[i];
                if (c.CountedTicks <= 0) continue;

                ClipGrade g = Score(ref c, in blend, scale, _gradedSeconds, _anyGatedSeconds);
                if (!haveBest || GradeRanking.Beats(g.Counted, g.Percent01, best.Counted, best.Percent01))
                {
                    best = g;
                    haveBest = true;
                }
            }
            if (haveBest) return best;

            GradeMiss reason = MissReason(blend.MissRule);
            if (reason == GradeMiss.Unevaluated)
                return _ticks > 0 ? ClipGrade.Missed(GradeMiss.NoSubject, clipSeconds)
                                  : ClipGrade.NotGraded(clipSeconds);
            int ri = (int)reason;
            return ClipGrade.Missed(reason, clipSeconds, ri >= 0 && ri < _missSubject.Length ? _missSubject[ri] : null);
        }

        private static ClipGrade Score(ref Candidate c, in ClipBlend blend, StarScale scale, double clipSeconds,
                                       double anyGatedSeconds)
        {
            double composition = c.GatedSeconds > 0d ? c.CompositionSeconds / c.GatedSeconds : 0d;
            double framingMean = composition;
            if (blend.FramingSource == ClipFramingSource.PeakTicksWhenCovered && c.PeakGatedSeconds > 0d)
                framingMean = c.PeakCompositionSeconds / c.PeakGatedSeconds;

            double present = blend.Presence == ClipPresence.Lifecycle ? c.GatedSeconds : anyGatedSeconds;
            float inShot = clipSeconds > 0d ? (float)(present / clipSeconds) : 0f;
            float framing = ClipScoring.Framing((float)framingMean, inShot, blend.AbsenceWeight);

            bool hasPeak = c.PeakDuration > 0f;
            float coverage = 0f, before = 0f, after = 0f;
            if (hasPeak)
            {
                double dur = c.PeakDuration;
                coverage = (float)(c.Covered / dur);
                double s = c.SpanStart < dur ? c.SpanStart : dur;
                before = s > 0d ? (float)(s / dur) : 0f;
                double e = c.SpanEnd > 0d ? c.SpanEnd : 0d;
                after = e < dur ? (float)((dur - e) / dur) : 0f;
            }

            float percent = ClipScoring.Percent(framing, coverage, in blend);
            return ClipGrade.Scored(percent, scale.StarsFor(percent), (float)composition, framing, coverage, before,
                                    after, inShot, hasPeak, (float)clipSeconds, c.SubjectId);
        }

        private GradeMiss MissReason(ClipMissRule rule)
        {
            GradeMiss best = GradeMiss.Unevaluated;
            double bestSeconds = 0d;
            for (int r = 0; r < _missSeconds.Length; r++)
            {
                double s = _missSeconds[r];
                if (!(s > 0d)) continue;
                var m = (GradeMiss)r;
                bool better;
                if (best == GradeMiss.Unevaluated) better = true;
                else if (rule == ClipMissRule.DeepestGate)
                    better = ClipScoring.GateDepth(m) > ClipScoring.GateDepth(best);
                else
                    better = s > bestSeconds
                             || (s == bestSeconds && ClipScoring.GateDepth(m) > ClipScoring.GateDepth(best));
                if (better) { best = m; bestSeconds = s; }
            }
            return best;
        }

        private int Find(int serial)
        {
            for (int i = 0; i < _count; i++)
                if (_candidates[i].Serial == serial) return i;
            return -1;
        }

        private int IndexOfOrder(int order)
        {
            for (int i = 0; i < _count; i++)
                if (_candidates[i].Order == order) return i;
            return -1;
        }

        private int Open(int serial, string subjectId)
        {
            int idx;
            if (_count < _candidates.Length) idx = _count++;
            else
            {
                // Full: replace the weakest lifecycle not seen on this tick, ranked as if the clip ended now.
                // Only reachable with more lifecycles in one clip than Capacity; the caller warns once.
                Overflowed = true;
                idx = -1;
                ClipGrade worst = default;
                ClipBlend neutral = new ClipBlend(0f, 0f, ClipBlendShape.FloorTimesCoverage,
                                                  ClipFramingSource.GatedTicks, ClipMissRule.MostTime);
                for (int i = 0; i < _candidates.Length; i++)
                {
                    if (_candidates[i].TouchedTick == _ticks + 1) continue;
                    ClipGrade g = _candidates[i].CountedTicks > 0
                        ? Score(ref _candidates[i], in neutral, StarScale.Default, _gradedSeconds + _tickSeconds, _anyGatedSeconds)
                        : ClipGrade.Missed(GradeMiss.NoSubject, 0f);
                    if (idx < 0 || GradeRanking.Beats(worst.Counted, worst.Percent01, g.Counted, g.Percent01))
                    {
                        worst = g;
                        idx = i;
                    }
                }
                if (idx < 0) return -1;
            }

            _candidates[idx] = default;
            _candidates[idx].Serial = serial;
            _candidates[idx].Order = _nextOrder++;
            _candidates[idx].SubjectId = subjectId;
            return idx;
        }

        private static double Finite(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0d : v;
    }
}
