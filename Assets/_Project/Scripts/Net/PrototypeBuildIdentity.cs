using System;
using System.IO;
using UnityEngine;

namespace SunkCost.Net
{
    // Who we are for compatibility checks: project marker, wire protocol and the
    // source revision the build was made from (docs/STEAM_LOBBY_IMPLEMENTATION_PLAN.md
    // section 6). In a player this is read from sunkcost-build.json next to the
    // executable, written by HQPrototypeBuild. In the editor the editor helper
    // supplies it at play entry from Git. Application.version is display text only.
    [Serializable]
    public sealed class PrototypeBuildIdentity
    {
        public const string ManifestFileName = "sunkcost-build.json";
        public const string Project = "sunkcost-hq";
        public const int Protocol = 1;
        public const int MaxProjectLength = 32;
        public const int MaxBuildLength = 64;
        public const string LocalDevPrefix = "local-dev:";

        public string project = Project;
        public int protocol = Protocol;
        public string revision = string.Empty;
        public string builtUtc = string.Empty;
        public string unityVersion = string.Empty;
        public string packages = string.Empty;
        public bool localOnly;

        public static PrototypeBuildIdentity Current { get; private set; }
        public static string LoadError { get; private set; } = string.Empty;

        public static void Set(PrototypeBuildIdentity identity)
        {
            Current = identity;
            LoadError = identity == null ? "No build identity." : string.Empty;
        }

        // Runtime only. The editor never reads a manifest; its helper calls Set.
        public static bool EnsureLoaded()
        {
            if (Current != null) return true;
            if (Application.isEditor)
            {
                LoadError = "Editor identity was not supplied (enter Play Mode from a clean checkout).";
                return false;
            }
            if (TryLoadFromExecutableDirectory(out PrototypeBuildIdentity loaded, out string error))
            {
                Set(loaded);
                return true;
            }
            LoadError = error;
            return false;
        }

        public static string ExecutableDirectory()
        {
            // <exe dir>/<Product>_Data is Application.dataPath on Windows players.
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        public static bool TryLoadFromExecutableDirectory(out PrototypeBuildIdentity identity, out string error)
        {
            identity = null;
            string path = Path.Combine(ExecutableDirectory(), ManifestFileName);
            if (!File.Exists(path))
            {
                error = "Build manifest missing (" + ManifestFileName + "). Rebuild through Sunk Cost > Prototype > Build.";
                return false;
            }
            try
            {
                identity = JsonUtility.FromJson<PrototypeBuildIdentity>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                error = "Build manifest unreadable: " + exception.Message;
                return false;
            }
            string invalid = identity?.StructuralError();
            if (invalid != null)
            {
                identity = null;
                error = "Build manifest invalid: " + invalid;
                return false;
            }
            error = string.Empty;
            return true;
        }

        public string ToJson() => JsonUtility.ToJson(this, true);

        // Null when the fields are well formed; otherwise the first problem.
        public string StructuralError()
        {
            if (string.IsNullOrEmpty(project) || project.Length > MaxProjectLength) return "project";
            if (protocol <= 0) return "protocol";
            if (string.IsNullOrEmpty(revision) || revision.Length > MaxBuildLength) return "revision";
            return null;
        }

        // A shared Steam build must come from a clean, committed revision.
        public bool IsUsableForSteam(out string reason)
        {
            string structural = StructuralError();
            if (structural != null) { reason = "Build identity invalid (" + structural + ")."; return false; }
            if (localOnly || revision.StartsWith(LocalDevPrefix, StringComparison.Ordinal))
            {
                reason = "This is a local-development build; Steam needs a clean shared build.";
                return false;
            }
            if (!IsFullGitSha(revision)) { reason = "Build revision is not a full Git SHA."; return false; }
            if (!string.Equals(project, Project, StringComparison.Ordinal)) { reason = "Wrong project marker."; return false; }
            if (protocol != Protocol) { reason = "Protocol mismatch with this code."; return false; }
            reason = string.Empty;
            return true;
        }

        // Ordinal, exact. Used by lobby validation and the admission handshake.
        public AdmissionRejection Compare(string remoteProject, int remoteProtocol, string remoteBuild)
        {
            if (!string.Equals(project, remoteProject, StringComparison.Ordinal)) return AdmissionRejection.WrongProject;
            if (protocol != remoteProtocol) return AdmissionRejection.WrongProtocol;
            if (!string.Equals(revision, remoteBuild, StringComparison.Ordinal)) return AdmissionRejection.WrongBuild;
            return AdmissionRejection.None;
        }

        public static bool IsFullGitSha(string value)
        {
            if (value == null || value.Length != 40) return false;
            foreach (char c in value)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
                if (!hex) return false;
            }
            return true;
        }
    }
}
