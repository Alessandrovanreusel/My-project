namespace CameraGame.Core
{
    /// <summary>True invariants (no magic strings). Tunable numbers belong in ScriptableObjects, NOT here.</summary>
    public static class GameConstants
    {
        // New Input System "Player" action map — action names (Send-Messages handler names derive from these).
        public static class InputActions
        {
            public const string Move        = "Move";
            public const string Look        = "Look";
            public const string Sprint      = "Sprint";
            public const string Jump        = "Jump";
            public const string Capture     = "Capture";      // repurposed from "Attack" in Story 1.5
            public const string RaiseCamera = "RaiseCamera";  // added in Story 1.3
            public const string Zoom        = "Zoom";         // added in Story 1.4
        }

        // Layers / tags — fill in as systems need them (kept here to kill magic strings).
        public static class Tags { /* e.g. public const string Subject = "Subject"; */ }
        public static class Layers { /* e.g. public const string Occluder = "Occluder"; */ }
    }
}
