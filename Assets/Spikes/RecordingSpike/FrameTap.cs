// THROWAWAY SPIKE CODE (Story 1.14). Never referenced by game code. See README.md in this folder.
using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace CameraGame.Spikes.RecordingSpike
{
    /// <summary>
    /// Task 1a's frame source: the photo camera's own post-processed colour, WITHOUT the Screen Space -
    /// Overlay UI, copied on the GPU from the frame the game has already rendered. No second render, no
    /// ReadPixels. A URP render pass is enqueued from script (no renderer-asset change) only on frames
    /// the harness has marked for capture, blits the camera colour into an sRGB RT the spike owns, and
    /// an AsyncGPUReadback is issued after the frame has been submitted.
    /// </summary>
    public sealed class FrameTap : IDisposable
    {
        public readonly int Width, Height;
        public readonly RenderTexture Target;
        public Camera Camera;
        public int BackbufferSkips;     // frames on which URP gave us the backbuffer (nothing to copy)
        public int PassesRecorded;

        private readonly CopyPass _pass;
        private readonly RTHandle _handle;
        private bool _captureThisFrame;
        private bool _passRanThisFrame;
        private Action _afterSubmit;

        public FrameTap(Camera cam, int w, int h)
        {
            Camera = cam; Width = w; Height = h;
            // sRGB target: the post-processed colour is linear in shader space, and writing it to an sRGB
            // target encodes it, so the bytes we read back are display-referred (Task 1a colour-space trap).
            Target = new RenderTexture(w, h, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.None)
            { name = "RecordingSpike_FrameTap" };
            Target.Create();
            _handle = RTHandles.Alloc(Target);
            _pass = new CopyPass(this)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
                requiresIntermediateTexture = true,
            };
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endContextRendering += OnEndContext;
        }

        /// <summary>Mark the current frame for capture. <paramref name="afterSubmit"/> runs once the frame
        /// has been submitted, when it is safe to issue the readback.</summary>
        public void CaptureThisFrame(Action afterSubmit)
        {
            _captureThisFrame = true;
            _afterSubmit = afterSubmit;
        }

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_captureThisFrame || cam != Camera) return;
            var data = cam.GetUniversalAdditionalCameraData();
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        private void OnEndContext(ScriptableRenderContext ctx, System.Collections.Generic.List<Camera> cams)
        {
            if (!_captureThisFrame) return;
            _captureThisFrame = false;
            var cb = _afterSubmit; _afterSubmit = null;
            bool ran = _passRanThisFrame; _passRanThisFrame = false;
            if (ran) cb?.Invoke();
        }

        /// <summary>Issue a readback of the tap's target into a caller-owned slot.</summary>
        public AsyncGPUReadbackRequest ReadInto(ref NativeArray<byte> slot, Action<AsyncGPUReadbackRequest> done)
            => AsyncGPUReadback.RequestIntoNativeArray(ref slot, Target, 0, GraphicsFormat.R8G8B8A8_SRGB, done);

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endContextRendering -= OnEndContext;
            _handle?.Release();
            if (Target != null) { Target.Release(); UnityEngine.Object.Destroy(Target); }
        }

        private sealed class CopyPass : ScriptableRenderPass
        {
            private readonly FrameTap _tap;
            public CopyPass(FrameTap tap) { _tap = tap; profilingSampler = new ProfilingSampler("RecordingSpike.Copy"); }

            public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                if (res.isActiveTargetBackBuffer) { _tap.BackbufferSkips++; return; }
                TextureHandle dst = rg.ImportTexture(_tap._handle);
                rg.AddBlitPass(res.activeColorTexture, dst, Vector2.one, Vector2.zero, passName: "RecordingSpike.Copy");
                _tap._passRanThisFrame = true;
                _tap.PassesRecorded++;
            }
        }
    }
}
