using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BaseConquest.Editor
{
    public static class BuildAndroidDemo
    {
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            bool lanTest = args.Contains("-demoLanDuelTest");
            int pathArg = Array.IndexOf(args, "-demoApkPath");
            string output = Path.GetFullPath(pathArg >= 0 && pathArg + 1 < args.Length
                ? args[pathArg + 1] : lanTest ? "Logs/AndroidQa/lan-duel-qa.apk" : "Builds/Android/DEMO.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            bool previousBundle = EditorUserBuildSettings.buildAppBundle;
            string previousId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            int exitCode = 0;
            try
            {
                EditorUserBuildSettings.buildAppBundle = false;
                if (lanTest) PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.baseconquest.lanqa");
                var options = BuildOptions.CompressWithLz4HC | BuildOptions.DetailedBuildReport;
                if (args.Contains("-demoDevelopment")) options |= BuildOptions.Development;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    target = BuildTarget.Android,
                    extraScriptingDefines = lanTest ? new[] { "LAN_DUEL_QA" } : Array.Empty<string>(),
                    locationPathName = output,
                    scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                    options = options
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"Android 构建结果 {report.summary.result}，错误 {report.summary.totalErrors} 项。");

                var entries = report.packedAssets.SelectMany(file => file.contents)
                    .GroupBy(asset => asset.sourceAssetPath)
                    .Select(group => new { Path = group.Key, Bytes = group.Sum(asset => (long)asset.packedSize) })
                    .OrderByDescending(asset => asset.Bytes);
                File.WriteAllLines(Path.ChangeExtension(output, ".assets.tsv"),
                    new[] { "PackedBytes\tSourceAsset" }.Concat(entries.Select(asset =>
                        asset.Bytes.ToString(CultureInfo.InvariantCulture) + "\t" + asset.Path)));
                Debug.Log($"Android APK: {new FileInfo(output).Length:N0} bytes · {output}");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (!Application.isBatchMode) throw;
                exitCode = 1;
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBundle;
                if (lanTest) PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousId);
            }
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }
    }
}
