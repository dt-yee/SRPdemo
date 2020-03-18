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
    static int visibleLightDirectionsId = Shader.PropertyToID("_VisibleLightDirections");

    Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    Vector4[] visibleLightDirections = new Vector4[maxVisibleLights]; 

    public MyPipeline (bool dynamicBatching, bool instancing)
    {
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
            Render(renderContext, camera);
        }
    }

    void Render (ScriptableRenderContext context, Camera camera)
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
        buffer.BeginSample("Render Camera");

        buffer.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
        buffer.SetGlobalVectorArray(visibleLightDirectionsId, visibleLightDirections);

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
