using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VRLauncher.EditorTools
{
    /// <summary>
    /// Headless build entry point, invoked by build.ps1 via -executeMethod.
    /// Exists so the build reports a real exit code: Unity's -buildWindows64Player
    /// flag returns 0 even when the player was never produced.
    /// </summary>
    public static class BuildScript
    {
        private const string DefaultOutput = "Build/vr-launch.exe";

        public static void BuildWindows64()
        {
            string output = GetArg("-buildOutput") ?? DefaultOutput;

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Fail("No enabled scenes in EditorBuildSettings - nothing to build.");
                return;
            }

            Debug.Log($"Building {scenes.Length} scene(s) to {output}");

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"Build result: {summary.result} ({summary.totalErrors} error(s), {summary.totalWarnings} warning(s))");

            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Build failed with result '{summary.result}'.");
                return;
            }

            EditorApplication.Exit(0);
        }

        private static void Fail(string message)
        {
            Debug.LogError(message);
            EditorApplication.Exit(1);
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();

            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
