#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using Dynasty.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Dynasty.Unity.Editor
{
    /// <summary>
    /// Project setup and checks meant for batch mode, e.g.
    /// <c>Unity.exe -batchmode -projectPath unity/DotaDynasty -executeMethod Dynasty.Unity.Editor.BuildTools.ProbeIl2Cpp -logFile -</c>.
    /// Each method exits the editor with 0 on success and 1 on failure.
    /// </summary>
    public static class BuildTools
    {
        public const string BootstrapScene = "Assets/Dynasty/Scenes/Match.unity";
        private const string PipelineFolder = "Assets/Dynasty/Rendering";

        /// <summary>Creates the URP asset and renderer once and makes URP the active pipeline for every quality level.</summary>
        [MenuItem("Dynasty/Setup URP")]
        public static void SetupUrp()
        {
            Run(() =>
            {
                Directory.CreateDirectory(PipelineFolder);
                string assetPath = PipelineFolder + "/DynastyURP.asset";
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
                if (asset == null)
                {
                    var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, PipelineFolder + "/DynastyURP_Renderer.asset");
                    asset = UniversalRenderPipelineAsset.Create(renderer);
                    AssetDatabase.CreateAsset(asset, assetPath);
                }

                GraphicsSettings.defaultRenderPipeline = asset;
                for (int level = 0; level < QualitySettings.names.Length; level++)
                {
                    QualitySettings.SetQualityLevel(level, false);
                    QualitySettings.renderPipeline = asset;
                }

                EnsureBootstrapScene();
                AssetDatabase.SaveAssets();
                Debug.Log("URP set up: " + assetPath);
            });
        }

        /// <summary>
        /// Builds a Windows IL2CPP player, runs it headless with the determinism probe and compares the
        /// checksums with the .NET reference in StreamingAssets. The project's scripting backend is restored afterwards.
        /// </summary>
        [MenuItem("Dynasty/Check IL2CPP Determinism")]
        public static void ProbeIl2Cpp()
        {
            Run(() =>
            {
                string referencePath = Path.Combine(Application.streamingAssetsPath, "determinism-reference.txt");
                if (!File.Exists(referencePath))
                    throw new InvalidOperationException("Run tools/sync-unity.ps1 first: " + referencePath);
                EnsureBootstrapScene();

                NamedBuildTarget target = NamedBuildTarget.Standalone;
                ScriptingImplementation previous = PlayerSettings.GetScriptingBackend(target);
                string buildDir = Path.GetFullPath("Build/ProbeIl2Cpp");
                string exe = Path.Combine(buildDir, "DynastyProbe.exe");
                try
                {
                    PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
                    BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                    {
                        scenes = new[] { BootstrapScene },
                        locationPathName = exe,
                        target = BuildTarget.StandaloneWindows64,
                        options = BuildOptions.None,
                    });
                    if (report.summary.result != BuildResult.Succeeded)
                        throw new InvalidOperationException("IL2CPP build failed: " + report.summary.result);
                }
                finally
                {
                    PlayerSettings.SetScriptingBackend(target, previous);
                }

                string output = Path.Combine(buildDir, "probe-result.txt");
                if (File.Exists(output))
                    File.Delete(output);
                using (Process player = Process.Start(new ProcessStartInfo(exe,
                           "-batchmode -nographics -logFile \"" + Path.Combine(buildDir, "player.log") + "\" " + DeterminismRunner.Argument + " \"" + output + "\"")
                       { UseShellExecute = false })!)
                {
                    if (!player.WaitForExit(10 * 60 * 1000))
                    {
                        player.Kill();
                        throw new TimeoutException("The IL2CPP probe did not finish in 10 minutes.");
                    }
                }

                if (!File.Exists(output))
                    throw new InvalidOperationException("The IL2CPP probe wrote no result; see " + Path.Combine(buildDir, "player.log"));
                string expected = DeterminismProbe.Format(DeterminismProbe.Parse(File.ReadAllText(referencePath)));
                string actual = DeterminismProbe.Format(DeterminismProbe.Parse(File.ReadAllText(output)));
                if (expected != actual)
                    throw new InvalidOperationException("IL2CPP checksums differ from .NET.\nExpected:\n" + expected + "Actual:\n" + actual);
                Debug.Log("IL2CPP determinism OK:\n" + actual);
            });
        }

        private static void EnsureBootstrapScene()
        {
            if (File.Exists(BootstrapScene))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(BootstrapScene)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, BootstrapScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapScene, true) };
        }

        private static void Run(Action action)
        {
            try
            {
                action();
                if (Application.isBatchMode)
                    EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
                else
                    throw;
            }
        }
    }
}
