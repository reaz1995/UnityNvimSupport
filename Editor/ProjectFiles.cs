using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NvimUnity
{
    /// <summary>
    /// Generates .sln/.csproj files by driving the project generator of the
    /// com.unity.ide.visualstudio package through reflection, so the files are identical
    /// to what Visual Studio / VS Code would get (plus our post-processing).
    /// </summary>
    internal static class ProjectFiles
    {
        const string VsAssemblyName = "Unity.VisualStudio.Editor";
        const string VsNamespace = "Microsoft.Unity.VisualStudio.Editor.";

        static Assembly s_VsAssembly;
        static object s_GuiGenerator;
        static bool s_GuiGeneratorSdk;

        public static string ProjectDirectory => NvimSettings.ProjectDirectory;

        public static string SolutionPath => Path.Combine(ProjectDirectory, Path.GetFileName(ProjectDirectory) + ".sln");

        public static bool SolutionExists => File.Exists(SolutionPath);

        static Assembly VsAssembly
        {
            get
            {
                if (s_VsAssembly == null)
                    s_VsAssembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == VsAssemblyName);
                return s_VsAssembly;
            }
        }

        public static bool IsGeneratorAvailable => VsAssembly != null;

        static object CreateGenerator(bool sdkStyle)
        {
            Assembly asm = VsAssembly;
            if (asm == null) return null;

            string[] candidates = sdkStyle
                ? new[] { "SdkStyleProjectGeneration", "LegacyStyleProjectGeneration", "ProjectGeneration" }
                : new[] { "LegacyStyleProjectGeneration", "ProjectGeneration" };

            foreach (string name in candidates)
            {
                Type type = asm.GetType(VsNamespace + name, false);
                if (type == null || type.IsAbstract) continue;
                try
                {
                    return Activator.CreateInstance(type, true);
                }
                catch (Exception e)
                {
                    NvimSettings.Log($"Could not create {name}: {e.Message}");
                }
            }
            return null;
        }

        /// <summary>Regenerates the solution and all project files.</summary>
        public static bool Sync()
        {
            if (VsAssembly == null)
            {
                NvimSettings.Warn("The 'Visual Studio Editor' package (com.unity.ide.visualstudio) is required to generate .sln/.csproj files. Install it from the Package Manager.");
                return false;
            }

            object generator = CreateGenerator(NvimSettings.SdkStyleProjects);
            MethodInfo sync = generator?.GetType().GetMethod("Sync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (sync != null)
            {
                try
                {
                    sync.Invoke(generator, null);
                    NvimSettings.Log("Project files regenerated.");
                    return true;
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                    return false;
                }
            }

            // Fallback for package versions without an accessible generator type.
            Type cli = VsAssembly.GetType(VsNamespace + "Cli", false);
            MethodInfo generate = cli?.GetMethod("GenerateSolution", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (generate != null)
            {
                try
                {
                    generate.Invoke(null, null);
                    NvimSettings.Log("Project files regenerated (Cli fallback).");
                    return true;
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                    return false;
                }
            }

            NvimSettings.Warn("Could not find a project generator in com.unity.ide.visualstudio. Please update the package.");
            return false;
        }

        /// <summary>Incremental sync, same logic the Visual Studio package uses for its own editor.</summary>
        public static void SyncIfNeeded(string[] affectedFiles, string[] reimportedFiles)
        {
            if (VsAssembly == null) return;

            if (!SolutionExists)
            {
                Sync();
                return;
            }

            object generator = CreateGenerator(NvimSettings.SdkStyleProjects);
            MethodInfo method = generator?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "SyncIfNeeded" && m.GetParameters().Length == 2);
            if (method == null)
            {
                Sync();
                return;
            }

            try
            {
                method.Invoke(generator, new object[] { affectedFiles, reimportedFiles });
            }
            catch (Exception e)
            {
                NvimSettings.Log("SyncIfNeeded failed, doing a full sync: " + (e.InnerException ?? e).Message);
                Sync();
            }
        }

        /// <summary>
        /// Draws the "Generate .csproj files for" toggles (embedded packages, local packages, ...)
        /// backed by the Visual Studio package's own setting.
        /// </summary>
        public static void DrawGenerationFlagsGUI()
        {
            bool sdk = NvimSettings.SdkStyleProjects;
            if (s_GuiGenerator == null || s_GuiGeneratorSdk != sdk)
            {
                s_GuiGenerator = CreateGenerator(sdk);
                s_GuiGeneratorSdk = sdk;
            }

            object provider = s_GuiGenerator?.GetType()
                .GetProperty("AssemblyNameProvider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(s_GuiGenerator);
            if (provider == null) return;

            Type providerType = provider.GetType();
            PropertyInfo flagProp = providerType.GetProperty("ProjectGenerationFlag", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo toggle = providerType.GetMethod("ToggleProjectGeneration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (flagProp == null || toggle == null || !flagProp.PropertyType.IsEnum) return;

            Type flagType = flagProp.PropertyType;
            long current = Convert.ToInt64(flagProp.GetValue(provider));

            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            foreach (object value in Enum.GetValues(flagType))
            {
                long bit = Convert.ToInt64(value);
                if (bit == 0) continue;
                bool on = (current & bit) == bit;
                string label = ObjectNames.NicifyVariableName(Enum.GetName(flagType, value));
                if (EditorGUILayout.Toggle(label, on) != on)
                    toggle.Invoke(provider, new[] { value });
            }
            EditorGUI.indentLevel--;
        }
    }
}
