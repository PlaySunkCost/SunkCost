using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using SunkCost.Net;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SunkCost.Editor.Prototype
{
    // Editor-only source of build identity (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 6): reads the Git revision and cleanliness of this checkout, supplies
    // the same identity to Play Mode that a packaged build carries in its manifest,
    // and creates the manifest object for HQPrototypeBuild. Player code never runs Git.
    public static class PrototypeBuildIdentityEditor
    {
        private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [InitializeOnLoadMethod]
        private static void Register()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            // After the domain reload, so the static survives into Play Mode.
            if (change == PlayModeStateChange.EnteredPlayMode)
                SupplyPlayModeIdentity();
        }

        // Called automatically when entering Play Mode. A clean checkout gets its real
        // revision; a dirty one gets a local-dev identity that Steam mode refuses.
        public static PrototypeBuildIdentity SupplyPlayModeIdentity()
        {
            PrototypeBuildIdentity identity;
            try
            {
                bool dirty = IsWorkingTreeDirty(out string details);
                identity = Create(dirty);
                if (dirty)
                    Debug.Log("[BuildIdentity] Play Mode identity is local-dev (uncommitted changes):\n" + details);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BuildIdentity] Git unavailable; Play Mode identity is local-dev. " + exception.Message);
                identity = new PrototypeBuildIdentity
                {
                    revision = PrototypeBuildIdentity.LocalDevPrefix + "unknown",
                    builtUtc = DateTime.UtcNow.ToString("o"),
                    unityVersion = Application.unityVersion,
                    packages = PackageSummary(),
                    localOnly = true
                };
            }
            PrototypeBuildIdentity.Set(identity);
            return identity;
        }

        public static PrototypeBuildIdentity Create(bool localOnly)
        {
            string head = GetHeadRevision();
            return new PrototypeBuildIdentity
            {
                project = PrototypeBuildIdentity.Project,
                protocol = PrototypeBuildIdentity.Protocol,
                revision = localOnly ? PrototypeBuildIdentity.LocalDevPrefix + head : head,
                builtUtc = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                packages = PackageSummary(),
                localOnly = localOnly
            };
        }

        public static string GetHeadRevision()
        {
            string head = RunGit("rev-parse HEAD").Trim();
            if (!PrototypeBuildIdentity.IsFullGitSha(head))
                throw new InvalidOperationException("git rev-parse HEAD did not return a revision: '" + head + "'");
            return head;
        }

        // Tracked changes anywhere, plus untracked files under the source, asset,
        // package and settings folders. Ignored files (Builds/, Library/, logs) are
        // excluded by Git itself.
        public static bool IsWorkingTreeDirty(out string details)
        {
            string status = RunGit("status --porcelain --untracked-files=all");
            var sb = new StringBuilder();
            foreach (string rawLine in status.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length < 4) continue;
                string code = line.Substring(0, 2);
                string path = line.Substring(3);
                bool untracked = code == "??";
                if (untracked && !(path.StartsWith("Assets/", StringComparison.Ordinal) ||
                                   path.StartsWith("Packages/", StringComparison.Ordinal) ||
                                   path.StartsWith("ProjectSettings/", StringComparison.Ordinal)))
                    continue;
                sb.AppendLine(line);
            }
            details = sb.ToString().TrimEnd();
            return details.Length > 0;
        }

        private static string PackageSummary()
        {
            return "fishnet=4.7.3;steamworks.net=2025.164.1;fishysteamworks=4.1.1";
        }

        private static string RunGit(string arguments)
        {
            var start = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = ProjectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Could not start git.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("git " + arguments + " timed out."); }
            if (process.ExitCode != 0) throw new InvalidOperationException("git " + arguments + " failed: " + error.Trim());
            return output;
        }
    }
}
