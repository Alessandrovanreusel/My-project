using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using CameraGame.Player;

namespace CameraGame.Tests
{
    /// <summary>
    /// Story 2.1 / ADR-5. Two kinds of check, and neither encodes a tuned value:
    ///   - the fail-soft contract of <see cref="PlayerConfig"/> (an unusable value is replaced and REPORTED);
    ///   - the structural invariants between the player's config, the NavMesh agent and the drunk's agent — one
    ///     body, one agent (ADR-5), and an eye inside the body. Those hold at any world scale, which is why they
    ///     are worth asserting: they are what a future rescale would break silently.
    /// No test here can tell whether the scale is RIGHT; the Story 2.1 rigs carry that.
    /// </summary>
    public class PlayerConfigTests
    {
        private const string AssetPath = "Assets/Data/Player/PlayerConfig.asset";
        private const string DrunkPrefabPath = "Assets/Prefabs/Events/EventActor_Drunk.prefab";

        private PlayerConfig _cfg;

        [SetUp] public void SetUp() => _cfg = ScriptableObject.CreateInstance<PlayerConfig>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(_cfg);

        [Test]
        public void Defaults_HaveNoProblem()
        {
            Assert.IsFalse(_cfg.TryGetConfigProblem(out string problem), problem);
        }

        [TestCase(0f)] [TestCase(-3f)] [TestCase(float.NaN)]
        public void UnusableWalkSpeed_FallsBackAndIsReported(float bad)
        {
            _cfg.walkSpeed = bad;
            Assert.AreEqual(PlayerConfig.DefaultWalkSpeed, _cfg.SafeWalkSpeed);
            Assert.IsTrue(_cfg.TryGetConfigProblem(out string problem));
            StringAssert.Contains("walkSpeed", problem);
        }

        [Test]
        public void SprintSlowerThanWalk_IsRaisedToWalkAndReported()
        {
            _cfg.walkSpeed = 5f; _cfg.sprintSpeed = 3f;
            Assert.AreEqual(5f, _cfg.SafeSprintSpeed);
            Assert.IsTrue(_cfg.TryGetConfigProblem(out string problem));
            StringAssert.Contains("sprintSpeed", problem);
        }

        [TestCase(0f)] [TestCase(9.81f)] [TestCase(float.NaN)]
        public void GravityThatDoesNotPullDown_FallsBack(float bad)
        {
            _cfg.gravity = bad;
            Assert.Less(_cfg.SafeGravity, 0f);
            Assert.IsTrue(_cfg.TryGetConfigProblem(out _));
        }

        [Test]
        public void UpwardGroundStick_FallsBack()
        {
            _cfg.groundStickVelocity = 2f;
            Assert.LessOrEqual(_cfg.SafeGroundStickVelocity, 0f);
            Assert.IsTrue(_cfg.TryGetConfigProblem(out _));
        }

        [TestCase(0f)] [TestCase(-1f)]
        public void NonPositiveEyeOrNearClip_FallsBack(float bad)
        {
            _cfg.eyeHeight = bad;
            Assert.Greater(_cfg.SafeEyeHeight, 0f);
            _cfg.eyeHeight = PlayerConfig.DefaultEyeHeight;
            _cfg.cameraNearClip = bad;
            Assert.Greater(_cfg.SafeCameraNearClip, 0f);
            Assert.IsTrue(_cfg.TryGetConfigProblem(out _));
        }

        [Test]
        public void EyeAtOrAboveTheBody_IsABodyProblem()
        {
            Assert.IsTrue(PlayerConfig.TryGetBodyProblem(2f, 0.3f, 1.77f, 0.3f, out string p1));
            StringAssert.Contains("eyeHeight", p1);
            Assert.IsTrue(PlayerConfig.TryGetBodyProblem(1.77f, 0.3f, 1.77f, 0.3f, out _));
            Assert.IsFalse(PlayerConfig.TryGetBodyProblem(1.5f, 0.3f, 1.77f, 0.3f, out string ok), ok);
        }

        [Test]
        public void StepAsTallAsTheBody_OrNoBody_IsABodyProblem()
        {
            Assert.IsTrue(PlayerConfig.TryGetBodyProblem(1f, 2f, 1.77f, 0.3f, out _));
            Assert.IsTrue(PlayerConfig.TryGetBodyProblem(1f, 0.3f, 0f, 0.3f, out _));
        }

        [Test]
        public void AgentMatchesBody_WithinTolerance()
        {
            Assert.IsTrue(PlayerConfig.AgentMatchesBody(0.31f, 1.75f, 0.3f, 1.77f));
            Assert.IsFalse(PlayerConfig.AgentMatchesBody(0.95f, 8.86f, 0.3f, 1.77f));
        }

        // --- the shipped assets (ADR-5: one body, one agent) ---------------------------------------------

        [Test]
        public void ShippedConfig_LoadsWithNoProblem()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<PlayerConfig>(AssetPath);
            Assert.IsNotNull(cfg, $"{AssetPath} is missing — the game would run on defaults with a warning.");
            Assert.IsFalse(cfg.TryGetConfigProblem(out string problem), problem);
        }

        [Test]
        public void NavMeshAgentType0_IsTheBodyThePlayerConfigRidesIn()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<PlayerConfig>(AssetPath);
            NavMeshBuildSettings agent = NavMesh.GetSettingsByID(0);
            Assert.IsFalse(PlayerConfig.TryGetBodyProblem(cfg.SafeEyeHeight, cfg.SafeStepOffset, agent.agentHeight,
                                                          agent.agentRadius, out string problem), problem);
            Assert.AreEqual(cfg.SafeStepOffset, agent.agentClimb, 0.01f,
                            "The NavMesh agent's climb should be the player's step offset (ADR-5).");
        }

        [Test]
        public void DrunkPrefabAgent_MatchesAgentType0()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DrunkPrefabPath);
            var agent = prefab.GetComponent<NavMeshAgent>();
            NavMeshBuildSettings type0 = NavMesh.GetSettingsByID(agent.agentTypeID);
            Assert.IsTrue(PlayerConfig.AgentMatchesBody(agent.radius, agent.height, type0.agentRadius, type0.agentHeight),
                          $"Drunk agent r {agent.radius} h {agent.height} vs bake r {type0.agentRadius} h {type0.agentHeight}");
        }
    }
}
