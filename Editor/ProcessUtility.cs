using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace NvimUnity
{
    internal static class ProcessUtility
    {
        public static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;
        public static bool IsMac => Application.platform == RuntimePlatform.OSXEditor;

        /// <summary>Quotes one argument so CreateProcess / Mono's argument parser hands it over unchanged.</summary>
        public static string QuoteArg(string arg)
        {
            if (arg == null) return "\"\"";
            bool needsQuotes = arg.Length == 0
                               || arg.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) >= 0
                               || (!IsWindows && arg.IndexOfAny(new[] { '\'', '\\', '$', '`', '(', ')', '&', '|', ';', '<', '>', '*', '?' }) >= 0);
            if (!needsQuotes) return arg;

            if (!IsWindows)
            {
                // Mono on macOS/Linux splits arguments like a shell (g_shell_parse_argv).
                var u = new StringBuilder("\"");
                foreach (char c in arg)
                {
                    if (c == '\\' || c == '"' || c == '$' || c == '`') u.Append('\\');
                    u.Append(c);
                }
                return u.Append('"').ToString();
            }

            var sb = new StringBuilder("\"");
            for (int i = 0; i < arg.Length; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == arg.Length)
                {
                    sb.Append('\\', backslashes * 2);
                    break;
                }

                if (arg[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string JoinArgs(IEnumerable<string> args) => string.Join(" ", args.Select(QuoteArg));

        /// <summary>Starts a process and forgets about it.</summary>
        public static bool Start(string fileName, string arguments, string workingDirectory = null, bool hidden = false, bool shellExecute = false)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? "",
                    UseShellExecute = shellExecute,
                    CreateNoWindow = hidden,
                    WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                };
                if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                    psi.WorkingDirectory = workingDirectory;

                NvimSettings.Log($"exec: {fileName} {arguments}");
                using (Process.Start(psi)) { }
                return true;
            }
            catch (Exception e)
            {
                NvimSettings.Warn($"Failed to start '{fileName} {arguments}': {e.Message}");
                return false;
            }
        }

        /// <summary>Runs a process and waits for it (used for small helper commands only).</summary>
        public static int Run(string fileName, string arguments, int timeoutMs, out string output)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? "",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { /* ignored */ }
                        return -1;
                    }
                    output = stdout.Result + stderr.Result;
                    return p.ExitCode;
                }
            }
            catch (Exception e)
            {
                output = e.Message;
                return -1;
            }
        }

        /// <summary>Finds an executable on PATH or in a list of well known locations.</summary>
        public static string FindExecutable(string name, params string[] extraCandidates)
        {
            var candidates = new List<string>();
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try { candidates.Add(Path.Combine(dir.Trim().Trim('"'), name)); }
                catch { /* invalid PATH entry */ }
            }
            candidates.AddRange(extraCandidates.Where(c => !string.IsNullOrEmpty(c)));

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch { /* ignored */ }
            }
            return null;
        }
    }
}
