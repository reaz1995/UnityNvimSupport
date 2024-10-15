using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;

public class UnityBinaryReferencesForNvimFix : AssetPostprocessor
{
    private static string OnGeneratedCSProject(string path, string content)
    {
        bool isNvim = true;

        var document = XDocument.Parse(content);
        if (isNvim)
        {
            List<XElement> hintPaths = document.Root.Descendants().Where(x => x.Name.LocalName == "HintPath").ToList();
            foreach (XElement hintPath in hintPaths)
            {
                string newPath = ConvertWindowsPathToWslPath(hintPath.Value);
                hintPath.Value = newPath;
            }
            List<XElement> noneElements = document.Descendants().Where(x => x.Name.LocalName == "None").ToList();
            foreach (XElement noneElement in noneElements)
            {
                XAttribute attribute = noneElement.Attribute("Include");
                if (attribute == null) break;
                string newPath = ConvertWindowsPathToWslPath(attribute.Value);
                attribute.Value = newPath;
            }
        }
        return document.Declaration + System.Environment.NewLine + document.Root;
    }

    public static string ConvertWindowsPathToWslPath(string windowsPath)
    {
        if (string.IsNullOrWhiteSpace(windowsPath)) return string.Empty;
        if (windowsPath.Substring(0, 3).Contains(":\\"))
        {
            // Convert drive letter (e.g., C:) to /mnt/c
            string driveLetter = windowsPath.Substring(0, 1).ToLower();
            string pathWithoutDrive = windowsPath.Substring(2).Replace('\\', '/');

            return $"/mnt/{driveLetter}{pathWithoutDrive}";
        }
        else
        {
            string pathWithoutDrive = windowsPath.Replace('\\', '/');

            return $"{pathWithoutDrive}";
        }
    }
}

