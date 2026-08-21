using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

partial class DSHDesktopUninstaller
{

#region Preset/Plugin Detection
    static List<PresetInfo> DetectAgentPresets()
    {
        List<PresetInfo> result = new List<PresetInfo>();
        try
        {
            string presetRoot = Path.Combine(DshHome, ".agent-presets");
            if (!Directory.Exists(presetRoot))
            {
                return result;
            }

            foreach (string dir in Directory.GetDirectories(presetRoot))
            {
                string folderName = Path.GetFileName(dir);
                string displayName = GetPresetDisplayName(dir);
                result.Add(new PresetInfo(folderName, displayName));
            }
        }
        catch (Exception ex)
        {
            Log("  DetectAgentPresets failed: " + ex.Message);
        }
        return result;
    }

    static string GetPresetDisplayName(string presetDir)
    {
        string presetFile = Path.Combine(presetDir, "preset.yml");
        if (!File.Exists(presetFile))
        {
            return Path.GetFileName(presetDir);
        }

        try
        {
            foreach (string rawLine in File.ReadAllLines(presetFile))
            {
                string line = rawLine.Trim();
                if (line.Length > 5 && line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                {
                    string name = line.Substring(5).Trim();
                    if (name.Length >= 2 &&
                        ((name[0] == '"' && name[name.Length - 1] == '"') ||
                         (name[0] == '\'' && name[name.Length - 1] == '\'')))
                    {
                        name = name.Substring(1, name.Length - 2);
                    }
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name.Trim();
                    }
                }
            }
        }
        catch
        {
        }

        return Path.GetFileName(presetDir);
    }

    static List<PluginInfo> DetectPlugins()
    {
        List<PluginInfo> result = new List<PluginInfo>();
        try
        {
            string webModules = Path.Combine(DshHome, @"profiles\web\node_modules");
            if (!Directory.Exists(webModules))
            {
                return result;
            }

            foreach (string dir in Directory.GetDirectories(webModules))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith("@")) continue;
                AddPluginIfDsh(dir, result);
            }

            foreach (string scopeDir in Directory.GetDirectories(webModules))
            {
                string scope = Path.GetFileName(scopeDir);
                if (!scope.StartsWith("@")) continue;
                foreach (string pkgDir in Directory.GetDirectories(scopeDir))
                {
                    AddPluginIfDsh(pkgDir, result);
                }
            }

            result.Sort((a, b) => string.Compare(a.PackageName, b.PackageName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log("  DetectPlugins failed: " + ex.Message);
        }
        return result;
    }

    static void AddPluginIfDsh(string pkgDir, List<PluginInfo> result)
    {
        string pkgFile = Path.Combine(pkgDir, "package.json");
        if (!File.Exists(pkgFile)) return;

        try
        {
            string json = File.ReadAllText(pkgFile);
            if (json.IndexOf("\"dsh\"", StringComparison.OrdinalIgnoreCase) < 0) return;
            string packageName = ExtractJsonString(json, "name");
            string description = ExtractJsonString(json, "description");
            if (string.IsNullOrEmpty(packageName)) return;

            string display = string.IsNullOrEmpty(description)
                ? packageName
                : packageName + " — " + description;
            result.Add(new PluginInfo(packageName, display));
        }
        catch
        {
        }
    }

    static string ExtractJsonString(string json, string key)
    {
        int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
        if (keyIdx < 0) return null;
        int colon = json.IndexOf(':', keyIdx);
        if (colon < 0) return null;
        int start = colon + 1;
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
        if (start >= json.Length || json[start] != '"') return null;
        start++;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = start; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '"') break;
            if (c == '\\' && i + 1 < json.Length)
            {
                i++;
                char esc = json[i];
                if (esc == 'n') sb.Append('\n');
                else if (esc == 't') sb.Append('\t');
                else if (esc == 'r') sb.Append('\r');
                else sb.Append(esc);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    static List<SkillInfo> DetectSkills()
    {
        List<SkillInfo> result = new List<SkillInfo>();
        try
        {
            string skillsRoot = Path.Combine(DshHome, "skills");
            if (!Directory.Exists(skillsRoot))
            {
                return result;
            }

            // Skills are stored under .dsh\skills as either a subfolder (containing
            // SKILL.md etc.) or a plain .md file directly under the skills root.
            foreach (string dir in Directory.GetDirectories(skillsRoot))
            {
                string name = Path.GetFileName(dir);
                result.Add(new SkillInfo(name, name));
            }

            foreach (string file in Directory.GetFiles(skillsRoot))
            {
                string ext = Path.GetExtension(file);
                if (!string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                result.Add(new SkillInfo(name, name));
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log("  DetectSkills failed: " + ex.Message);
        }
        return result;
    }


    static string FindPluginSourceDir(string webModules, string packageName)
    {
        string relative = packageName.Replace('/', Path.DirectorySeparatorChar);
        string candidate = Path.Combine(webModules, relative);
        return Directory.Exists(candidate) ? candidate : string.Empty;
    }
#endregion

#region User Data Retention & Cleanup
    static void PreserveSelectedPlugins()
    {
        if (!keepPlugins || !keepRuntime) return;
        Log("  Preserving selected DSH plugins...");

        string webModules = Path.Combine(DshHome, @"profiles\web\node_modules");
        string destRoot = Path.Combine(DshRuntime, @"dsh\node_modules");
        if (!Directory.Exists(webModules))
        {
            Log("  WARNING: plugin source not found (" + webModules + "); selected plugins were not preserved.");
            return;
        }
        if (!Directory.Exists(destRoot))
        {
            // The runtime may have been removed already; try to recreate the
            // destination so plugins can still be preserved.
            Log("  Runtime node_modules missing (" + destRoot + "); attempting to recreate.");
            try
            {
                Directory.CreateDirectory(destRoot);
            }
            catch (Exception ex)
            {
                Log("  WARNING: could not create plugin destination (" + destRoot + "): " + ex.Message);
                return;
            }
        }

        List<PluginInfo> plugins = DetectPlugins();
        int preserved = 0;
        foreach (PluginInfo plugin in plugins)
        {
            if (keepPluginNames.Count > 0 &&
                !keepPluginNames.Contains(plugin.PackageName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string src = FindPluginSourceDir(webModules, plugin.PackageName);
            if (string.IsNullOrEmpty(src)) continue;

            string dest = Path.Combine(destRoot, plugin.PackageName.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(dest))
            {
                Log("  Plugin already exists in runtime: " + plugin.PackageName);
                continue;
            }

            try
            {
                string destDir = Path.GetDirectoryName(dest);
                if (string.IsNullOrEmpty(destDir)) destDir = destRoot;
                Directory.CreateDirectory(destDir);
                CopyDirectory(src, dest);
                Log("  Preserved plugin: " + plugin.PackageName);
                preserved++;
            }
            catch (Exception ex)
            {
                Log("  Failed to preserve plugin " + plugin.PackageName + ": " + ex.Message);
            }
        }

        if (preserved == 0)
        {
            Log("  No plugin needed copying.");
        }
    }
    static bool IsSettingsFile(string path)
    {
        string name = Path.GetFileName(path);
        return !string.IsNullOrEmpty(name) && name.StartsWith("settings.yaml", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsCredentialsFile(string path)
    {
        string name = Path.GetFileName(path);
        return !string.IsNullOrEmpty(name) && name.StartsWith(".credentials.yaml", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsSafeDshHome(string dir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            string full = Path.GetFullPath(dir);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (full.Equals(userProfile, StringComparison.OrdinalIgnoreCase)
                || full.Equals(windowsDir, StringComparison.OrdinalIgnoreCase)
                || full.Equals(programFiles, StringComparison.OrdinalIgnoreCase)
                || full.Equals(programFilesX86, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string name = Path.GetFileName(full);
            if (name.Equals(".dsh", StringComparison.OrdinalIgnoreCase)) return true;
            return Directory.Exists(Path.Combine(full, ".agent-presets"))
                || Directory.Exists(Path.Combine(full, "sessions"))
                || Directory.Exists(Path.Combine(full, "skills"));
        }
        catch
        {
            return false;
        }
    }

    static void CleanDshHome()
    {
        Log("  Cleaning DSH user data...");

        if (!Directory.Exists(DshHome))
        {
            Log("  DSH user data directory does not exist: " + DshHome);
            return;
        }

        if (!IsSafeDshHome(DshHome))
        {
            Log("  ERROR: DSH user data path failed safety check; refusing to clean: " + DshHome);
            failureCount++;
            return;
        }

        string presetRoot = Path.Combine(DshHome, ".agent-presets");
        string sessionsDir = Path.Combine(DshHome, "sessions");
        string skillsDir = Path.Combine(DshHome, "skills");
        bool keepPresets = keepAgentPresets && Directory.Exists(presetRoot);
        bool keepChat = keepChatData && Directory.Exists(sessionsDir);
        bool keepSkillsData = keepSkills && Directory.Exists(skillsDir);
        bool keepOther = keepOtherUserData;

        if (keepPresets)
        {
            if (keepPresetNames.Count == 0)
            {
                Log("  Keeping all agent presets: " + presetRoot);
            }
            else
            {
                Log("  Keeping selected agent presets: " + string.Join(", ", keepPresetNames.ToArray()));
                KeepSelectedPresets(presetRoot, keepPresetNames);
            }
        }
        if (keepChat)
        {
            Log("  Keeping chat data (sessions): " + sessionsDir);
        }
        if (keepSkillsData)
        {
            if (keepSkillNames.Count == 0)
            {
                Log("  Keeping all skills: " + skillsDir);
            }
            else
            {
                Log("  Keeping selected skills: " + string.Join(", ", keepSkillNames.ToArray()));
                KeepSelectedSkills(skillsDir, keepSkillNames);
            }
        }
        if (keepAppSettings)
        {
            Log("  Keeping application settings: settings.yaml*");
        }
        if (keepModelConfig)
        {
            Log("  Keeping model configuration/credentials: .credentials.yaml* + settings.yaml* (shared file)");
        }
        if (keepOther)
        {
            Log("  Keeping other .dsh user data (graph-memory/storages/profiles 等): " + DshHome);
        }

        string[] dirs = Directory.GetDirectories(DshHome);
        foreach (string dir in dirs)
        {
            bool isPreset = dir.Equals(presetRoot, StringComparison.OrdinalIgnoreCase);
            bool isSessions = dir.Equals(sessionsDir, StringComparison.OrdinalIgnoreCase);
            bool isSkills = dir.Equals(skillsDir, StringComparison.OrdinalIgnoreCase);

            if ((keepPresets && isPreset) ||
                (keepChat && isSessions) ||
                (keepSkillsData && isSkills))
            {
                continue;
            }

            if (keepOther && !isPreset && !isSessions && !isSkills)
            {
                continue;
            }

            DeleteDirectoryWithRetry(dir);
        }

        string[] files = Directory.GetFiles(DshHome);
        foreach (string file in files)
        {
            if (keepAppSettings && IsSettingsFile(file))
            {
                continue;
            }
            if (keepModelConfig && (IsCredentialsFile(file) || IsSettingsFile(file)))
            {
                continue;
            }
            if (keepOther && !IsSettingsFile(file) && !IsCredentialsFile(file))
            {
                continue;
            }
            DeleteFileIfExists(file);
        }

        if (!keepPresets && !keepChat && !keepOther && !keepAppSettings && !keepModelConfig && !keepSkillsData)
        {
            // Nothing is being retained under .dsh: remove the data root too.
            DeleteDirectoryWithRetry(DshHome);
        }
    }

    static void KeepSelectedPresets(string presetRoot, List<string> names)
    {
        HashSet<string> keep = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (PresetInfo info in DetectAgentPresets())
        {
            if (names.Contains(info.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                keep.Add(info.FolderName);
            }
        }
        foreach (string dir in Directory.GetDirectories(presetRoot))
        {
            string name = Path.GetFileName(dir);
            if (!keep.Contains(name))
            {
                Log("  Removing agent preset: " + name);
                DeleteDirectoryWithRetry(dir);
            }
        }

        foreach (string file in Directory.GetFiles(presetRoot))
        {
            DeleteFileIfExists(file);
        }
    }

    static void KeepSelectedSkills(string skillsRoot, List<string> names)
    {
        HashSet<string> keep = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        foreach (string dir in Directory.GetDirectories(skillsRoot))
        {
            string name = Path.GetFileName(dir);
            if (!keep.Contains(name))
            {
                Log("  Removing skill: " + name);
                DeleteDirectoryWithRetry(dir);
            }
        }

        foreach (string file in Directory.GetFiles(skillsRoot))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!keep.Contains(name))
            {
                Log("  Removing skill: " + name);
                DeleteFileIfExists(file);
            }
        }
    }

    static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (string sub in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
        }
    }

    static void CleanupTemp()
    {
        Log("  Cleaning temp dsh-* directories...");
        string temp = Path.GetTempPath();
        try
        {
            foreach (string d in Directory.GetDirectories(temp, "dsh*"))
            {
                string name = Path.GetFileName(d);
                // Narrow match: only "dsh-" prefixed directories that clearly
                // contain a DSH log file. This avoids deleting a third-party
                // tool's "dsh-backup" or "dsh_xyz" temp folder.
                bool nameMatch = name.StartsWith("dsh-", StringComparison.OrdinalIgnoreCase);

                bool contentMatch = File.Exists(Path.Combine(d, "dsh.log"))
                                    || File.Exists(Path.Combine(d, "dsh-desktop.log"));

                if (!nameMatch || !contentMatch)
                {
                    Log("  Skipping non-DSH temp: " + d + " (nameMatch=" + nameMatch + ", contentMatch=" + contentMatch + ")");
                    continue;
                }

                // Use the same retrying deleter as install/user-data directories
                // so a briefly locked temp folder does not fail the cleanup.
                DeleteDirectoryWithRetry(d);
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to scan temp dsh-* directories: " + ex.Message);
        }
    }
#endregion

}
