using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

namespace NvimUnity
{
    internal enum NvimTarget
    {
        /// <summary>nvim.exe on Windows, nvim on macOS/Linux.</summary>
        Native,
        /// <summary>nvim running inside WSL, started through wsl.exe.</summary>
        Wsl,
    }

    /// <summary>
    /// Registers Neovim as an External Script Editor (Preferences > External Tools).
    /// </summary>
    [InitializeOnLoad]
    public class NvimCodeEditor : IExternalCodeEditor
    {
        internal const string WslInstallationName = "Neovim (WSL)";
        internal const string NativeInstallationName = "Neovim";

        static readonly NvimCodeEditor s_Instance = new NvimCodeEditor();
        static CodeEditor.Installation[] s_Installations;

        string m_InstallationPath;

        static NvimCodeEditor()
        {
            CodeEditor.Register(s_Instance);
            EditorApplication.delayCall += OnEditorLoaded;
        }

        /// <summary>True when Neovim is the selected External Script Editor.</summary>
        public static bool IsCurrent => TryGetInstallation(CodeEditor.CurrentEditorInstallation, out _);

        internal static NvimTarget CurrentTarget => TargetForPath(CodeEditor.CurrentEditorInstallation);

        internal static NvimTarget TargetForPath(string path)
        {
            string name = SafeFileName(path);
            return name == "wsl" ? NvimTarget.Wsl : NvimTarget.Native;
        }

        static void OnEditorLoaded()
        {
            if (!IsCurrent) return;
            if (NvimSettings.RegenerateOnReload || !ProjectFiles.SolutionExists)
                ProjectFiles.Sync();
        }

        // ------------------------------------------------------------------ IExternalCodeEditor

        public CodeEditor.Installation[] Installations => s_Installations ?? (s_Installations = Discover());

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation) =>
            TryGetInstallation(editorPath, out installation);

        public void Initialize(string editorInstallationPath)
        {
            m_InstallationPath = editorInstallationPath;
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            string[] none = Array.Empty<string>();
            string[] affected = (addedFiles ?? none).Concat(deletedFiles ?? none).Concat(movedFiles ?? none).Concat(movedFromFiles ?? none).ToArray();
            ProjectFiles.SyncIfNeeded(affected, importedFiles ?? none);
        }

        public void SyncAll()
        {
            AssetDatabase.Refresh();
            ProjectFiles.Sync();
        }

        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            if (!ProjectFiles.SolutionExists)
                ProjectFiles.Sync();

            string path = m_InstallationPath;
            if (string.IsNullOrEmpty(path) || !TryGetInstallation(path, out _))
                path = CodeEditor.CurrentEditorInstallation;

            string file = null;
            if (!string.IsNullOrEmpty(filePath))
            {
                file = Path.IsPathRooted(filePath) ? filePath : Path.Combine(NvimSettings.ProjectDirectory, filePath);
                file = Path.GetFullPath(file);
            }

            return NvimLauncher.Open(new NvimLauncher.Request
            {
                Target = TargetForPath(path),
                NvimPath = path,
                File = file,
                Line = line,
                Column = column,
            });
        }

        public void OnGUI()
        {
            NvimPreferencesGUI.Draw(TargetForPath(m_InstallationPath ?? CodeEditor.CurrentEditorInstallation));
        }

        // ------------------------------------------------------------------ discovery

        static bool TryGetInstallation(string editorPath, out CodeEditor.Installation installation)
        {
            installation = default;
            string name = SafeFileName(editorPath);
            switch (name)
            {
                case "wsl":
                    installation = new CodeEditor.Installation { Name = WslInstallationName, Path = editorPath };
                    return true;
                case "nvim":
                    installation = new CodeEditor.Installation { Name = NativeInstallationName, Path = editorPath };
                    return true;
                default:
                    return false;
            }
        }

        static string SafeFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try
            {
                return Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        static CodeEditor.Installation[] Discover()
        {
            var list = new List<CodeEditor.Installation>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (ProcessUtility.IsWindows)
            {
                string wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
                if (File.Exists(wsl))
                    list.Add(new CodeEditor.Installation { Name = WslInstallationName, Path = wsl });

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string nvim = ProcessUtility.FindExecutable("nvim.exe",
                    Path.Combine(programFiles, "Neovim", "bin", "nvim.exe"),
                    Path.Combine(home, "scoop", "shims", "nvim.exe"),
                    Path.Combine(home, "scoop", "apps", "neovim", "current", "bin", "nvim.exe"),
                    @"C:\ProgramData\chocolatey\bin\nvim.exe",
                    Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "nvim.exe"),
                    Path.Combine(localAppData, "bob", "nvim-bin", "nvim.exe"));
                if (nvim != null)
                    list.Add(new CodeEditor.Installation { Name = NativeInstallationName, Path = nvim });
            }
            else
            {
                string nvim = ProcessUtility.FindExecutable("nvim",
                    "/opt/homebrew/bin/nvim",
                    "/usr/local/bin/nvim",
                    "/usr/bin/nvim",
                    "/snap/bin/nvim",
                    "/opt/nvim/bin/nvim",
                    "/opt/nvim-linux-x86_64/bin/nvim",
                    Path.Combine(home, ".local", "bin", "nvim"),
                    Path.Combine(home, ".local", "share", "bob", "nvim-bin", "nvim"));
                if (nvim != null)
                    list.Add(new CodeEditor.Installation { Name = NativeInstallationName, Path = nvim });
            }

            return list.ToArray();
        }

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/Neovim/Regenerate Project Files")]
        static void RegenerateMenu()
        {
            AssetDatabase.Refresh();
            if (ProjectFiles.Sync())
                Debug.Log("[Neovim] Project files regenerated: " + ProjectFiles.SolutionPath);
        }

        [MenuItem("Tools/Neovim/Open Project in Neovim")]
        static void OpenProjectMenu()
        {
            if (!IsCurrent)
            {
                NvimSettings.Warn("Select 'Neovim' or 'Neovim (WSL)' in Preferences > External Tools > External Script Editor first.");
                SettingsService.OpenUserPreferences("Preferences/External Tools");
                return;
            }
            s_Instance.OpenProject();
        }
    }
}
