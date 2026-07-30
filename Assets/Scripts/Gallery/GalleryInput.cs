using UnityEngine;
using UnityEngine.InputSystem;
using CameraGame.Core;

namespace CameraGame.Gallery
{
    /// <summary>
    /// Opens and closes the gallery from player input (Story 1.11, AC2). A thin adapter and nothing else:
    /// every rule about WHEN the gallery may open lives in <see cref="GalleryView.Toggle"/>, so the rig can
    /// drive the same path without synthesising an <c>InputValue</c> — the shape that made Stories 1.9 and
    /// 1.10 provable by running them.
    ///
    /// ⚠️ THIS MUST LIVE ON THE PLAYER GAMEOBJECT — the one carrying <c>PlayerInput</c>. Under "Send
    /// Messages", PlayerInput calls <c>OnXxx</c> only on components of its OWN GameObject, never on a child
    /// and never on the camera. Put this on the gallery canvas instead and <see cref="OnGallery"/> will
    /// simply never fire, with a completely clean console and nothing to grep for.
    /// (<c>PhotoModeController.cs:16-19</c> states the same rule for the same reason.)
    /// </summary>
    public class GalleryInput : MonoBehaviour
    {
        [Tooltip("The gallery UI this key opens. If unassigned, the key does nothing and says so once at " +
                 "Awake — capture, grading and storing shots are all unaffected.")]
        [SerializeField] private GalleryView view;

        private void Awake()
        {
            // Reported once, here, because an unassigned view in a shipped scene is a wiring mistake and a
            // dead key is exactly the kind of thing nobody notices until a playtest.
            if (view == null)
                GameLog.Error("Gallery",
                    "GalleryInput has no GalleryView — the gallery key will do nothing. Shots are still " +
                    "captured, graded and stored.", this);
        }

        // Method name "OnGallery" derives from the action name GameConstants.InputActions.Gallery.
        //
        // The Gallery action is a *Button*, like Capture (PhotoModeController.cs:289-293): a discrete
        // one-shot tap, and under Send Messages a Button delivers exactly ONE call per press — one toggle.
        //
        // ⚠️ The OPPOSITE rule applies to HELD inputs. RaiseCamera and Zoom are Value actions precisely
        // because a Button never delivers the release, which is the sticky-input bug this project has fixed
        // twice (Sprint in 1.2, then the camera raise in 1.3). A Value action here would fire on press AND
        // release, toggling the gallery open and shut again within one tap. Do not copy the wrong one.
        public void OnGallery(InputValue value)
        {
            // Checked HERE rather than against a flag cached in Awake. A view that is assigned or replaced
            // after Awake — spawned UI, or the verification rig rebuilding the gallery between scenarios —
            // must still work, and a stale readiness flag would leave the key permanently dead in precisely
            // the case that is hardest to notice: everything wired, nothing happening, clean console.
            if (view == null) return;

            view.Toggle();
        }
    }
}
