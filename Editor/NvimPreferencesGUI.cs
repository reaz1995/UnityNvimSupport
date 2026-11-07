using UnityEditor;
using UnityEngine;

namespace NvimUnity
{
    /// <summary>Settings drawn under Preferences > External Tools when Neovim is selected.</summary>
    internal static class NvimPreferencesGUI
    {
        static readonly GUIContent s_LaunchMode = new GUIContent("Launch new Neovim in",
            "Used only when no Neovim is listening for this project yet. Files are then opened in the running instance.");
        static readonly GUIContent s_Port = new GUIContent("RPC port (0 = auto)",
            "Neovim is started with --listen 127.0.0.1:PORT. Auto derives a stable port from the project path, so every project gets its own instance.");
        static readonly GUIContent s_Extra = new GUIContent("Extra nvim arguments", "Appended to the nvim command line when a new instance is launched.");
        static readonly GUIContent s_Focus = new GUIContent("Focus Windows Terminal", "Bring the Windows Terminal window to the front when a file is opened.");
        static readonly GUIContent s_Distro = new GUIContent("WSL distribution", "Leave empty for the default distribution (wsl -l -v).");
        static readonly GUIContent s_MountRoot = new GUIContent("WSL mount root", "Where Windows drives are mounted inside WSL ([automount] root in /etc/wsl.conf). Default /mnt/.");
        static readonly GUIContent s_WslNvim = new GUIContent("nvim command", "nvim executable used when launching through a shell script (WSL, tmux). Resolved by your login shell, so a name on PATH is fine.");
        static readonly GUIContent s_Convert = new GUIContent("Convert .csproj paths to WSL", "Rewrites absolute Windows paths (references, analyzers, ...) to /mnt/c/... so Roslyn inside WSL can resolve them.");
        static readonly GUIContent s_Globs = new GUIContent("Add wildcard Compile items",
            "Adds <Compile Include=\"<assembly folder>/**/*.cs\"/> per assembly, so files created from Neovim get LSP support right away.");
        static readonly GUIContent s_Reload = new GUIContent("Regenerate after every domain reload",
            "Normally project files are updated incrementally when scripts/asmdefs change. Enable if you still see stale project files.");
        static readonly GUIContent s_Sdk = new GUIContent("SDK-style projects", "Use the SDK-style generator of the Visual Studio package (2.0.20+) instead of legacy-style projects.");

        public static void Draw(NvimTarget target)
        {
            if (!ProjectFiles.IsGeneratorAvailable)
            {
                EditorGUILayout.HelpBox("The 'Visual Studio Editor' package (com.unity.ide.visualstudio) is required to generate .sln/.csproj files. Install it from the Package Manager.", MessageType.Error);
            }

            EditorGUILayout.LabelField("Neovim", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            NvimSettings.LaunchMode = (LaunchMode)EditorGUILayout.EnumPopup(s_LaunchMode, NvimSettings.LaunchMode);
            switch (NvimSettings.LaunchMode)
            {
                case LaunchMode.Tmux:
                    NvimSettings.TmuxSession = EditorGUILayout.TextField("tmux session", NvimSettings.TmuxSession);
                    break;
                case LaunchMode.Custom:
                    NvimSettings.CustomLaunchCommand = EditorGUILayout.TextField("Custom command", NvimSettings.CustomLaunchCommand);
                    EditorGUILayout.HelpBox("Placeholders: {nvimCommand} (complete command that starts nvim, incl. --listen), {file}, {line}, {column}, {project}, {listen}.\n" +
                                            "Example: wezterm start --cwd \"{project}\" -- {nvimCommand}", MessageType.None);
                    break;
                default:
                    if (Application.platform == RuntimePlatform.WindowsEditor)
                        NvimSettings.FocusTerminal = EditorGUILayout.Toggle(s_Focus, NvimSettings.FocusTerminal);
                    break;
            }

            NvimSettings.Port = EditorGUILayout.IntField(s_Port, NvimSettings.Port);
            EditorGUILayout.LabelField(" ", "nvim --listen " + NvimSettings.ListenAddress, EditorStyles.miniLabel);
            NvimSettings.ExtraNvimArgs = EditorGUILayout.TextField(s_Extra, NvimSettings.ExtraNvimArgs);

            if (target == NvimTarget.Wsl || (Application.platform != RuntimePlatform.WindowsEditor && NvimSettings.LaunchMode == LaunchMode.Tmux))
                NvimSettings.WslNvimPath = EditorGUILayout.TextField(s_WslNvim, NvimSettings.WslNvimPath);

            if (target == NvimTarget.Wsl)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("WSL", EditorStyles.boldLabel);
                NvimSettings.WslDistro = EditorGUILayout.TextField(s_Distro, NvimSettings.WslDistro);
                NvimSettings.WslMountRoot = EditorGUILayout.TextField(s_MountRoot, NvimSettings.WslMountRoot);
                NvimSettings.ConvertPathsForWsl = EditorGUILayout.Toggle(s_Convert, NvimSettings.ConvertPathsForWsl);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Project files", EditorStyles.boldLabel);
            NvimSettings.AddCompileGlobs = EditorGUILayout.Toggle(s_Globs, NvimSettings.AddCompileGlobs);
            NvimSettings.RegenerateOnReload = EditorGUILayout.Toggle(s_Reload, NvimSettings.RegenerateOnReload);
            NvimSettings.SdkStyleProjects = EditorGUILayout.Toggle(s_Sdk, NvimSettings.SdkStyleProjects);
            ProjectFiles.DrawGenerationFlagsGUI();

            NvimSettings.VerboseLogging = EditorGUILayout.Toggle("Verbose logging", NvimSettings.VerboseLogging);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("Regenerate project files", GUILayout.Width(180)))
                {
                    AssetDatabase.Refresh();
                    ProjectFiles.Sync();
                }
                if (GUILayout.Button("Show launch script", GUILayout.Width(140)))
                {
                    EditorUtility.RevealInFinder(System.IO.Path.Combine(NvimSettings.ProjectDirectory, "Library", "NvimUnity",
                        Application.platform == RuntimePlatform.OSXEditor && NvimSettings.LaunchMode == LaunchMode.Terminal && target != NvimTarget.Wsl
                            ? "launch-nvim.command" : "launch-nvim.sh"));
                }
            }

            EditorGUI.indentLevel--;
        }
    }
}
