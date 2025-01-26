using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class VSProjectGenerator
{
    static bool enableLogs = false;
    public static void Log(string msg)
    {
        if (!enableLogs) return;
        Debug.Log(msg);
    }

    [InitializeOnLoadMethod]
    public static void GenerateSLN()
    {
        Assembly vsEditorAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Unity.VisualStudio.Editor");
        Type cliType = vsEditorAssembly?.GetType("Microsoft.Unity.VisualStudio.Editor.Cli", false, false);
        MethodInfo generateSolutionMethod = cliType?.GetMethod("GenerateSolution", BindingFlags.Static | BindingFlags.NonPublic);

        if (generateSolutionMethod != null)
        {
            generateSolutionMethod.Invoke(null, null);
            Log("Successfully invoked GenerateSolution.");
            return;
        }

        Log($"Failed - Targeted Method is null");
    }
}


