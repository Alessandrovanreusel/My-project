#if UNITY_EDITOR
using UnityEngine;

namespace CameraGame.Core
{
    /// <summary>
    /// The seam that lets a verification rig be filmed (rig infrastructure, not game code).
    ///
    /// ⚠️ WHY A STATIC HANDOFF AND NOT A DIRECT CALL. Unity Recorder's API lives in
    /// <c>Unity.Recorder.Editor</c>, whose asmdef is <c>includePlatforms: ["Editor"]</c>. A runtime
    /// assembly cannot reference an Editor-only one, so <c>PhotoShootRunner</c> and
    /// <c>GalleryShootRunner</c> — which must stay in the RUNTIME assembly, because an Editor-assembly
    /// MonoBehaviour cannot be resolved on a scene object entering play mode (CLAUDE.md §Traps) — cannot
    /// talk to Recorder at all. So the runners publish here, and the editor-side
    /// <c>RigVideoRecorder</c> watches. Neither runner references Recorder, and this file compiles with
    /// no knowledge of it either.
    ///
    /// ⚠️ WHAT IS PUBLISHED IS THE RIG'S OWN RENDER TARGET, deliberately. Both runners already bind a
    /// <c>RenderTexture</c> to the camera for the whole run so the graded frame and the saved image share
    /// one pixel space. Filming that same texture means the video is literally the frames that were
    /// graded — and it is non-invasive: Recorder reads the texture, it does not rebind the camera, so it
    /// cannot fight the target the rig depends on. Recording the Game View instead would hit the trap
    /// this project has already paid for: the Game View does not repaint its 3D content while the editor
    /// runs unattended.
    ///
    /// Editor-only by construction (<c>#if UNITY_EDITOR</c>), so no part of this reaches a build.
    /// </summary>
    public static class RigVideoFeed
    {
        /// <summary>The texture to film, or null when no rig is running.</summary>
        public static RenderTexture Texture { get; private set; }

        /// <summary>Where the clip should be written — the rig's own output folder, so the video lands
        /// beside the stills it explains. Never <c>Temp/</c>: Unity deletes that on shutdown, and evidence
        /// a human is asked to review has to outlive the session that produced it.</summary>
        public static string OutputDir { get; private set; }

        /// <summary>Base file name for the clip, without extension.</summary>
        public static string ClipName { get; private set; }

        /// <summary>Bumped on every <see cref="Publish"/>. Lets the watcher tell "a new run started" from
        /// "the same run is still going" without comparing texture references, which would be ambiguous if
        /// two consecutive runs happened to reuse the same native allocation.</summary>
        public static int Generation { get; private set; }

        /// <summary>True while a rig is offering something to film.</summary>
        public static bool IsLive => Texture != null;

        /// <summary>
        /// Offer a texture for filming. Call once the render target is bound and BEFORE the first thing
        /// worth seeing happens.
        ///
        /// Fail-soft: a null texture or a blank output directory is ignored rather than throwing. A rig
        /// that cannot be filmed must still run — the stills are the primary evidence and the video is a
        /// supplement, so a recording problem may never take a shoot down with it.
        /// </summary>
        public static void Publish(RenderTexture texture, string outputDir, string clipName)
        {
            if (texture == null || string.IsNullOrWhiteSpace(outputDir)) return;

            Texture = texture;
            OutputDir = outputDir;
            ClipName = string.IsNullOrWhiteSpace(clipName) ? "run" : clipName;
            Generation++;
        }

        /// <summary>
        /// Withdraw the offer. Call from the rig's Restore(), i.e. the same place the RenderTexture is
        /// released — filming a texture after it has been released is a read of freed native memory.
        /// </summary>
        public static void Clear()
        {
            Texture = null;
            OutputDir = null;
            ClipName = null;
        }
    }
}
#endif
