using System;
using System.IO;
using SunkCost.Net;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SunkCost.Editor.Prototype
{
    public static class HQPrototypeBuild
    {
        public const string OutputPath = "Builds/HQPrototype/SunkCostHQ.exe";
        public const string LocalOutputPath = "Builds/HQPrototypeLocal/SunkCostHQ.exe";
        public const string LinuxOutputPath = "Builds/HQPrototypeLinux/SunkCostHQ";

        // The shareable Steam build: requires a clean checkout so every tester's
        // build carries the same Git revision (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
        // section 6).
        [MenuItem("Sunk Cost/Prototype/Build Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            if (PrototypeBuildIdentityEditor.IsWorkingTreeDirty(out string details))
                throw new InvalidOperationException("Commit/stash project changes before building a shared Steam test:\n" + details);
            Build(OutputPath, BuildTarget.StandaloneWindows64, PrototypeBuildIdentityEditor.Create(localOnly: false));
        }

        // The GitHub Actions build (.github/workflows/build.yml): the checkout is
        // the pushed commit, so the build carries that revision as a shared Steam
        // build. Unity rewrites Packages/manifest.json and packages-lock.json when
        // it imports on the runner; that rewrite is Unity's own and is tolerated
        // (and logged). Any other change means the runner is not building the
        // commit it claims, and the build refuses.
        public static void BuildWindowsCI()
        {
            if (PrototypeBuildIdentityEditor.IsWorkingTreeDirty(out string details))
            {
                var unexpected = new System.Collections.Generic.List<string>();
                foreach (string rawLine in details.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0) continue;
                    string path = line.Length > 3 ? line.Substring(2).Trim() : line;
                    if (path != "Packages/manifest.json" && path != "Packages/packages-lock.json") unexpected.Add(line);
                }
                if (unexpected.Count > 0)
                    throw new InvalidOperationException("The CI checkout differs from the pushed commit:\n" + string.Join("\n", unexpected));
                Debug.Log("[Build] Unity rewrote the package files at import; building the checked-out commit anyway:\n" + details);
            }
            Build(OutputPath, BuildTarget.StandaloneWindows64, PrototypeBuildIdentityEditor.Create(localOnly: false));
        }

        // Local two-process testing of uncommitted work. The manifest is marked
        // localOnly and the runtime refuses Steam with it.
        [MenuItem("Sunk Cost/Prototype/Build Windows Local Development")]
        public static void BuildWindowsLocalDevelopment()
        {
            bool dirty = PrototypeBuildIdentityEditor.IsWorkingTreeDirty(out _);
            Build(LocalOutputPath, BuildTarget.StandaloneWindows64, PrototypeBuildIdentityEditor.Create(localOnly: dirty));
        }

        // Linux counterpart of the shareable Windows build, used by CI (see
        // .github/workflows/build.yml) and for Linux testers. Same clean-checkout
        // requirement so every platform's shared build carries the same revision.
        [MenuItem("Sunk Cost/Prototype/Build Linux Development")]
        public static void BuildLinuxDevelopment()
        {
            if (PrototypeBuildIdentityEditor.IsWorkingTreeDirty(out string details))
                throw new InvalidOperationException("Commit/stash project changes before building a shared Steam test:\n" + details);
            Build(LinuxOutputPath, BuildTarget.StandaloneLinux64, PrototypeBuildIdentityEditor.Create(localOnly: false));
        }

        // Session first; then every world scene on disk (WorldSceneChecks.BuildListPaths).
        private static string[] BuildScenes()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (string path in WorldSceneChecks.BuildListPaths)
                if (File.Exists(path)) scenes.Add(path);
            return scenes.ToArray();
        }

        private static void Build(string outputPath, BuildTarget target, PrototypeBuildIdentity identity)
        {
            EditorSceneManager.OpenScene(SunkCost.World.WorldScenes.SessionPath);
            SessionSceneValidator.ValidateOrThrow();
            EditorSceneManager.OpenScene(HQPrototypeBuilder.ScenePath);
            HQPrototypeValidator.ValidateOrThrow();
            EditorSceneManager.OpenScene(SunkCost.World.WorldScenes.SessionPath);
            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(outputDirectory) || !Path.GetFullPath(outputDirectory).StartsWith(Path.GetFullPath("Builds"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The prototype build output directory must be inside Builds/.");

            Directory.CreateDirectory(outputDirectory);
            // A failed build must not leave the previous manifest looking current.
            string manifestPath = Path.Combine(outputDirectory, PrototypeBuildIdentity.ManifestFileName);
            if (File.Exists(manifestPath)) File.Delete(manifestPath);

            BuildPlayerOptions options = new()
            {
                scenes = BuildScenes(),
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.Development
            };
            // A missing override in any volume profile makes URP's shader preprocessor throw
            // while the build sets up, and the whole build then compiles unstripped (1 h 53 min,
            // 7,776 Lit variants per pass; 24 September 2026). Clean them first.
            SunkCost.Editor.Look.VolumeProfileAssets.RemoveMissingOverrides();
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"HQ build failed: {report.summary.result} ({report.summary.totalErrors} errors).");

            File.WriteAllText(Path.Combine(outputDirectory, "steam_appid.txt"), "480" + Environment.NewLine);
            File.WriteAllText(manifestPath, identity.ToJson());
            Debug.Log($"HQ {target} build succeeded: {outputPath} ({report.summary.totalSize} bytes); revision {identity.revision}; localOnly={identity.localOnly}");
        }
    }
}
