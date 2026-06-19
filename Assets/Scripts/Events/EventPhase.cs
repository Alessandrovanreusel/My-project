namespace CameraGame.Events
{
    /// <summary>
    /// The five stages every data-driven event-actor runs through, in order. The actor advances
    /// Spawn → Build → Peak → WindDown → Despawn driven only by per-phase timers (Story 1.6).
    /// </summary>
    public enum EventPhase
    {
        Spawn,
        Build,
        Peak,
        WindDown,
        Despawn
    }
}
