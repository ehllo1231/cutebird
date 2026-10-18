using System;
using System.IO;
using Moonbird;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Moonbird.Editor
{
    public static class BuildTools
    {
        const string ScenePath = "Assets/Moonbird/Scenes/Studio.unity";

        [MenuItem("Moonbird/Prepare Studio Scene")]
        public static void Prepare()
        {
            Directory.CreateDirectory("Assets/Moonbird/Scenes");
            Directory.CreateDirectory("Assets/Moonbird/Resources");
            var materialPath = "Assets/Moonbird/Resources/MoonbirdSprite.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(materialPath))
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (!shader) throw new InvalidOperationException("URP sprite shader was not imported.");
                AssetDatabase.CreateAsset(new Material(shader), materialPath);
            }
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var shaders = graphics.FindProperty("m_AlwaysIncludedShaders");
            foreach (var shaderName in new[] { "UI/Default", "UI/Default Font" })
            {
                var shader = Shader.Find(shaderName);
                if (!shader) continue;
                var found = false;
                for (var i = 0; i < shaders.arraySize; i++) if (shaders.GetArrayElementAtIndex(i).objectReferenceValue == shader) found = true;
                if (!found) { var i = shaders.arraySize; shaders.InsertArrayElementAtIndex(i); shaders.GetArrayElementAtIndex(i).objectReferenceValue = shader; }
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Moonbird Studio", typeof(StudioApp));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            PlayerSettings.companyName = "Moonbird";
            PlayerSettings.productName = "Moonbird Studio";
            PlayerSettings.bundleVersion = StudioApp.Version;
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();
            Debug.Log("MOONBIRD_PREPARE_OK");
        }

        [MenuItem("Moonbird/Build Windows App")]
        public static void BuildWindows() => BuildAt("Builds/Windows");

        [MenuItem("Moonbird/Build Windows App To Developer Folder")]
        public static void BuildDeveloperWindows() => BuildAt("Builds/Windows-Developer");

        static void BuildAt(string directory)
        {
            Prepare();
            Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(directory, "MoonbirdStudio.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result);
            Debug.Log("MOONBIRD_BUILD_OK " + report.summary.totalSize + " bytes");
        }
    }
}
