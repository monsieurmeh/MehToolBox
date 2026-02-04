using MelonLoader;
using MelonLoader.Utils;
using System;
using System.IO;
using System.Reflection;

namespace MehToolBox.Asset
{
    public static class EmbeddedResourceExtractor
    {
        internal static void Extract(string fileName, string outfileFilePath)
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Your namespace + folder path inside the DLL, adjust if needed
            string resourceName = FindResourceName(assembly, fileName);

            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                MelonLogger.Error($"Embedded resource not found: {resourceName}");
                return;
            }

            using FileStream fileStream = new FileStream(outfileFilePath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fileStream);
        }

        internal static string? FindResourceName(Assembly assembly, string fileName)
        {
            return assembly.GetManifestResourceNames()
                           .FirstOrDefault(r => r.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        }

        internal static void RefreshEmbeddedData(string basePath, string fileName, bool force)
        {
            if (force || !File.Exists(Path.Combine(basePath, fileName)))
            {
                Extract(fileName, Path.Combine(basePath, fileName));
            }
        }
    }
}
