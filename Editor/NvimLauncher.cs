using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEditor;

namespace NvimUnity
{
    /// <summary>
    /// Opens files in a running Neovim (via RPC) or starts a new one.
    /// Every Neovim started by this package listens on 127.0.0.1:PORT (one port per Unity project),
    /// which is how later "open file" requests find it again - no tmux send-keys tricks needed.
    /// </summary>
    internal static class NvimLauncher
    {
        internal struct Request
        {
            public NvimTarget Target;
            public string NvimPath; // nvim(.exe) or wsl.exe
            public string File;     // absolute Windows/host path or null to just open the project
            public int Line;
            public int Column;
        }

        const string WtWindowName = "nvim-unity";
        const string LaunchedInWtKey = "NvimUnity.LaunchedInWindowsTerminal";

        // Runs inside nvim: open (or jump to) the file and move the cursor.
        const string OpenFileLua = @"
local path, line, col = ...
pcall(vim.cmd, 'stopinsert')
local ok = pcall(vim.cmd, 'drop ' .. vim.fn.fnameescape(path))
if not ok then vim.cmd('edit ' .. vim.fn.fnameescape(path)) end
if line and line > 0 then
  local last = vim.api.nvim_buf_line_count(0)
  pcall(vim.api.nvim_win_set_cursor, 0, { math.min(line, last), math.max((col or 1) - 1, 0) })
  pcall(vim.cmd, 'normal! zz')
end
pcall(vim.cmd, 'redraw')
";

        public static bool Open(Request r)
        {
            int port = NvimSettings.EffectivePort;
            string nvimFile = r.File == null ? null : ToTargetPath(r.Target, r.File);

            NvimRpc.Status status;
            string error;
            if (nvimFile != null)
                status = NvimRpc.ExecLua(port, OpenFileLua, new object[] { nvimFile, r.Line, r.Column }, out error);
            else
                status = NvimRpc.ExecLua(port, "return nil", null, out error);

            switch (status)
            {
                case NvimRpc.Status.Ok:
                    NvimSettings.Log($"Opened {nvimFile ?? "project"} in running Neovim on port {port}.");
                    Focus(r.Target);
                    return true;
                case NvimRpc.Status.Error:
                    NvimSettings.Warn($"Neovim reported an error while opening {nvimFile}: {error}");
                    Focus(r.Target);
                    return true;
                case NvimRpc.Status.Timeout:
                    NvimSettings.Warn("Neovim is running but did not respond (is it waiting on a prompt?). Press <Esc> in Neovim and try again.");
                    Focus(r.Target);
                    return true;
                default:
                    return Launch(r, nvimFile, port);
            }
        }

        static string ToTargetPath(NvimTarget target, string hostPath) =>
            target == NvimTarget.Wsl ? PathUtility.ToWslPath(hostPath, NvimSettings.WslMountRoot) : hostPath;

        // ------------------------------------------------------------------ launching

        static List<string> NvimArgs(string nvimFile, int line, int column, int port)
        {
            var args = new List<string> { "--listen", "127.0.0.1:" + port };
            if (nvimFile != null)
            {
                if (line > 0)
                {
                    args.Add($"+call cursor({line},{Math.Max(column, 1)})");
                    args.Add("+normal! zz");
                }
                args.Add(nvimFile);
            }
            return args;
        }

        static bool Launch(Request r, string nvimFile, int port)
        {
            string projectDir = NvimSettings.ProjectDirectory;
            LaunchMode mode = NvimSettings.LaunchMode;
            List<string> nvimArgs = NvimArgs(nvimFile, r.Line, r.Column, port);
            string extra = NvimSettings.ExtraNvimArgs.Trim();

            NvimSettings.Log($"No Neovim listening on port {port}, launching ({r.Target}, {mode}).");

            if (r.Target == NvimTarget.Native && ProcessUtility.IsWindows)
                return LaunchWindowsNative(r, mode, nvimArgs, extra, projectDir, nvimFile);

            // WSL and macOS/Linux: generate a small sh script that runs nvim through the user's
            // login + interactive shell, so PATH, DOTNET_ROOT etc. from .zshrc/.bashrc are set.
            bool wsl = r.Target == NvimTarget.Wsl;
            string nvimExe = (wsl || mode == LaunchMode.Tmux) ? NvimSettings.WslNvimPath : r.NvimPath;
            string targetProjectDir = wsl ? PathUtility.ToWslPath(projectDir, NvimSettings.WslMountRoot) : projectDir;

            string quotedExe = PathUtility.ShellQuote(nvimExe);
            var nvimCommand = new StringBuilder()
                .Append("command -v ").Append(quotedExe).Append(" >/dev/null 2>&1 || { ")
                .Append("echo \"'").Append(nvimExe.Replace("\"", "").Replace("'", "")).Append("' was not found")
                .Append(wsl ? " in this WSL distribution ($WSL_DISTRO_NAME)." : ".").Append("\"; ")
                .Append("echo \"Set 'WSL distribution' / 'nvim command' in Unity: Preferences > External Tools.\"; ")
                .Append("echo; echo 'Press Enter to close.'; read _; exit 127; }; ")
                .Append("exec ").Append(quotedExe);
            foreach (string a in nvimArgs) nvimCommand.Append(' ').Append(PathUtility.ShellQuote(a));
            if (extra.Length > 0) nvimCommand.Append(' ').Append(extra);

            bool tmux = mode == LaunchMode.Tmux;
            bool macTerminal = !wsl && ProcessUtility.IsMac && mode == LaunchMode.Terminal;
            string script = WriteLaunchScript(targetProjectDir, nvimCommand.ToString(), tmux, macTerminal);
            if (script == null) return false;

            if (wsl)
            {
                string scriptInWsl = PathUtility.ToWslPath(script, NvimSettings.WslMountRoot);
                string wslArgs = WslPrefixArgs() + "-e sh " + ProcessUtility.QuoteArg(scriptInWsl);

                switch (mode)
                {
                    case LaunchMode.Tmux:
                        return ProcessUtility.Start(r.NvimPath, wslArgs, projectDir, hidden: true);
                    case LaunchMode.Custom:
                        return RunCustom(ProcessUtility.QuoteArg(r.NvimPath) + " " + wslArgs, nvimFile, r, projectDir);
                    default:
                        string wt = FindWindowsTerminal();
                        if (wt != null)
                        {
                            SessionState.SetBool(LaunchedInWtKey, true);
                            var wtArgs = new List<string> { "-w", WtWindowName, "new-tab", "--title", WtTabTitle, "--suppressApplicationTitle", r.NvimPath };
                            return ProcessUtility.Start(wt, JoinWt(wtArgs) + " " + EscapeWt(wslArgs), projectDir);
                        }
                        return ProcessUtility.Start(r.NvimPath, wslArgs, projectDir, shellExecute: true);
                }
            }

            // macOS / Linux
            switch (mode)
            {
                case LaunchMode.Tmux:
                    return ProcessUtility.Start("/bin/sh", ProcessUtility.QuoteArg(script), projectDir, hidden: true);
                case LaunchMode.Custom:
                    return RunCustom("sh " + PathUtility.ShellQuote(script), nvimFile, r, projectDir);
                default:
                    if (ProcessUtility.IsMac)
                        return ProcessUtility.Start("/usr/bin/open", "-a Terminal " + ProcessUtility.QuoteArg(script), projectDir, hidden: true);
                    return LaunchLinuxTerminal(script, projectDir);
            }
        }

        static bool LaunchWindowsNative(Request r, LaunchMode mode, List<string> nvimArgs, string extra, string projectDir, string nvimFile)
        {
            string args = ProcessUtility.JoinArgs(nvimArgs) + (extra.Length > 0 ? " " + extra : "");

            if (mode == LaunchMode.Custom)
                return RunCustom(ProcessUtility.QuoteArg(r.NvimPath) + " " + args, nvimFile, r, projectDir);

            if (mode == LaunchMode.Tmux)
                NvimSettings.Warn("tmux mode is not available for native Windows Neovim, using a terminal window instead. Select 'Neovim (WSL)' to use tmux.");

            string wt = FindWindowsTerminal();
            if (wt != null)
            {
                SessionState.SetBool(LaunchedInWtKey, true);
                var wtArgs = new List<string> { "-w", WtWindowName, "new-tab", "--title", WtTabTitle, "--suppressApplicationTitle", "-d", projectDir.TrimEnd('\\', '/'), r.NvimPath };
                return ProcessUtility.Start(wt, JoinWt(wtArgs) + " " + EscapeWt(args), projectDir);
            }

            // ShellExecute on a console program opens a new console window.
            return ProcessUtility.Start(r.NvimPath, args, projectDir, shellExecute: true);
        }

        static bool LaunchLinuxTerminal(string script, string projectDir)
        {
            string quotedScript = ProcessUtility.QuoteArg(script);
            var candidates = new List<KeyValuePair<string, string>>();

            string preferred = Environment.GetEnvironmentVariable("TERMINAL");
            if (!string.IsNullOrEmpty(preferred))
                candidates.Add(new KeyValuePair<string, string>(preferred, "-e sh " + quotedScript));

            candidates.Add(new KeyValuePair<string, string>("x-terminal-emulator", "-e sh " + quotedScript));
            candidates.Add(new KeyValuePair<string, string>("kitty", "sh " + quotedScript));
            candidates.Add(new KeyValuePair<string, string>("wezterm", "start -- sh " + quotedScript));
            candidates.Add(new KeyValuePair<string, string>("alacritty", "-e sh " + quotedScript));
            candidates.Add(new KeyValuePair<string, string>("gnome-terminal", "-- sh " + quotedScript));
            candidates.Add(new KeyValuePair<string, string>("konsole", "-e sh " + quotedScript));
            candidates.Add(new KeyValuePair<string, string>("xfce4-terminal", "-x sh " + quotedScript));
            candidates.Add(new KeyValuePair<string, string>("xterm", "-e sh " + quotedScript));

            foreach (var c in candidates)
            {
                string exe = Path.IsPathRooted(c.Key) ? c.Key : ProcessUtility.FindExecutable(c.Key);
                if (exe != null && File.Exists(exe))
                    return ProcessUtility.Start(exe, c.Value, projectDir);
            }

            NvimSettings.Warn("No terminal emulator found. Set the TERMINAL environment variable or use the 'Custom' launch mode.");
            return false;
        }

        static bool RunCustom(string nvimCommand, string nvimFile, Request r, string projectDir)
        {
            string template = NvimSettings.CustomLaunchCommand.Trim();
            if (template.Length == 0)
            {
                NvimSettings.Warn("Launch mode is 'Custom' but no custom command is set (Preferences > External Tools).");
                return false;
            }

            string cmd = template
                .Replace("{nvimCommand}", nvimCommand)
                .Replace("{file}", nvimFile ?? "")
                .Replace("{line}", Math.Max(r.Line, 1).ToString())
                .Replace("{column}", Math.Max(r.Column, 1).ToString())
                .Replace("{project}", projectDir)
                .Replace("{listen}", NvimSettings.ListenAddress);

            if (ProcessUtility.IsWindows)
                return ProcessUtility.Start("cmd.exe", "/s /c \"" + cmd + "\"", projectDir, hidden: true);
            return ProcessUtility.Start("/bin/sh", "-c " + ProcessUtility.QuoteArg(cmd), projectDir, hidden: true);
        }

        // ------------------------------------------------------------------ focus

        static void Focus(NvimTarget target)
        {
            LaunchMode mode = NvimSettings.LaunchMode;

            if (mode == LaunchMode.Tmux && (target == NvimTarget.Wsl || !ProcessUtility.IsWindows))
            {
                string selectArgs = "select-window -t " + ProcessUtility.QuoteArg(NvimSettings.TmuxSession + ":nvim");
                if (target == NvimTarget.Wsl)
                    ProcessUtility.Start(WslExe, WslPrefixArgs() + "-e tmux " + selectArgs, null, hidden: true);
                else if (ProcessUtility.FindExecutable("tmux", "/opt/homebrew/bin/tmux", "/usr/local/bin/tmux") is string tmux)
                    ProcessUtility.Start(tmux, selectArgs, null, hidden: true);
                return;
            }

            if (mode == LaunchMode.Terminal && ProcessUtility.IsWindows && NvimSettings.FocusTerminal
                && SessionState.GetBool(LaunchedInWtKey, false))
            {
                FocusTerminalWindow(WtTabTitle);
            }
        }

        // Brings the Windows Terminal window whose active tab is our nvim tab to the front.
        // (wt's own "focus-tab -t N" switches tabs by index, which picks the wrong tab when the window has several.)
        static void FocusTerminalWindow(string title)
        {
            try
            {
                var candidates = new List<IntPtr>();
                IntPtr match = IntPtr.Zero;
                EnumWindows((hWnd, _) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    var cls = new StringBuilder(256);
                    GetClassName(hWnd, cls, cls.Capacity);
                    if (cls.ToString() != "CASCADIA_HOSTING_WINDOW_CLASS") return true;
                    candidates.Add(hWnd);
                    var text = new StringBuilder(512);
                    GetWindowText(hWnd, text, text.Capacity);
                    if (text.ToString() == title) match = hWnd;
                    return true;
                }, IntPtr.Zero);

                if (match == IntPtr.Zero && candidates.Count == 1) match = candidates[0];
                if (match == IntPtr.Zero) return;

                if (IsIconic(match)) ShowWindow(match, 9 /* SW_RESTORE */);
                SetForegroundWindow(match);
            }
            catch (Exception e)
            {
                NvimSettings.Log("Could not focus Windows Terminal: " + e.Message);
            }
        }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        // ------------------------------------------------------------------ helpers

        static string ProjectName => Path.GetFileName(NvimSettings.ProjectDirectory);

        static string WtTabTitle => "nvim - " + ProjectName;

        static string WslExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");

        static string WslPrefixArgs()
        {
            string distro = NvimSettings.WslDistro;
            return string.IsNullOrEmpty(distro) ? "" : "-d " + ProcessUtility.QuoteArg(distro) + " ";
        }

        static string FindWindowsTerminal()
        {
            if (!ProcessUtility.IsWindows) return null;
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return ProcessUtility.FindExecutable("wt.exe", Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe"));
        }

        // Windows Terminal uses ';' as a command separator, escape it.
        static string EscapeWt(string s) => s.Replace(";", "\\;");

        static string JoinWt(IEnumerable<string> args) => string.Join(" ", args.Select(a => ProcessUtility.QuoteArg(EscapeWt(a))));

        static string WriteLaunchScript(string targetProjectDir, string nvimCommand, bool tmux, bool macCommandFile)
        {
            string dir = Path.Combine(NvimSettings.ProjectDirectory, "Library", "NvimUnity");
            string path = Path.Combine(dir, macCommandFile ? "launch-nvim.command" : "launch-nvim.sh");

            var sb = new StringBuilder();
            sb.Append("#!/bin/sh\n");
            sb.Append("# Generated by the Neovim Unity package on every launch - edits will be overwritten.\n");
            sb.Append("cd ").Append(PathUtility.ShellQuote(targetProjectDir)).Append(" 2>/dev/null || true\n");
            sb.Append("user_shell=\"$(getent passwd \"$(id -un)\" 2>/dev/null | cut -d: -f7)\"\n");
            sb.Append("if [ -z \"$user_shell\" ] || [ ! -x \"$user_shell\" ]; then user_shell=\"${SHELL:-/bin/sh}\"; fi\n");
            sb.Append("nvim_cmd=").Append(PathUtility.ShellQuote(nvimCommand)).Append('\n');

            if (tmux)
            {
                sb.Append("session=").Append(PathUtility.ShellQuote(NvimSettings.TmuxSession)).Append('\n');
                sb.Append("if ! command -v tmux >/dev/null 2>&1; then echo 'tmux is not installed' >&2; exit 1; fi\n");
                sb.Append("if tmux has-session -t \"=$session\" 2>/dev/null; then\n");
                sb.Append("  tmux new-window -t \"$session:\" -n nvim -c \"$PWD\" \"$user_shell\" -lic \"$nvim_cmd\"\n");
                sb.Append("else\n");
                sb.Append("  tmux new-session -d -s \"$session\" -n nvim -c \"$PWD\" \"$user_shell\" -lic \"$nvim_cmd\"\n");
                sb.Append("fi\n");
            }
            else
            {
                sb.Append("exec \"$user_shell\" -lic \"$nvim_cmd\"\n");
            }

            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                if (macCommandFile)
                    ProcessUtility.Run("/bin/chmod", "+x " + ProcessUtility.QuoteArg(path), 2000, out _);
                return path;
            }
            catch (Exception e)
            {
                NvimSettings.Warn($"Could not write launch script '{path}': {e.Message}");
                return null;
            }
        }
    }
}
