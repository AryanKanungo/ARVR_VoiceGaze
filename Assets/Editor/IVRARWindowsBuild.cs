using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;

public static class IVRARWindowsBuild
{
    public static bool DesktopBuildInProgress { get; private set; }
    [MenuItem("IVRAR/Build Windows Desktop")]
    public static void Build()
    {
        EditorSceneManager.SaveOpenScenes();
        Directory.CreateDirectory("Builds/Windows");
        var xr=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        bool originalInit=xr.InitManagerOnStart;
        var originalApis=PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64);
        bool originalAuto=PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64);
        try
        {
            DesktopBuildInProgress=true;
            // Desktop player avoids hardware XR startup; the Editor/XR configuration is restored below.
            xr.InitManagerOnStart=false;EditorUtility.SetDirty(xr);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11,GraphicsDeviceType.Vulkan});
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{"Assets/Scenes/MainVRScene.unity"},
                locationPathName="Builds/Windows/IVRAR_VoiceGaze.exe",
                target=BuildTarget.StandaloneWindows64,options=BuildOptions.None
            });
            var s=report.summary;
            File.WriteAllText("Evidence/WindowsBuildResult.txt","Result="+s.result+"\nErrors="+s.totalErrors+"\nWarnings="+s.totalWarnings+"\nBytes="+s.totalSize+"\nDuration="+s.totalTime+"\nPath="+s.outputPath);
            if(s.result!=BuildResult.Succeeded)throw new System.Exception("Windows build failed: "+s.result);
            Debug.Log("IVRAR Windows build succeeded: "+s.outputPath);
        }
        finally
        {
            DesktopBuildInProgress=false;
            xr.InitManagerOnStart=originalInit;EditorUtility.SetDirty(xr);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,originalAuto);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,originalApis);
            AssetDatabase.SaveAssets();
        }
    }
}

// This prototype has no inference model/components. Skip only the unused inference package's
// shader variants in this explicit desktop build; ordinary XR/model builds are unaffected.
public sealed class IVRARUnusedInferenceShaderStripper : IPreprocessShaders, IPreprocessComputeShaders
{
    public int callbackOrder => int.MaxValue;
    static bool Unused(Object shader)
    {
        return IVRARWindowsBuild.DesktopBuildInProgress &&
            AssetDatabase.GetAssetPath(shader).StartsWith("Packages/com.unity.ai.inference/");
    }
    public void OnProcessShader(Shader shader,ShaderSnippetData snippet,IList<ShaderCompilerData> data)
    { if(Unused(shader))data.Clear(); }
    public void OnProcessComputeShader(ComputeShader shader,string kernel,IList<ShaderCompilerData> data)
    { if(Unused(shader))data.Clear(); }
}

