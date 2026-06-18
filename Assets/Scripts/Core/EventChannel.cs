using System;
using UnityEngine;

namespace CameraGame.Core
{
    /// <summary>Base ScriptableObject event channel with no payload (e.g. a future EventPeaked-style ping).</summary>
    public abstract class EventChannel : ScriptableObject
    {
        public event Action Raised;

        // Defensive cleanup: drop subscribers when the asset unloads (domain reload / play-mode exit)
        // so stale delegates from a previous play session can't survive into the next one. Consumers
        // must still unsubscribe in their own OnDisable (architecture §Communication Patterns).
        private void OnDisable() => Raised = null;

        public void Raise()
        {
            // Snapshot + per-handler isolation: a handler that throws — or that subscribes/unsubscribes
            // mid-dispatch — can't abort the rest of the listeners.
            var handlers = Raised;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action)d).Invoke(); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }
    }

    /// <summary>Base ScriptableObject event channel carrying a typed payload.
    /// Concrete channels subclass this, e.g. <c>ShotCapturedChannel : EventChannel&lt;ShotGrade&gt;</c> (Story 1.5/1.11).</summary>
    public abstract class EventChannel<T> : ScriptableObject
    {
        public event Action<T> Raised;

        private void OnDisable() => Raised = null;

        public void Raise(T payload)
        {
            var handlers = Raised;
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action<T>)d).Invoke(payload); }
                catch (Exception e) { Debug.LogException(e, this); }
            }
        }
    }
}
