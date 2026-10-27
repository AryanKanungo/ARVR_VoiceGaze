using System;
using System.IO;
using System.Linq;
using System.Xml;
using Meta.WitAi.Configuration;
using Meta.WitAi.Data.Configuration;
using Oculus.Voice;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

public static class QuestProjectSetup
{
    public const string DesktopScene = "Assets/Scenes/MainVRScene.unity";
    public const string QuestScene = "Assets/Scenes/MainQuestScene.unity";
    public const string WitPath = "Assets/wit_voice_verse.asset";
    [MenuItem("VOICEVERSE/Quest/Create or Validate Quest Scene")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Exit Play Mode and save your own scene changes before Quest setup.");
        if (!File.Exists(QuestScene) && !AssetDatabase.CopyAsset(DesktopScene, QuestScene))
            throw new IOException("Could not copy the Windows scene.");
        var scene = EditorSceneManager.OpenScene(QuestScene);
        var controller = UnityEngine.Object.FindAnyObjectByType<SpatialVoiceIntentController>();
        if (!controller) throw new InvalidOperationException("Atelier controller is missing.");
        var configuration = AssetDatabase.LoadAssetAtPath<WitConfiguration>(WitPath);
        if (!configuration) throw new InvalidOperationException("Existing wit_voice_verse configuration was not found; no duplicate will be created.");
        // Explicit transcription bridge: no generated Conduit manifest or attribute dispatch is used.
        configuration.useConduit=false; configuration.useIntentAttributes=false;
        EditorUtility.SetDirty(configuration);
        var oldCamera = controller.resolver.gazeCamera;
        var origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
        if (!origin)
        {
            var root = new GameObject("Quest XR Origin"); root.transform.position = new Vector3(0,0,-1.4f);
            origin = root.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Floor Offset"); offset.transform.SetParent(root.transform,false);
            var go = new GameObject("Quest Head Camera"); go.transform.SetParent(offset.transform,false);
            go.transform.localPosition = Vector3.up * 1.65f; go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>(); camera.nearClipPlane = .05f; camera.farClipPlane = 50;
            camera.clearFlags = oldCamera.clearFlags; camera.backgroundColor = oldCamera.backgroundColor;
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.allowXRRendering = true; data.renderPostProcessing = false; data.renderShadows = false;
            origin.Camera = camera; origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor; origin.CameraYOffset = 1.65f;
            foreach (Transform child in oldCamera.transform.Cast<Transform>().ToArray())
                if (child.GetComponent<Canvas>()) child.SetParent(camera.transform,false);
            oldCamera.transform.root.gameObject.SetActive(false);
        }
        controller.resolver.gazeCamera = origin.Camera;
        var rig = origin.GetComponent<QuestRigSupport>() ?? origin.gameObject.AddComponent<QuestRigSupport>();
        rig.rigOrigin = origin.transform; rig.headCamera = origin.Camera;
        rig.controller = controller; rig.handbook = controller.handbook;
        controller.studio.studioCamera = origin.Camera; controller.studio.questRig = rig;
        var windows = controller.GetComponent<VoiceCommandRecognizer>(); windows.enabled = false;
        var provider = controller.GetComponent<QuestVoiceCommandProvider>() ?? controller.gameObject.AddComponent<QuestVoiceCommandProvider>();
        provider.controller = controller; provider.configuration = configuration;
        if (!provider.voiceService)
        {
            var service = new GameObject("Quest App Voice Experience"); service.SetActive(false);
            service.transform.SetParent(controller.transform,false);
            provider.voiceService = service.AddComponent<AppVoiceExperience>();
        }
        provider.voiceService.RuntimeConfiguration = new WitRuntimeConfiguration { witConfiguration = configuration,
            transcribeOnly = true, sendAudioToWit = true, alwaysRecord = false, maxRecordingTime = 8,
            minKeepAliveTimeInSeconds = 1, maxConcurrentRequests = 1, overrideTimeoutMs = 15000 };
        provider.voiceService.UsePlatformIntegrations = false;
        controller.GetComponent<InteractionTelemetry>().speechProvider = provider;
        var book = controller.handbook;
        book.platformFooter = "QUEST: Menu closes guide | X next / Y previous\nRT voice: speak handbook commands; no intent training required";
        book.panel.transform.localPosition = new Vector3(0,-.05f,1.5f);
        book.panel.transform.localScale = Vector3.one * .0012f;
        book.body.fontSize = 23; book.panel.SetActive(false);
        var reticle = origin.Camera.GetComponentsInChildren<Canvas>(true).FirstOrDefault(x=>x.name.Contains("Reticle"));
        if (reticle) { reticle.transform.localPosition=new Vector3(0,0,1.5f); reticle.transform.localScale=Vector3.one*.0006f; }
        foreach(var canvas in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Canvas>(true))) canvas.worldCamera=origin.Camera;
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        ConfigureAndroid();
        AssetDatabase.SaveAssets();
        Debug.Log("Quest scene saved; existing Wit configuration reused; final transcription maps the existing command vocabulary. No headset/microphone test performed.");
    }
    public static void ConfigureAndroid()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget settings))
            throw new InvalidOperationException("Existing XR settings could not be found.");
        var desktop = settings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (desktop) { desktop.InitManagerOnStart=false; EditorUtility.SetDirty(desktop); }
        if(!settings.HasSettingsForBuildTarget(BuildTargetGroup.Android)) settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if(!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        var android=settings.SettingsForBuildTarget(BuildTargetGroup.Android);
        android.InitManagerOnStart=true; android.Manager.automaticLoading=true; android.Manager.automaticRunning=true;
        if(!XRPackageMetadataStore.AssignLoader(android.Manager,"UnityEngine.XR.OpenXR.OpenXRLoader",BuildTargetGroup.Android))
            throw new InvalidOperationException("Android OpenXR loader assignment failed.");
        EditorUtility.SetDirty(android); EditorUtility.SetDirty(android.Manager); EditorUtility.SetDirty(settings);
        var openxr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        openxr.renderMode=OpenXRSettings.RenderMode.SinglePassInstanced;
        foreach(var feature in openxr.GetFeatures<UnityEngine.XR.OpenXR.Features.OpenXRFeature>())
            if(feature && (feature.GetType().Name=="MetaQuestFeature" || feature.GetType().Name=="MetaQuestTouchPlusControllerProfile" || feature.GetType().Name=="OculusTouchControllerProfile"))
            { feature.enabled=true; EditorUtility.SetDirty(feature); }
        EditorUtility.SetDirty(openxr);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.forceInternetPermission=true;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.ivrargroup05.voiceverse");
    }
    public static string ValidateScene()
    {
        var c=UnityEngine.Object.FindAnyObjectByType<SpatialVoiceIntentController>();
        if(!c || !c.studio.questRig || !c.resolver.gazeCamera.GetComponent<UniversalAdditionalCameraData>().allowXRRendering)
            throw new InvalidOperationException("Quest camera wiring invalid.");
        if(UnityEngine.Object.FindObjectsByType<Camera>().Count(x=>x.enabled)!=1 || UnityEngine.Object.FindObjectsByType<AudioListener>().Count(x=>x.enabled)!=1)
            throw new InvalidOperationException("Exactly one active camera and listener are required.");
        var p=c.GetComponent<QuestVoiceCommandProvider>();
        if(!p || !p.configuration || !p.voiceService || p.voiceService.RuntimeConfiguration.witConfiguration!=p.configuration || c.GetComponent<VoiceCommandRecognizer>().enabled)
            throw new InvalidOperationException("Quest voice wiring invalid.");
        if(c.targets.Length!=13 || Mathf.Abs(c.resolver.stabilizationSeconds-.12f)>.0001f)
            throw new InvalidOperationException("Shared targets or gaze stabilization changed.");
        return "Quest scene validated: 13 targets, one camera/listener, 120 ms head-gaze, existing Wit asset, select_object mapping only.";
    }
}

// Modify only the generated Android manifest, not the source Windows scene or Meta's package files.
public sealed class QuestMicrophoneManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 10000;
    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var file=Path.Combine(path,"src/main/AndroidManifest.xml");
        var xml=new XmlDocument(); xml.Load(file);
        const string ns="http://schemas.android.com/apk/res/android";
        foreach(var permission in new[]{"android.permission.RECORD_AUDIO","android.permission.INTERNET"})
        {
            bool present=xml.DocumentElement.ChildNodes.Cast<XmlNode>().Any(n=>n.Name=="uses-permission" && n.Attributes?["name",ns]?.Value==permission);
            if(!present) { var node=xml.CreateElement("uses-permission"); node.SetAttribute("name",ns,permission); xml.DocumentElement.AppendChild(node); }
        }
        xml.Save(file);
    }
}
