using System;
using System.Collections.Generic;
using UnityEngine;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.Grading;

namespace CameraGame.Gallery
{
    /// <summary>
    /// The player's roll of film (Story 1.11, FR8). Subscribes to <see cref="ShotCapturedChannel"/> and, for
    /// every capture, stores a <see cref="CapturedShot"/>: the picture, the grade, the subject and the UTC
    /// time. In-memory only — a plain <c>List</c>, no disk I/O (AR6); see <see cref="CapturedShot"/> for the
    /// written argument that Epic 5 can add PNG + JSON persistence without reshaping any of it.
    ///
    /// ================================================================================================
    /// WHY THE GALLERY TAKES ITS OWN PICTURE (the decision that gates this story — Task 1)
    /// ================================================================================================
    /// The channel delivers a <see cref="ShotGrade"/> and nothing else. Three routes could get the image
    /// and identity here; this is route (c), and the reasoning is worth keeping because the other two look
    /// more obvious:
    ///
    ///   (a) Widen the channel payload to carry the image. Rejected: it makes the RAISER create a
    ///       Texture2D and hand it to an unknown number of listeners, so NOBODY owns it. With Story 1.12's
    ///       HUD subscribing to the same channel, "who destroys this texture" has no answer — and a texture
    ///       nobody destroys is precisely the NFR3 leak this story exists to avoid. It also changes a
    ///       shipped seam two stories already depend on.
    ///
    ///   (b) Give the gallery a back-reference to PhotoModeController and pull the image from it. Rejected:
    ///       it reverses the decoupling AR4 exists for and that ShotCapturedChannel.cs:10-11 states outright.
    ///
    ///   (c) CHOSEN. Add SubjectId to ShotGrade (it is pure data, so the struct stays free of Unity object
    ///       references and the channel TYPE is unchanged — 1.12 is unaffected), and let the gallery render
    ///       the camera itself. This handler runs SYNCHRONOUSLY inside <c>Capture()</c>, on the shutter
    ///       frame, so the frame rendered here is the same instant that was graded. And because the gallery
    ///       creates the texture, the gallery owns it — so AC4's "destroy it" rule has exactly one home,
    ///       which is this class.
    ///
    /// ⚠️ THE PICTURE CONTAINS NO UI, AND THAT IS CORRECT. Both canvases in SampleScene (the viewfinder and
    /// the capture flash) are Screen Space – Overlay, which is composited after every camera and is never
    /// part of a camera's render. Do not "fix" this, and do not reorder the flash relative to the grade to
    /// try to get it into the photograph.
    /// </summary>
    public class GalleryService : MonoBehaviour
    {
        [Header("Wiring")]

        [Tooltip("The channel raised on every capture (Assets/Data/Channels/ShotCapturedChannel.asset). " +
                 "The gallery's only wire to the capture path — if unassigned, the gallery stores nothing " +
                 "and capture, grading, flash and shutter all keep working exactly as before.")]
        [SerializeField] private ShotCapturedChannel shotCapturedChannel;

        [Tooltip("Designer-facing gallery tunables (thumbnail size, how many shots are kept). Required: " +
                 "without it there is no authored bound on stored shots, and storing without a bound is " +
                 "the unbounded native-memory leak NFR3 forbids — so the gallery stands down instead.")]
        [SerializeField] private GalleryConfig galleryConfig;

        [Tooltip("The camera to photograph — it MUST be the same camera PhotoModeController grades through. " +
                 "Two different cameras would store a picture of a different view from the one that was " +
                 "scored: plausible-looking, completely wrong, and invisible in every log. If unassigned, " +
                 "shots are still recorded (grade, subject, time) but carry no picture.")]
        [SerializeField] private Camera photoCamera;

        // Fail-soft flags, resolved INDEPENDENTLY in Awake so one missing reference never disables the
        // others — the independence the 1.4 and 1.5 reviews both praised and PhotoModeController.cs:99-110
        // documents. _storeReady = can store at all; _cameraReady = can also take a picture.
        private bool _storeReady;
        private bool _cameraReady;

        // The roll of film, oldest first. A List rather than a Queue so it can be exposed as
        // IReadOnlyList<T> the way EventManager.ActiveActors is (EventManager.cs:57); the O(n) RemoveAt(0)
        // on eviction runs at most once per shutter press over at most a few hundred entries, which is not
        // a cost worth trading that clarity for.
        private readonly List<CapturedShot> _shots = new List<CapturedShot>();

        // Monotonic within the session; half of the shot id (see CapturedShot.MakeId for why the timestamp
        // is the other half).
        private int _shotCounter;

        // The aspect warning fires ONCE per session, lazily, at the first capture that disagrees. Not at
        // Awake: the Game View's aspect is not settled then, and a warning about a value that has not
        // stopped changing is a warning people learn to ignore.
        private bool _warnedAspect;

        /// <summary>
        /// The stored shots, OLDEST FIRST. Read-only to callers so nothing outside can desync the
        /// service's own bookkeeping — the same exposure as <c>EventManager.ActiveActors</c>.
        ///
        /// ⚠️ An entry's <c>Image</c> is owned by this service and is DESTROYED on eviction. Do not hold a
        /// <c>CapturedShot</c> across captures and expect its texture to still exist; read the list live,
        /// exactly as grading reads the actor list live.
        /// </summary>
        public IReadOnlyList<CapturedShot> Shots => _shots;

        /// <summary>How many shots the gallery will keep before it starts evicting, or 0 when it is not
        /// configured. Exposed so the view can size its cell pool from the same authority.</summary>
        public int Capacity => galleryConfig != null ? galleryConfig.SafeMaxStoredShots : 0;

        /// <summary>True when the gallery is actually recording. False is a supported state, not a bug.</summary>
        public bool IsRecording => _storeReady;

        private void Awake()
        {
            // Each reference is checked on its own line into its own flag. A missing camera must not stop
            // the gallery recording grades, and a missing config must not be reported as a missing channel.
            bool haveChannel = shotCapturedChannel != null;
            if (!haveChannel)
                GameLog.Error("Gallery",
                    "ShotCapturedChannel unassigned — the gallery will store nothing. Capture, grading, " +
                    "flash and shutter are unaffected.", this);

            bool haveConfig = galleryConfig != null;
            if (!haveConfig)
                GameLog.Error("Gallery",
                    "GalleryConfig unassigned — the gallery will store nothing. Storing without an " +
                    "authored cap would be an unbounded Texture2D leak (NFR3), which is worse than " +
                    "storing nothing at all.", this);
            else if (galleryConfig.TryGetConfigProblem(out string problem))
                // Fail-soft must not mean invisible: these are values that let the gallery run while doing
                // something other than what was authored, which is far harder to notice than an outright
                // failure.
                GameLog.Warn("Gallery", $"GalleryConfig: {problem}");

            _storeReady = haveChannel && haveConfig;

            _cameraReady = photoCamera != null;
            if (!_cameraReady)
                // Error, not Info: unlike a missing shutter clip this is not an expected authoring state.
                // A gallery of pictureless entries is a real degradation, and it should say so once.
                GameLog.Error("Gallery",
                    "No camera assigned — shots will be recorded with their grade, subject and time but " +
                    "WITHOUT a picture.", this);

            if (_storeReady)
                GameLog.Info("Gallery", $"Recording — {galleryConfig.DescribeBudget()}.");
        }

        // Subscribe/unsubscribe symmetrically (architecture §Communication Patterns). EventChannel<T> also
        // clears its subscribers on domain reload, but that is a backstop, not a substitute: a GalleryService
        // that is merely DISABLED must leave no live delegate on the channel asset, or a disabled gallery
        // goes on quietly allocating a texture per shutter press.
        private void OnEnable()
        {
            if (shotCapturedChannel != null) shotCapturedChannel.Raised += HandleShotCaptured;
        }

        private void OnDisable()
        {
            if (shotCapturedChannel != null) shotCapturedChannel.Raised -= HandleShotCaptured;
        }

        /// <summary>
        /// Runs synchronously inside <c>PhotoModeController.Capture()</c>, on the shutter frame — which is
        /// what makes the picture below the same instant that was graded.
        /// </summary>
        private void HandleShotCaptured(ShotGrade grade)
        {
            if (!_storeReady) return;

            // The picture first, so its cost lands inside the shutter frame with everything else it has to
            // be measured against (NFR2). Null is a supported outcome, not an error path.
            Texture2D image = _cameraReady ? TryRenderThumbnail() : null;

            DateTime now = DateTime.UtcNow;
            var shot = new CapturedShot(
                CapturedShot.MakeId(++_shotCounter, now), image, grade, grade.SubjectId, now);

            _shots.Add(shot);
            EvictOverflow();

            // Milestone log — capture is user-driven and infrequent, so this is not console spam (same
            // reasoning as PhotoModeController.cs:345-351). Nothing in this class logs from a per-frame path.
            GameLog.Info("Gallery", $"Stored {shot}  ({_shots.Count}/{galleryConfig.SafeMaxStoredShots}).");
        }

        /// <summary>
        /// Renders the live camera into a thumbnail, using the technique this project has already proven in
        /// <c>PhotoShootRunner.Save()</c> (PhotoShootRunner.cs:821-855).
        ///
        /// ⚠️ NOT <c>ScreenCapture.CaptureScreenshot</c>, and that is already paid for: it grabs the Game
        /// View backbuffer, which does not repaint its 3D content while the editor runs unattended — it
        /// produced ten images of a UI overlay on blank white (CLAUDE.md §Traps).
        ///
        /// Returns null rather than throwing on any failure. This runs inside the shutter, and NFR8 says
        /// nothing here may take the capture path down with it.
        /// </summary>
        private Texture2D TryRenderThumbnail()
        {
            int w = galleryConfig.SafeThumbnailWidth;
            int h = galleryConfig.SafeThumbnailHeight;

            WarnOnAspectMismatch(w, h);

            // ⚠️ SAVE AND RESTORE BOTH. Neither is safely assumed null: the photo-shoot rig binds its own
            // render target to this very camera for the whole of a run (PhotoShootRunner.cs:320-322), and a
            // gallery that clobbers it breaks the one tool this project verifies grading with.
            RenderTexture prevTarget = photoCamera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            // GetTemporary/ReleaseTemporary pools the render target, which is the cheaper choice for a
            // per-capture readback than constructing one each time — but it still has to be released, or a
            // leak per shutter press is exactly what we have built.
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = null;

            try
            {
                photoCamera.targetTexture = rt;
                photoCamera.Render();

                RenderTexture.active = rt;

                // RGB24, no mip chain: a photograph has no transparency, and a mip chain would cost a third
                // more memory for a thumbnail that is never minified. Created by ReadPixels, so it is
                // readable and uncompressed by construction — which is what makes EncodeToPNG() work on it
                // unchanged in Epic 5 (AC3).
                tex = new Texture2D(w, h, TextureFormat.RGB24, mipChain: false);
                tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            catch (Exception e)
            {
                // Destroy the half-built texture before letting go of it — the one place in this class where
                // an early exit could strand native memory.
                if (tex != null) Destroy(tex);

                GameLog.Error("Gallery",
                    $"Failed to photograph the camera for the gallery — this shot is stored without a " +
                    $"picture. {e.GetType().Name}: {e.Message}", this);
                return null;
            }
            finally
            {
                photoCamera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Says out loud when the stored picture will not frame what the player saw.
        ///
        /// Binding a target texture makes Unity recompute <c>Camera.aspect</c> from it, so a 16:9 thumbnail
        /// taken from a camera rendering at 21:9 shows MORE of the scene vertically and less horizontally
        /// than the graded frame did. Nothing crashes and the picture looks perfectly plausible — the
        /// subject is simply not where the grade says he was. Exactly the class of silent disagreement this
        /// project keeps getting caught by, so it warns once and names both numbers.
        /// </summary>
        private void WarnOnAspectMismatch(int w, int h)
        {
            if (_warnedAspect) return;

            // Read BEFORE the target texture is rebound, or we would be comparing the thumbnail with itself.
            float live = photoCamera.aspect;
            float thumb = w / (float)h;

            // A percent of slack: a 1920x1080 Game View and a 480x270 thumbnail are the same ratio to
            // floating-point noise, and a warning that fires on rounding is a warning nobody reads.
            if (Mathf.Abs(live - thumb) <= 0.01f * thumb) return;

            _warnedAspect = true;
            GameLog.Warn("Gallery",
                $"The camera is rendering at aspect {live:0.###} but the gallery thumbnail is " +
                $"{w}x{h} ({thumb:0.###}) — stored pictures will not frame exactly what was graded. " +
                "Set thumbnailWidth/Height to the game's aspect ratio in GalleryConfig.");
        }

        /// <summary>
        /// Drops the oldest shots until the gallery is back inside its cap, DESTROYING each picture as it
        /// goes.
        ///
        /// ⚠️ <c>Destroy</c> is not optional and dropping the reference is not enough. A Texture2D holds
        /// NATIVE memory that the C# garbage collector never touches, so an evicted-but-undestroyed
        /// thumbnail is a permanent 380 KB — invisible, with a clean console and a working game, growing
        /// once per shutter press for as long as the session lasts. This is the first story in the project
        /// that can do that.
        /// </summary>
        private void EvictOverflow()
        {
            int cap = galleryConfig.SafeMaxStoredShots;

            // `while`, not `if`: a cap lowered at runtime (the verification rig does exactly this) leaves
            // the list several over, and a single-step eviction would go on holding every one of them.
            while (_shots.Count > cap)
            {
                CapturedShot oldest = _shots[0];
                _shots.RemoveAt(0);
                if (oldest.Image != null) Destroy(oldest.Image);
            }
        }

        /// <summary>Empties the gallery, destroying every picture. Used when the service goes away and by
        /// the verification rig between scenarios; there is no player-facing "delete all" in this story.</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _shots.Count; i++)
            {
                if (_shots[i].Image != null) Destroy(_shots[i].Image);
            }

            _shots.Clear();
        }

        // A scene reload must not strand native memory: the managed List dies with the scene, but every
        // Texture2D it pointed at would outlive it. OnDestroy is the last moment this object can free them.
        private void OnDestroy() => ClearAll();
    }
}
