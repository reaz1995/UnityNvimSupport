using System;
using System.Text;
using System.Text.RegularExpressions;

namespace NvimUnity
{
    internal static class PathUtility
    {
        // C:\foo or C:/foo
        static readonly Regex s_DrivePath = new Regex(@"^(?<drive>[A-Za-z]):[\\/](?<rest>.*)$", RegexOptions.Singleline);

        // \\wsl$\Ubuntu\home\me or \\wsl.localhost\Ubuntu\home\me
        static readonly Regex s_WslUncPath = new Regex(@"^[\\/]{2}(?:wsl\$|wsl\.localhost)[\\/][^\\/]+(?<rest>(?:[\\/].*)?)$",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        public static bool IsWindowsAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return s_DrivePath.IsMatch(path) || s_WslUncPath.IsMatch(path);
        }

        /// <summary>
        /// Converts an absolute Windows path to the path WSL sees.
        /// C:\Program Files\Unity -> /mnt/c/Program Files/Unity, \\wsl$\Ubuntu\home\me -> /home/me.
        /// Anything else only gets its separators normalized.
        /// </summary>
        public static string ToWslPath(string windowsPath, string mountRoot)
        {
            if (string.IsNullOrWhiteSpace(windowsPath)) return windowsPath;

            var unc = s_WslUncPath.Match(windowsPath);
            if (unc.Success)
            {
                string rest = unc.Groups["rest"].Value.Replace('\\', '/');
                return rest.Length == 0 ? "/" : rest;
            }

            var drive = s_DrivePath.Match(windowsPath);
            if (drive.Success)
            {
                return NormalizeMountRoot(mountRoot)
                       + char.ToLowerInvariant(drive.Groups["drive"].Value[0])
                       + "/"
                       + drive.Groups["rest"].Value.Replace('\\', '/');
            }

            return windowsPath.Replace('\\', '/');
        }

        /// <summary>Converts every absolute Windows path in a ';'-separated MSBuild value. Other entries are untouched.</summary>
        public static string ConvertPathList(string value, string mountRoot, out bool changed)
        {
            changed = false;
            if (string.IsNullOrEmpty(value)) return value;

            string[] parts = value.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string trimmed = parts[i].Trim();
                if (!IsWindowsAbsolutePath(trimmed)) continue;
                parts[i] = ToWslPath(trimmed, mountRoot);
                changed = true;
            }
            return changed ? string.Join(";", parts) : value;
        }

        static string NormalizeMountRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) root = "/mnt/";
            root = root.Trim().Replace('\\', '/');
            if (!root.StartsWith("/")) root = "/" + root;
            if (!root.EndsWith("/")) root += "/";
            return root;
        }

        /// <summary>Path relative to the project directory using forward slashes, or null when outside it.</summary>
        public static string ToProjectRelative(string fullPath, string projectDirectory)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            string p = fullPath.Replace('\\', '/');
            string root = projectDirectory.Replace('\\', '/').TrimEnd('/') + "/";
            if (p.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return p.Substring(root.Length);
            return p.Contains(":") || p.StartsWith("/") ? null : p;
        }

        /// <summary>Escapes characters MSBuild treats specially in item specs (keeps * ? / for globs).</summary>
        public static string EscapeMsBuild(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '%': sb.Append("%25"); break;
                    case '$': sb.Append("%24"); break;
                    case '@': sb.Append("%40"); break;
                    case '\'': sb.Append("%27"); break;
                    case ';': sb.Append("%3B"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Quotes a string for a POSIX shell using single quotes.</summary>
        public static string ShellQuote(string value)
        {
            if (value == null) return "''";
            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }
    }
}
