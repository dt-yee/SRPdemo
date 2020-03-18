using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using Conditional = System.Diagnostics.ConditionalAttribute;
public class MyPipeline : RenderPipeline
{
    CullResults cull;

    Material errorMaterial;

    DrawRendererFlags drawflags;

    const int maxVisibleLights = 4;

    static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
    static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
    static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");

    Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    Vector4[] visibleLightDirections = new Vector4[maxVisibleLights];
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

        context.SetupCameraProperties(camera);
        
        CommandBuffer buffer = new CommandBuffer
        {
               name = "Render Camera"
        };
        //buffer.BeginSample("Render Camera");

        CameraClearFlags clearFlags = camera.clearFlags;
        buffer.ClearRenderTarget(
            (clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0,
            camera.backgroundColor
        );

        ConfigureLights();

        buffer.BeginSample("Render Camera");

        buffer.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
        buffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsId, visibleLightDirections);
        buffer.SetGlobalVectorArray(visibleLightAttenuationsId, visibleLightAttenuations);
        buffer.SetGlobalVectorArray(visibleLightSpotDirectionsId, visibleLightSpotDirections);

        //buffer.ClearRenderTarget(true, false, Color.clear);
        context.ExecuteCommandBuffer(buffer);
        buffer.Clear();
        //buffer.EndSample("Render Camera");

        var drawSettings = new DrawRendererSettings(camera, new ShaderPassName("SRPDefaultUnlit"));
        drawSettings.flags = drawflags;
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
            visibleLightDirections[i] = v;

        }
        for(int i = maxVisibleLights-1; i >= cull.visibleLights.Count; --i)
        {
            visibleLightColors[i] = Color.clear;
        }

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
