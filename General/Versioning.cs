using MelonLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MehToolBox.General;

public class Versioning
{
    public static bool IsVersionCorrect(string basePath, string currentVersion)
    {
        bool IsVersionCorrect = false;
        string detectedVersion;
        if (!File.Exists(Path.Combine(basePath, "VersionInfo.txt")))
        {
            MelonLogger.Msg($"Could not detect version info file, making with current version {currentVersion}");
            IsVersionCorrect = true;
            File.WriteAllText(Path.Combine(basePath, "VersionInfo.txt"), currentVersion, System.Text.Encoding.UTF8);
        }
        detectedVersion = File.ReadAllText(Path.Combine(basePath, "VersionInfo.txt"), System.Text.Encoding.UTF8);
        if (detectedVersion != currentVersion)
        {
            MelonLogger.Msg($"Detected version {detectedVersion} is not {currentVersion}");
            IsVersionCorrect = true;
            File.WriteAllText(Path.Combine(basePath, "VersionInfo.txt"), currentVersion, System.Text.Encoding.UTF8);
        }
        return IsVersionCorrect;
    }
}
