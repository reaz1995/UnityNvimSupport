using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace NvimUnity
{
    /// <summary>
    /// Hooks into project generation (the Visual Studio package calls OnGeneratedCSProject on every
    /// AssetPostprocessor) and adapts the .csproj files for Neovim / Roslyn:
    ///  * converts absolute Windows paths to WSL paths when Neovim runs inside WSL
    ///  * adds per-assembly wildcard Compile items, so .cs files created from nvim/the terminal
    ///    get full LSP support before Unity regenerates the project.
    /// Only active while Neovim is the selected External Script Editor.
    /// </summary>
    public class CsprojPostprocessor : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 1000;

        static string OnGeneratedCSProject(string path, string content)
        {
            if (!NvimCodeEditor.IsCurrent) return content;
            try
            {
                return CsprojRewriter.Rewrite(path, content, CsprojRewriter.Options.FromSettings());
            }
            catch (Exception e)
            {
                NvimSettings.Warn($"Could not post-process {Path.GetFileName(path)}: {e.Message}");
                return content;
            }
        }
    }

    internal static class CsprojRewriter
    {
        internal struct Options
        {
            public bool ConvertToWsl;
            public string MountRoot;
            public bool AddGlobs;

            public static Options FromSettings() => new Options
            {
                ConvertToWsl = NvimCodeEditor.CurrentTarget == NvimTarget.Wsl && NvimSettings.ConvertPathsForWsl,
                MountRoot = NvimSettings.WslMountRoot,
                AddGlobs = NvimSettings.AddCompileGlobs,
            };
        }

        const string Marker = "Added by the Neovim Unity package";

        public static string Rewrite(string csprojPath, string content, Options options)
        {
            if (!options.ConvertToWsl && !options.AddGlobs) return content;

            XDocument doc = XDocument.Parse(content);
            if (doc.Root == null) return content;

            if (options.AddGlobs)
                AddCompileGlobs(doc, Path.GetFileNameWithoutExtension(csprojPath));

            if (options.ConvertToWsl)
                ConvertPaths(doc, options.MountRoot);

            string declaration = doc.Declaration != null ? doc.Declaration + Environment.NewLine : "";
            return declaration + doc.Root;
        }

        // ------------------------------------------------------------------ WSL paths

        static void ConvertPaths(XDocument doc, string mountRoot)
        {
            foreach (XElement element in doc.Root.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration) continue;
                    string converted = PathUtility.ConvertPathList(attribute.Value, mountRoot, out bool changed);
                    if (changed) attribute.Value = converted;
                }

                if (!element.HasElements && !element.IsEmpty)
                {
                    string converted = PathUtility.ConvertPathList(element.Value, mountRoot, out bool changed);
                    if (changed) element.Value = converted;
                }
            }
        }

        // ------------------------------------------------------------------ Compile globs

        static readonly string[] s_IgnoredByUnity = { "**/.*/**", "**/*~/**", "Assets/StreamingAssets/**" };

        static readonly string[] s_FirstPassRoots = { "Assets/Plugins", "Assets/Standard Assets", "Assets/Pro Standard Assets" };

        static void AddCompileGlobs(XDocument doc, string projectName)
        {
            XNamespace ns = doc.Root.Name.Namespace;

            // Already processed (should not happen, generator always starts from scratch).
            if (doc.Root.Nodes().OfType<XComment>().Any(c => c.Value.Contains(Marker))) return;

            List<string> includes;
            var excludes = new List<string>();
            var layout = AssemblyLayout.Get();

            switch (projectName)
            {
                case "Assembly-CSharp":
                    includes = new List<string> { "Assets/**/*.cs" };
                    excludes.Add("Assets/**/Editor/**");
                    excludes.AddRange(s_FirstPassRoots.Select(r => r + "/**"));
                    excludes.AddRange(layout.AllCustomAssemblyDirs.Where(IsUnderAssets).Select(d => d + "/**"));
                    break;

                case "Assembly-CSharp-Editor":
                    includes = new List<string> { "Assets/**/Editor/**/*.cs" };
                    excludes.AddRange(s_FirstPassRoots.Select(r => r + "/**"));
                    excludes.AddRange(layout.AllCustomAssemblyDirs.Where(IsUnderAssets).Select(d => d + "/**"));
                    break;

                case "Assembly-CSharp-firstpass":
                    includes = s_FirstPassRoots.Select(r => r + "/**/*.cs").ToList();
                    excludes.AddRange(s_FirstPassRoots.Select(r => r + "/**/Editor/**"));
                    excludes.AddRange(layout.AllCustomAssemblyDirs.Where(IsUnderAssets).Select(d => d + "/**"));
                    break;

                case "Assembly-CSharp-Editor-firstpass":
                    includes = s_FirstPassRoots.Select(r => r + "/**/Editor/**/*.cs").ToList();
                    excludes.AddRange(layout.AllCustomAssemblyDirs.Where(IsUnderAssets).Select(d => d + "/**"));
                    break;

                default:
                    // Custom assembly (.asmdef), only for folders that physically live in the project
                    // (Assets/ or embedded packages under Packages/). PackageCache content is read-only anyway.
                    if (!layout.AssemblyDirs.TryGetValue(projectName, out List<string> roots)) return;
                    includes = roots.Select(r => r + "/**/*.cs").ToList();
                    excludes.AddRange(layout.AllCustomAssemblyDirs
                        .Where(d => roots.Any(r => IsStrictlyUnder(d, r)) && !roots.Contains(d))
                        .Select(d => d + "/**"));
                    break;
            }

            if (includes.Count == 0) return;

            includes = includes.Where(i => Directory.Exists(Path.Combine(NvimSettings.ProjectDirectory, GlobRoot(i)))).ToList();
            if (includes.Count == 0) return;

            // Folders Unity never compiles: hidden (".git", ".vs"), "Samples~"-style and StreamingAssets.
            excludes.AddRange(s_IgnoredByUnity);
            excludes.Insert(0, "@(Compile)"); // never duplicate the explicit items Unity generated
            var group = new XElement(ns + "ItemGroup");
            foreach (string include in includes)
            {
                group.Add(new XElement(ns + "Compile",
                    new XAttribute("Include", PathUtility.EscapeMsBuild(include)),
                    new XAttribute("Exclude", string.Join(";", excludes.Select(e => e == "@(Compile)" ? e : PathUtility.EscapeMsBuild(e))))));
            }

            // Insert right after the last ItemGroup that contains Compile items (or at the end).
            XElement anchor = doc.Root.Elements(ns + "ItemGroup").LastOrDefault(g => g.Elements(ns + "Compile").Any());
            var comment = new XComment($" {Marker}: picks up .cs files created outside of Unity ");
            if (anchor != null)
                anchor.AddAfterSelf(comment, group);
            else
                doc.Root.Add(comment, group);
        }

        static string GlobRoot(string glob)
        {
            int star = glob.IndexOf('*');
            string root = star >= 0 ? glob.Substring(0, star) : glob;
            return root.TrimEnd('/');
        }

        static bool IsUnderAssets(string dir) => dir.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);

        static bool IsStrictlyUnder(string dir, string root) =>
            dir.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>Which folders belong to which custom assembly (asmdef + asmref folders). Cached briefly per generation pass.</summary>
        sealed class AssemblyLayout
        {
            static AssemblyLayout s_Cached;
            static double s_CachedAt = -1;

            /// <summary>assembly name -> project relative root folders (asmdef folder + asmref folders).</summary>
            public readonly Dictionary<string, List<string>> AssemblyDirs = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            /// <summary>Every folder that contains an .asmdef or .asmref.</summary>
            public readonly HashSet<string> AllCustomAssemblyDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            [Serializable]
            class AsmRef
            {
                public string reference;
            }

            public static AssemblyLayout Get()
            {
                double now = EditorApplication.timeSinceStartup;
                if (s_Cached != null && now - s_CachedAt < 5.0) return s_Cached;
                s_Cached = Build();
                s_CachedAt = now;
                return s_Cached;
            }

            static AssemblyLayout Build()
            {
                var layout = new AssemblyLayout();
                string projectDir = NvimSettings.ProjectDirectory;
                var asmdefPathToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (Assembly assembly in CompilationPipeline.GetAssemblies())
                {
                    string asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
                    if (string.IsNullOrEmpty(asmdef)) continue;
                    asmdef = asmdef.Replace('\\', '/');
                    asmdefPathToName[asmdef] = assembly.name;

                    string dir = Path.GetDirectoryName(asmdef)?.Replace('\\', '/');
                    if (string.IsNullOrEmpty(dir)) continue;
                    layout.AllCustomAssemblyDirs.Add(dir);
                    if (Directory.Exists(Path.Combine(projectDir, dir)))
                        layout.Add(assembly.name, dir);
                }

                foreach (string guid in AssetDatabase.FindAssets("t:AssemblyDefinitionReferenceAsset"))
                {
                    string asmrefPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(asmrefPath)) continue;
                    string dir = Path.GetDirectoryName(asmrefPath)?.Replace('\\', '/');
                    if (string.IsNullOrEmpty(dir)) continue;
                    layout.AllCustomAssemblyDirs.Add(dir);

                    string target = ResolveAsmRefTarget(asmrefPath, asmdefPathToName);
                    if (target != null && Directory.Exists(Path.Combine(projectDir, dir)))
                        layout.Add(target, dir);
                }

                return layout;
            }

            void Add(string assemblyName, string dir)
            {
                if (!AssemblyDirs.TryGetValue(assemblyName, out List<string> list))
                    AssemblyDirs[assemblyName] = list = new List<string>();
                if (!list.Contains(dir)) list.Add(dir);
            }

            static string ResolveAsmRefTarget(string asmrefPath, Dictionary<string, string> asmdefPathToName)
            {
                try
                {
                    string json = File.ReadAllText(Path.Combine(NvimSettings.ProjectDirectory, asmrefPath));
                    string reference = JsonUtility.FromJson<AsmRef>(json)?.reference;
                    if (string.IsNullOrEmpty(reference)) return null;
                    if (!reference.StartsWith("GUID:", StringComparison.OrdinalIgnoreCase)) return reference;

                    string asmdefPath = AssetDatabase.GUIDToAssetPath(reference.Substring(5))?.Replace('\\', '/');
                    return !string.IsNullOrEmpty(asmdefPath) && asmdefPathToName.TryGetValue(asmdefPath, out string name) ? name : null;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
