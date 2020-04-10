using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;
public class MyPipeline : RenderPipeline
{
    CullResults cull;

    Material errorMaterial;

    DrawRendererFlags drawflags;

    RenderTexture shadowMap;

    CommandBuffer buffer = new CommandBuffer
    {
        name = "Render Camera"
    };

    CommandBuffer shadowBuffer = new CommandBuffer
    {
        name = "Render Shadows"
    };
    
    const int maxVisibleLights = 16;

    static int visibleLightColorsId                 = Shader.PropertyToID("_VisibleLightColors");
    static int visibleLightDirectionsOrPositionsId  = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    static int visibleLightAttenuationsId           = Shader.PropertyToID("_VisibleLightAttenuations");
    static int visibleLightSpotDirectionsId         = Shader.PropertyToID("_VisibleLightSpotDirections");
    static int lightIndicesOffsetAndCountID         = Shader.PropertyToID("unity_LightIndicesOffsetAndCount");

    static int shadowMapId              = Shader.PropertyToID("_ShadowMap");
    static int worldToShadowMatrixId    = Shader.PropertyToID("_WorldToShadowMatrix");

    Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
    Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
    Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];

    public MyPipeline (bool dynamicBatching, bool instancing)
    {
        GraphicsSettings.lightsUseLinearIntensity = true;
        if (dynamicBatching)
        {
            drawflags = DrawRendererFlags.EnableDynamicBatching;
        }
        if (instancing)
        {
            drawflags |= DrawRendererFlags.EnableInstancing;
        }
    }
    public override void Render (ScriptableRenderContext renderContext, Camera[] cameras)
    {
        base.Render(renderContext, cameras);
        //renderContext.DrawSkybox(cameras[0]);
        //renderContext.Submit();
        foreach (var camera in cameras)
        {
            RenderSingle(renderContext, camera);
        }
    }

    void RenderSingle (ScriptableRenderContext context, Camera camera)
    {
        ScriptableCullingParameters cullingParameters;
        if(!CullResults.GetCullingParameters(camera, out cullingParameters))
        {
            return;
        }
    #if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
    #endif
        CullResults.Cull(ref cullingParameters, context, ref cull);


        
        RenderShadows(context);

        context.SetupCameraProperties(camera);
        

        //buffer.BeginSample("Render Camera");

        CameraClearFlags clearFlags = camera.clearFlags;
        buffer.ClearRenderTarget(
            (clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0,
            camera.backgroundColor
        );
        if (cull.visibleLights.Count > 0)
        {
            ConfigureLights();
        }
        else
        {
            buffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
        }
        buffer.BeginSample("Render Camera");

        buffer.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
        buffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions);
        buffer.SetGlobalVectorArray(visibleLightAttenuationsId, visibleLightAttenuations);
        buffer.SetGlobalVectorArray(visibleLightSpotDirectionsId, visibleLightSpotDirections);

        //buffer.ClearRenderTarget(true, false, Color.clear);
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
        //buffer.EndSample("Render Camera");

        var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit")) { 
            flags = drawflags//,
            //rendererConfiguration = RendererConfiguration.PerObjectLightIndices8
        };
        if(cull.visibleLights.Count > 0)
        {
            drawSettings.rendererConfiguration = RendererConfiguration.PerObjectLightIndices8;
        }
        //drawSettings.flags = drawflags;
        drawSettings.sorting.flags = SortFlags.CommonOpaque;
        
        var filterSettings = new FilterRenderersSettings(true)
        {
            renderQueueRange = RenderQueueRange.opaque
        };

        context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);

        context.DrawSkybox(camera);

        drawSettings.sorting.flags = SortFlags.CommonTransparent;
        filterSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);

        DrawDefaultPipeline(context, camera);
        buffer.EndSample("Render Camera");
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();


        context.Submit();

        if (shadowMap)
        {
            RenderTexture.ReleaseTemporary(shadowMap);
            shadowMap = null;
        }
    }

    void ConfigureLights()
    {
        for(int i =0; i<cull.visibleLights.Count; ++i)
        {
            if (i == maxVisibleLights)
                break;

            VisibleLight light = cull.visibleLights[i];
            visibleLightColors[i] = light.finalColor;
            Vector4 attenuation = Vector4.zero;
            attenuation.w = 1f;
            Vector4 v;
            if (light.lightType == LightType.Directional)
            {
                v = light.localToWorld.GetColumn(2);
                v.x = -v.x;
                v.y = -v.y;
                v.z = -v.z;
            }
            else
            {
                v = light.localToWorld.GetColumn(3);
                attenuation.x = 1.0f / Mathf.Max(light.range * light.range, 1e-3f);

                if(light.lightType == LightType.Spot)
                {
                    Vector4 v1;
                    v1 = light.localToWorld.GetColumn(2);
                    v1.x = -v1.x;
                    v1.y = -v1.y;
                    v1.z = -v1.z;
                    visibleLightSpotDirections[i] = v1;
                    float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
                    float outerCos = Mathf.Cos(outerRad);
                    float outerTan = Mathf.Tan(outerRad);

                    float innerCos = Mathf.Cos(Mathf.Atan(23f / 32f) * outerTan);
                    float angleRange = Mathf.Max(innerCos - outerCos, 1e-3f);
                    attenuation.z = 1f / angleRange;
                    attenuation.w = -outerCos * attenuation.z;
                }
            }
            visibleLightAttenuations[i] = attenuation;
            visibleLightDirectionsOrPositions[i] = v;

        }

        if (cull.visibleLights.Count > maxVisibleLights)
        {
            int[] lightIndices = cull.GetLightIndexMap();
            for (int i = maxVisibleLights; i < cull.visibleLights.Count; ++i)
            {
                lightIndices[i] = -1;
            }
            cull.SetLightIndexMap(lightIndices);
        }

        //for(int i = maxVisibleLights-1; i >= cull.visibleLights.Count; --i)
        //{
        //    visibleLightColors[i] = Color.clear;
        //}

    }

    void RenderShadows(ScriptableRenderContext context)
    {
        shadowMap = RenderTexture.GetTemporary(512, 512, 16, RenderTextureFormat.Shadowmap);

        shadowMap.filterMode = FilterMode.Bilinear;
        shadowMap.wrapMode = TextureWrapMode.Clamp;

        CoreUtils.SetRenderTarget(shadowBuffer, shadowMap, RenderBufferLoadAction.DontCare, 
            RenderBufferStoreAction.Store,
            ClearFlag.Depth);

        shadowBuffer.BeginSample("Render Shadows");
        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();

        Matrix4x4 viewMatrix, projectionMatrix;
        ShadowSplitData splitData;
        cull.ComputeSpotShadowMatricesAndCullingPrimitives(0, out viewMatrix, out projectionMatrix, out splitData);
        shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();

        var shadowSettings = new DrawShadowsSettings(cull, 0);
        context.DrawShadows(ref shadowSettings);

        if (SystemInfo.usesReversedZBuffer)
        {
            projectionMatrix.m20 = -projectionMatrix.m20;
            projectionMatrix.m21 = -projectionMatrix.m21;
            projectionMatrix.m22 = -projectionMatrix.m22;
            projectionMatrix.m23 = -projectionMatrix.m23;

        }
        var scaleOffset = Matrix4x4.TRS(Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.5f);

        Matrix4x4 worldToShadowMatrix = scaleOffset * (projectionMatrix * viewMatrix);
        shadowBuffer.SetGlobalMatrix(worldToShadowMatrixId, worldToShadowMatrix);
        shadowBuffer.SetGlobalTexture(shadowMapId, shadowMap);


        shadowBuffer.EndSample("Render Shadows");
        context.ExecuteCommandBuffer(shadowBuffer);
        shadowBuffer.Clear();
    }

    [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
    void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
    {
        if (errorMaterial == null)
        {
            Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
            errorMaterial = new Material(errorShader) { hideFlags = HideFlags.HideAndDontSave };
        }
        var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("ForwardBase"));
        drawSettings.SetShaderPassName(1, new ShaderPassName("PrepassBase"));
        drawSettings.SetShaderPassName(1, new ShaderPassName("Always"));
        drawSettings.SetShaderPassName(1, new ShaderPassName("Vertex"));
        drawSettings.SetShaderPassName(1, new ShaderPassName("VertexLMRGBM"));
        drawSettings.SetShaderPassName(1, new ShaderPassName("VertexLM"));

        drawSettings.SetOverrideMaterial(errorMaterial, 0);
        
        var filterSettings = new FilterRenderersSettings(true);

        context.DrawRenderers(cull.visibleRenderers, ref drawSettings, filterSettings);
    }
}
