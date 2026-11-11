using NUnit.Framework;
using CameraGame.Events;

namespace CameraGame.Tests.EditMode
{
    /// <summary>
    /// Story 2.2 (cross-review finding 1): when the agent reaches the end of its path, the actor decides from the WAYPOINT,
    /// not from remainingDistance alone. A partial path (the whole-town mesh's search budget) ends with remainingDistance ≈ 0
    /// hundreds of metres short; counting that as "arrived" skipped the waypoint. These pin the decision table.
    /// </summary>
    public class EventActorRouteTests
    {
        private const float Stop = 0.2571f;   // the drunk's stopping distance (Story 2.1, lesson 68)

        [Test]
        public void AtTheWaypoint_IsArrived()
        {
            Assert.AreEqual(EventActor.RouteStep.Arrived, EventActor.NextRouteStep(Stop, float.PositiveInfinity, Stop));
            Assert.AreEqual(EventActor.RouteStep.Arrived, EventActor.NextRouteStep(Stop + EventActor.ArrivalSlack, float.PositiveInfinity, Stop));
        }

        [Test]
        public void PartialPathEnd_FarShort_FirstTime_Replans()
        {
            // measured: the drunk's first path toward the farm ended 162 m short, remainingDistance 0.256
            Assert.AreEqual(EventActor.RouteStep.Replan, EventActor.NextRouteStep(162f, float.PositiveInfinity, Stop));
        }

        [Test]
        public void ReplanThatEndsCloser_ReplansAgain()
        {
            Assert.AreEqual(EventActor.RouteStep.Replan, EventActor.NextRouteStep(70.8f, 418.6f, Stop));
        }

        [Test]
        public void ReplanThatEndsNoCloser_GivesUp()
        {
            // a dead end: the re-plan from the dead end ends where the agent already stands
            Assert.AreEqual(EventActor.RouteStep.GiveUp, EventActor.NextRouteStep(70.8f, 70.8f, Stop));
            Assert.AreEqual(EventActor.RouteStep.GiveUp, EventActor.NextRouteStep(70.8f, 70.8f + EventActor.ReplanProgress, Stop));
            Assert.AreEqual(EventActor.RouteStep.GiveUp, EventActor.NextRouteStep(75f, 70.8f, Stop));
        }

        [Test]
        public void NonFiniteShortfall_GivesUp_NeverReplansForever()
        {
            Assert.AreEqual(EventActor.RouteStep.GiveUp, EventActor.NextRouteStep(float.NaN, float.PositiveInfinity, Stop));
            Assert.AreEqual(EventActor.RouteStep.GiveUp, EventActor.NextRouteStep(float.PositiveInfinity, float.PositiveInfinity, Stop));
        }

        [Test]
        public void BadStoppingDistance_ReadsAsZero()
        {
            Assert.AreEqual(EventActor.RouteStep.Arrived, EventActor.NextRouteStep(0.4f, float.PositiveInfinity, float.NaN));
            Assert.AreEqual(EventActor.RouteStep.Arrived, EventActor.NextRouteStep(0.4f, float.PositiveInfinity, -3f));
            Assert.AreEqual(EventActor.RouteStep.Replan, EventActor.NextRouteStep(0.6f, float.PositiveInfinity, -3f));
        }
    }
}
