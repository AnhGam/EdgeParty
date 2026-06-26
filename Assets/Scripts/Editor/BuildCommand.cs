using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EdgeParty.Editor
{
    public static class BuildCommand
    {
        [MenuItem("Build/Build Windows Client")]
        public static void PerformWindowsClientBuild()
        {
            Debug.Log("[BuildCommand] Starting Windows Client build...");

            string[] scenes = new string[]
            {
                "Assets/Scenes/MainMenu.unity",
                "Assets/Scenes/SampleScene.unity",
                "Assets/Scenes/GameMap/Forest/Forest Pack/Maps/DemoScene_Forest.unity"
            };

            string buildPath = "Builds/Windows/EdgeParty.exe";
            
            // Ensure target directory exists
            string directory = Path.GetDirectoryName(buildPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = scenes;
            buildPlayerOptions.locationPathName = buildPath;
            buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
            buildPlayerOptions.options = BuildOptions.None;

            var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            var summary = report.summary;

            if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[BuildCommand] Windows Client build succeeded! Output size: {summary.totalSize} bytes.");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            else if (summary.result == UnityEditor.Build.Reporting.BuildResult.Failed)
            {
                Debug.LogError($"[BuildCommand] Windows Client build failed! Errors: {summary.totalErrors}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}
