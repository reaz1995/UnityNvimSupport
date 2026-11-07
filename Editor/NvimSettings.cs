using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NvimUnity
{
    /// <summary>How a new Neovim instance is started when none is listening yet.</summary>
    internal enum LaunchMode
    {
        /// <summary>Windows Terminal (or a plain console window) on Windows, the default terminal on macOS/Linux.</summary>
        Terminal = 0,
        /// <summary>A window inside a tmux session (WSL, macOS, Linux).</summary>
        Tmux = 1,
        /// <summary>A user supplied command line template.</summary>
        Custom = 2,
    }

    /// <summary>
    /// Per-user settings stored in EditorPrefs. Read them on the main thread only.
    /// </summary>
    internal static class NvimSettings
    {
        const string Prefix = "NvimUnity.";

        public static LaunchMode LaunchMode
        {
            get => (LaunchMode)EditorPrefs.GetInt(Prefix + "LaunchMode", (int)LaunchMode.Terminal);
            set => EditorPrefs.SetInt(Prefix + "LaunchMode", (int)value);
        }

        /// <summary>WSL distribution name. Empty = default distribution.</summary>
        public static string WslDistro
        {
            get => EditorPrefs.GetString(Prefix + "WslDistro", "");
            set => EditorPrefs.SetString(Prefix + "WslDistro", value?.Trim() ?? "");
        }

        /// <summary>Where Windows drives are mounted inside WSL (automount root).</summary>
        public static string WslMountRoot
        {
            get => EditorPrefs.GetString(Prefix + "WslMountRoot", "/mnt/");
            set => EditorPrefs.SetString(Prefix + "WslMountRoot", string.IsNullOrWhiteSpace(value) ? "/mnt/" : value.Trim());
        }

        /// <summary>Neovim executable inside WSL (or on macOS/Linux when launching through tmux).</summary>
        public static string WslNvimPath
        {
            get => EditorPrefs.GetString(Prefix + "WslNvimPath", "nvim");
            set => EditorPrefs.SetString(Prefix + "WslNvimPath", string.IsNullOrWhiteSpace(value) ? "nvim" : value.Trim());
        }

        /// <summary>Convert absolute Windows paths in generated .csproj files to WSL paths (WSL target only).</summary>
        public static bool ConvertPathsForWsl
        {
            get => EditorPrefs.GetBool(Prefix + "ConvertPathsForWsl", true);
            set => EditorPrefs.SetBool(Prefix + "ConvertPathsForWsl", value);
        }

        /// <summary>TCP port nvim listens on. 0 = derived from the project path.</summary>
        public static int Port
        {
            get => EditorPrefs.GetInt(Prefix + "Port", 0);
            set => EditorPrefs.SetInt(Prefix + "Port", Mathf.Clamp(value, 0, 65535));
        }

        public static string TmuxSession
        {
            get => EditorPrefs.GetString(Prefix + "TmuxSession", "unity");
            set => EditorPrefs.SetString(Prefix + "TmuxSession", string.IsNullOrWhiteSpace(value) ? "unity" : value.Trim());
        }

        public static string CustomLaunchCommand
        {
            get => EditorPrefs.GetString(Prefix + "CustomLaunchCommand", "");
            set => EditorPrefs.SetString(Prefix + "CustomLaunchCommand", value ?? "");
        }

        /// <summary>Extra arguments appended to the nvim command line when launching.</summary>
        public static string ExtraNvimArgs
        {
            get => EditorPrefs.GetString(Prefix + "ExtraNvimArgs", "");
            set => EditorPrefs.SetString(Prefix + "ExtraNvimArgs", value ?? "");
        }

        /// <summary>Bring the Windows Terminal window to the front after opening a file in a running instance.</summary>
        public static bool FocusTerminal
        {
            get => EditorPrefs.GetBool(Prefix + "FocusTerminal", true);
            set => EditorPrefs.SetBool(Prefix + "FocusTerminal", value);
        }

        /// <summary>Add per-assembly &lt;Compile Include="dir/**/*.cs"/&gt; globs so files created outside Unity get LSP support immediately.</summary>
        public static bool AddCompileGlobs
        {
            get => EditorPrefs.GetBool(Prefix + "AddCompileGlobs", true);
            set => EditorPrefs.SetBool(Prefix + "AddCompileGlobs", value);
        }

        /// <summary>Regenerate all project files after every domain reload (slower, but bullet-proof).</summary>
        public static bool RegenerateOnReload
        {
            get => EditorPrefs.GetBool(Prefix + "RegenerateOnReload", false);
            set => EditorPrefs.SetBool(Prefix + "RegenerateOnReload", value);
        }

        /// <summary>Use the SDK-style project generator of the Visual Studio package when available.</summary>
        public static bool SdkStyleProjects
        {
            get => EditorPrefs.GetBool(Prefix + "SdkStyleProjects", false);
            set => EditorPrefs.SetBool(Prefix + "SdkStyleProjects", value);
        }

        public static bool VerboseLogging
        {
            get => EditorPrefs.GetBool(Prefix + "VerboseLogging", false);
            set => EditorPrefs.SetBool(Prefix + "VerboseLogging", value);
        }

        // ------------------------------------------------------------------ derived values

        public static string ProjectDirectory => Path.GetDirectoryName(Application.dataPath);

        /// <summary>The port nvim listens on for this project (stable per project path).</summary>
        public static int EffectivePort
        {
            get
            {
                int port = Port;
                if (port > 0) return port;
                // FNV-1a over the normalized project path -> 41000..46999 (outside the Windows ephemeral range).
                string key = ProjectDirectory.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
                uint hash = 2166136261;
                foreach (char c in key)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return 41000 + (int)(hash % 6000);
            }
        }

        public static string ListenAddress => "127.0.0.1:" + EffectivePort;

        public static void Log(string message)
        {
            if (VerboseLogging) Debug.Log("[Neovim] " + message);
        }

        public static void Warn(string message) => Debug.LogWarning("[Neovim] " + message);
    }
}
