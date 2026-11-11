namespace CameraGame.World
{
    /// <summary>What <see cref="ColliderRule.Decide"/> says about one piece of the town map.</summary>
    public enum ColliderVerdict
    {
        /// <summary>A person walks around it, or stands on it: it keeps its collider.</summary>
        Keep,
        /// <summary>Grass: a person walks through it, whatever its height.</summary>
        Grass,
        /// <summary>Scatter that sticks up no more than a step: a person walks over it, so a collider only snags.</summary>
        LowScatter,
        /// <summary>Water: never stood on — the shoreline barrier stops the player before it.</summary>
        Water,
        /// <summary>Buried under the plain: only a player who fell through the world could reach it.</summary>
        Buried,
    }

    /// <summary>
    /// Which pieces of the town map get a collider (Story 2.2, 1b). ONE pure function, used by the tool that adds the
    /// colliders (<c>Tools/Add MeshColliders to World</c>) and by the tool that removes the ones this rule rejects, so a
    /// re-run never re-adds what was removed. EditMode-tested.
    ///
    /// Measured on the town (census, Story 2.2): the .blend scatters three particle families over the plain — "Circle"
    /// (6,000 pine trees, 3–10 m), "Cube" (8,000 grass tufts, ~1.5 m) and "Icosphere" (1,000 white rocks; 886 stand
    /// 1.25–1.5 m proud of the ground, the rest are pebbles at or under the step) — plus white and bare trees and their
    /// stumps. Before this rule every one of them carried a MeshCollider, so a grass tuft stopped the player like a wall.
    ///
    /// The rule: no collider for grass (whatever its height), for any other scatter no taller than a step above the
    /// ground under it, for water, or for anything buried below the plain. Everything else — pines, trees, stumps,
    /// rocks, props, buildings, terrain — keeps its collider.
    /// </summary>
    public static class ColliderRule
    {
        /// <summary>The .blend particle family that is grass (its instances are named "…|Cube.012_M18_0|Dupli|n").</summary>
        public const string GrassFamily = "Cube";

        /// <summary>The material-name prefix of the map's water (Water.001).</summary>
        public const string WaterMaterialPrefix = "Water";

        /// <summary>A map piece's class: "scatter:&lt;family&gt;" for a particle instance, else its base name (the text
        /// before the first '.').</summary>
        public static string ClassOf(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return string.Empty;
            string family = ScatterFamily(objectName);
            if (family != null) return "scatter:" + family;
            int i = objectName.IndexOf('.');
            return i > 0 ? objectName.Substring(0, i) : objectName;
        }

        /// <summary>The particle family of a scatter instance ("Plane|Cube.012_M18_0|Dupli|7" → "Cube"), or null when the
        /// piece is not scatter.</summary>
        public static string ScatterFamily(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            string[] parts = objectName.Split('|');
            if (parts.Length < 2 || parts[1].Length == 0) return null;
            int dot = parts[1].IndexOf('.');
            return dot > 0 ? parts[1].Substring(0, dot) : parts[1];
        }

        /// <summary>True when <see cref="Decide"/> needs the piece's height above the ground (scatter that is not grass).
        /// Everything else is decided from its name, material and top alone.</summary>
        public static bool NeedsHeightAboveGround(string objectName)
        {
            string family = ScatterFamily(objectName);
            return family != null && family != GrassFamily;
        }

        /// <summary>
        /// The rule. <paramref name="topY"/> is the top of the piece's world bounds; <paramref name="heightAboveGround"/>
        /// is that top minus the ground directly under its centre (only read for non-grass scatter);
        /// <paramref name="stepHeight"/> is the player's step (PlayerConfig.stepOffset); <paramref name="buriedTopY"/> is the
        /// height below which a piece is buried (TownBoundsConfig.buriedTopY). A NaN height counts as "unknown" and keeps
        /// the collider — never remove what was not measured.
        /// </summary>
        public static ColliderVerdict Decide(string objectName, string materialName, float topY, float heightAboveGround,
                                             float stepHeight, float buriedTopY)
        {
            if (!string.IsNullOrEmpty(materialName) && materialName.StartsWith(WaterMaterialPrefix)) return ColliderVerdict.Water;
            if (!float.IsNaN(topY) && !float.IsNaN(buriedTopY) && topY < buriedTopY) return ColliderVerdict.Buried;
            string family = ScatterFamily(objectName);
            if (family == null) return ColliderVerdict.Keep;
            if (family == GrassFamily) return ColliderVerdict.Grass;
            if (!float.IsNaN(heightAboveGround) && !float.IsNaN(stepHeight) && heightAboveGround <= stepHeight) return ColliderVerdict.LowScatter;
            return ColliderVerdict.Keep;
        }
    }
}
