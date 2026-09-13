using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    public static class HQPrototypeBuild
    {
        public const string OutputPath = "Builds/HQPrototype/SunkCostHQ.exe";

        [MenuItem("Sunk Cost/Prototype/Build Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            EditorSceneManager.OpenScene(HQPrototypeBuilder.ScenePath);
            HQPrototypeValidator.ValidateOrThrow();
            string outputDirectory = Path.GetDirectoryName(OutputPath);
            if (string.IsNullOrEmpty(outputDirectory))
                throw new InvalidOperationException("The prototype build output directory is invalid.");

            Directory.CreateDirectory(outputDirectory);
            BuildPlayerOptions options = new()
            {
                scenes = new[] { HQPrototypeBuilder.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"HQ build failed: {report.summary.result} ({report.summary.totalErrors} errors).");

            File.WriteAllText(Path.Combine(outputDirectory, "steam_appid.txt"), "480" + Environment.NewLine);
            Debug.Log($"HQ Windows build succeeded: {OutputPath} ({report.summary.totalSize} bytes)");
        }
    }
}
