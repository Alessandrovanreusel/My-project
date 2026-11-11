using NUnit.Framework;
using CameraGame.World;

namespace CameraGame.Tests
{
    /// <summary>Story 2.2, 1b: the one pure function that decides which pieces of the town map collide.</summary>
    public class ColliderRuleTests
    {
        private const float Step = 0.35f, Buried = -1f;

        [TestCase("Plane|Cube.012_M18_0|Dupli|1710", "Cube")]
        [TestCase("Plane.009|Cube.012_M18_0|Dupli|1401.1", "Cube")]
        [TestCase("Plane|Circle.005_M18.001_0.001|Dupli|2093.3", "Circle")]
        [TestCase("Plane|_11_tree__11_tree_0|Dupli|0.6", "_11_tree__11_tree_0")]
        [TestCase("Circle.002_M17_0", null)]
        [TestCase("SuperMarket_Colore_0", null)]
        [TestCase("", null)]
        [TestCase(null, null)]
        public void ScatterFamily_ReadsTheParticleFamily(string name, string expected) =>
            Assert.AreEqual(expected, ColliderRule.ScatterFamily(name));

        [TestCase("Plane|Cube.012_M18_0|Dupli|1710", "scatter:Cube")]
        [TestCase("Plane.004_M3_0", "Plane")]
        [TestCase("House Complete.001_House Complete_0", "House Complete")]
        [TestCase("Landscape", "Landscape")]
        public void ClassOf_GroupsByBaseNameOrFamily(string name, string expected) =>
            Assert.AreEqual(expected, ColliderRule.ClassOf(name));

        [Test]
        public void Grass_NeverCollides_WhateverItsHeight()
        {
            Assert.AreEqual(ColliderVerdict.Grass, ColliderRule.Decide("Plane|Cube.012_M18_0|Dupli|1", "material_13", 1.5f, 1.5f, Step, Buried));
            Assert.AreEqual(ColliderVerdict.Grass, ColliderRule.Decide("Plane|Cube.012_M18_0|Dupli|2", "material_13", 30f, 23.8f, Step, Buried));
        }

        [Test]
        public void Pines_GetATrunkCollider() =>
            Assert.AreEqual(ColliderVerdict.Trunk, ColliderRule.Decide("Plane|Circle.005_M18.001_0.001|Dupli|3", "M18.001", 8f, 8f, Step, Buried));

        [Test]
        public void ScatterAtOrUnderTheStep_DoesNotCollide()
        {
            Assert.AreEqual(ColliderVerdict.LowScatter, ColliderRule.Decide("Plane|Icosphere.002_M3_0|Dupli|5", "material_1", 0.3f, 0.35f, Step, Buried));
            Assert.AreEqual(ColliderVerdict.LowScatter, ColliderRule.Decide("Plane|Icosphere.002_M3_0|Dupli|6", "material_1", 0.1f, -0.2f, Step, Buried));
        }

        [Test]
        public void ScatterTallerThanTheStep_Keeps()
        {
            Assert.AreEqual(ColliderVerdict.Keep, ColliderRule.Decide("Plane|Icosphere.002_M3_0|Dupli|5", "material_1", 1.4f, 1.4f, Step, Buried));
            Assert.AreEqual(ColliderVerdict.Keep, ColliderRule.Decide("Plane|Cylinder.004_M18.001_0|Dupli|1", "M18.001", 1.9f, 0.82f, Step, Buried));
        }

        [Test]
        public void UnmeasuredHeight_KeepsTheCollider() =>
            Assert.AreEqual(ColliderVerdict.Keep, ColliderRule.Decide("Plane|Icosphere.002_M3_0|Dupli|5", "material_1", 1.4f, float.NaN, Step, Buried));

        [Test]
        public void Water_NeverCollides() =>
            Assert.AreEqual(ColliderVerdict.Water, ColliderRule.Decide("Plane001", "Water.001", 9f, float.NaN, Step, Buried));

        [Test]
        public void BuriedGeometry_DoesNotCollide()
        {
            Assert.AreEqual(ColliderVerdict.Buried, ColliderRule.Decide("Icosphere_Colore_0", "Colore", -6.2f, float.NaN, Step, Buried));
            Assert.AreEqual(ColliderVerdict.Buried, ColliderRule.Decide("Casa.001_Colore_0", "Colore", -58f, float.NaN, Step, Buried));
            // even grass and pines that sank below the plain
            Assert.AreEqual(ColliderVerdict.Buried, ColliderRule.Decide("Plane.009|Circle.005_M18.001_0.001|Dupli|121", "M18.001", -1.4f, float.NaN, Step, Buried));
        }

        [Test]
        public void TheSurfacePieces_Keep()
        {
            Assert.AreEqual(ColliderVerdict.Keep, ColliderRule.Decide("Landscape.005", "Material.005", 201f, float.NaN, Step, Buried));
            Assert.AreEqual(ColliderVerdict.Keep, ColliderRule.Decide("Plane.010", "Material.007", 0f, float.NaN, Step, Buried));   // the pond: barriered, never removed
            Assert.AreEqual(ColliderVerdict.Keep, ColliderRule.Decide("Circle.002_M17_0", "M17", 9f, float.NaN, Step, Buried));    // the solo pine is a prop, not scatter
            Assert.AreEqual(ColliderVerdict.Keep, ColliderRule.Decide("Plane.004_M3_0", "material_1", 2f, float.NaN, Step, Buried));
        }

        [Test]
        public void NeedsHeightAboveGround_OnlyForNonGrassNonPineScatter()
        {
            Assert.IsTrue(ColliderRule.NeedsHeightAboveGround("Plane|Icosphere.002_M3_0|Dupli|5"));
            Assert.IsFalse(ColliderRule.NeedsHeightAboveGround("Plane|Cube.012_M18_0|Dupli|5"));
            Assert.IsFalse(ColliderRule.NeedsHeightAboveGround("Plane|Circle.005_M18.001_0.001|Dupli|5"));
            Assert.IsFalse(ColliderRule.NeedsHeightAboveGround("SuperMarket_Colore_0"));
        }
    }
}
