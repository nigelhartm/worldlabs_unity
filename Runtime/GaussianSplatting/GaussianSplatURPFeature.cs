// SPDX-License-Identifier: MIT
#if GS_ENABLE_URP

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GaussianSplatting.Runtime
{
    // ReSharper disable once InconsistentNaming
    class GaussianSplatURPFeature : ScriptableRendererFeature
    {
        class GSRenderPass : ScriptableRenderPass
        {
            RTHandle m_RenderTarget;
            internal ScriptableRenderer m_Renderer = null;
            internal CommandBuffer m_Cmb = null;

            public void Dispose()
            {
                m_RenderTarget?.Release();
                m_Cmb?.Dispose();
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor rtDesc = renderingData.cameraData.cameraTargetDescriptor;
                rtDesc.depthBufferBits = 0;
                rtDesc.msaaSamples = 1;
                rtDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                rtDesc.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
                rtDesc.volumeDepth = 1;
                rtDesc.vrUsage = VRTextureUsage.None;
                RenderingUtils.ReAllocateIfNeeded(ref m_RenderTarget, rtDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GaussianSplatRT");
                cmd.SetGlobalTexture(m_RenderTarget.name, m_RenderTarget.nameID);

                ConfigureTarget(m_RenderTarget, m_Renderer.cameraDepthTargetHandle);
                ConfigureClear(ClearFlag.Color, new Color(0, 0, 0, 0));
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (m_Cmb == null) return;

                var system = GaussianSplatRenderSystem.instance;
                var cam = renderingData.cameraData.camera;

                if (!system.GatherSplatsForCamera(cam)) return;

                m_Cmb.Clear();
                Material matComposite = system.SortAndRenderSplats(cam, m_Cmb);
                if (matComposite == null) return;

                m_Cmb.BeginSample(GaussianSplatRenderSystem.s_ProfCompose);
                Blitter.BlitCameraTexture(m_Cmb, m_RenderTarget, m_Renderer.cameraColorTargetHandle, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, matComposite, 0);
                m_Cmb.EndSample(GaussianSplatRenderSystem.s_ProfCompose);
                context.ExecuteCommandBuffer(m_Cmb);
            }
        }

        readonly Dictionary<int, GSRenderPass> m_PassPerEye = new();

        GSRenderPass GetOrCreatePass(int eyeId)
        {
            if (!m_PassPerEye.TryGetValue(eyeId, out var pass))
            {
                pass = new GSRenderPass
                {
                    renderPassEvent = RenderPassEvent.BeforeRenderingTransparents,
                    m_Cmb = new CommandBuffer { name = $"RenderGaussianSplats_{eyeId}" }
                };
                m_PassPerEye[eyeId] = pass;
            }
            return pass;
        }

        // Use multipassId (0=left, 1=right) when XR is active, camera ID otherwise
        static int GetEyeId(in CameraData cameraData)
        {
            if (cameraData.xr.enabled)
                return cameraData.xr.multipassId;
            return cameraData.camera.GetInstanceID();
        }

        public override void Create() { }

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            if (cameraData.camera.cameraType == CameraType.Preview) return;
            GetOrCreatePass(GetEyeId(cameraData));
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.camera.cameraType == CameraType.Preview) return;

            int eyeId = GetEyeId(renderingData.cameraData);
            var pass = GetOrCreatePass(eyeId);
            pass.m_Renderer = renderer;
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var pass in m_PassPerEye.Values)
                pass?.Dispose();
            m_PassPerEye.Clear();
        }
    }
}

#endif // #if GS_ENABLE_URP
