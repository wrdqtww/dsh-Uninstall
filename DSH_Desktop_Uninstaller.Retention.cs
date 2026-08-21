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
            string name = ParseTopLevelScalar(File.ReadAllText(presetFile), "name");
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
        }
        catch (Exception)
        {
            Log("  Warning: non-fatal error ignored.");
        }

        return Path.GetFileName(presetDir);
    }

    // Strict top-level YAML scalar parser for flat preset.yml files. It only
    // accepts a key that starts at column 0 (no indentation), an optional
    // comment marker, and a quoted or plain scalar value on the same line.
    static string ParseTopLevelScalar(string text, string key)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (string rawLine in text.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (rawLine.Length == 0 || rawLine[0] == ' ' || rawLine[0] == '\t') continue;
            string line = rawLine.TrimEnd();
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            string k = line.Substring(0, colon).Trim();
            if (!k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            string v = line.Substring(colon + 1).Trim();
            int hash = v.IndexOf('#');
            if (hash >= 0) v = v.Substring(0, hash).TrimEnd();
            if (v.Length >= 2 && ((v[0] == '"' && v[v.Length - 1] == '"') || (v[0] == '\'' && v[v.Length - 1] == '\'')))
            {
                v = v.Substring(1, v.Length - 2);
            }
            return v;
        }
        return null;
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
            System.Web.Script.Serialization.JavaScriptSerializer serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            Dictionary<string, object> pkg = serializer.Deserialize<Dictionary<string, object>>(json);
            if (pkg == null) return;
            string packageName = pkg.ContainsKey("name") ? (pkg["name"] as string) : null;
            string description = pkg.ContainsKey("description") ? (pkg["description"] as string) : null;
            if (string.IsNullOrEmpty(packageName)) return;

            // Authoritative plugin marker first (the plugin spec stores metadata
            // under a top-level "dsh" key), then the package naming conventions.
            bool isDsh = pkg.ContainsKey("dsh")
                         || packageName.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0
                         || packageName.StartsWith("dsh", StringComparison.OrdinalIgnoreCase)
                         || packageName.IndexOf("dsh-", StringComparison.OrdinalIgnoreCase) >= 0
                         || packageName.IndexOf("@dsh", StringComparison.OrdinalIgnoreCase) >= 0
                         || packageName.IndexOf("/dsh", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isDsh) return;

            string display = string.IsNullOrEmpty(description)
                ? packageName
                : packageName + " 鈥?" + description;
            result.Add(new PluginInfo(packageName, display));
        }
        catch (Exception)
        {
            Log("  Warning: non-fatal error ignored.");
        }
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
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PluginInfo plugin in plugins)
        {
            if (keepPluginNames.Count > 0 &&
                !keepPluginNames.Contains(plugin.PackageName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string src = FindPluginSourceDir(webModules, plugin.PackageName);
            if (string.IsNullOrEmpty(src)) continue;

            try
            {
                if (CopyPluginWithDependencies(webModules, destRoot, plugin.PackageName, visited))
                {
                    Log("  Preserved plugin: " + plugin.PackageName);
                    preserved++;
                }
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

    static bool IsDotDshRoot(string dir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            string name = Path.GetFileName(Path.GetFullPath(dir));
            return name.Equals(".dsh", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
                Log("  Warning: non-fatal error ignored.");
            return false;
        }
    }

    static bool IsSafeDshHome(string dir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || IsUnsafeRootPath(dir)) return false;
            string full = Path.GetFullPath(dir);
            string name = Path.GetFileName(full);
            if (name.Equals(".dsh", StringComparison.OrdinalIgnoreCase)) return true;
            return Directory.Exists(Path.Combine(full, ".agent-presets"))
                || Directory.Exists(Path.Combine(full, "sessions"))
                || Directory.Exists(Path.Combine(full, "skills"));
        }
        catch (Exception)
        {
            Log("  Warning: non-fatal error ignored.");
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
            if (IsDotDshRoot(DshHome))
            {
                // Default user-data root: remove the .dsh directory itself.
                DeleteDirectoryWithRetry(DshHome);
            }
            else
            {
                // Custom DSH_HOME directory: only DSH content was removed above;
                // never delete a custom-named root that may contain other files.
                Log("  Keeping DSH_HOME root itself (custom directory name); only DSH content was removed: " + DshHome);
            }
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

    // Copies one plugin plus its declared dependencies from the same
    // node_modules tree, so the preserved plugin can actually load. Symlinks /
    // junctions are skipped to avoid following a reparse point out of the tree.
    static bool CopyPluginWithDependencies(string webModules, string destRoot, string packageName, HashSet<string> visited)
    {
        if (visited.Contains(packageName)) return false;
        visited.Add(packageName);

        string src = FindPluginSourceDir(webModules, packageName);
        if (string.IsNullOrEmpty(src)) return false;

        string dest = Path.Combine(destRoot, packageName.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(dest))
        {
            Log("  Plugin already exists in runtime: " + packageName);
            return false;
        }

        string destDir = Path.GetDirectoryName(dest);
        if (string.IsNullOrEmpty(destDir)) destDir = destRoot;
        Directory.CreateDirectory(destDir);
        CopyDirectory(src, dest);

        List<string> deps = ReadDependencyKeys(Path.Combine(src, "package.json"));
        foreach (string dep in deps)
        {
            if (visited.Contains(dep)) continue;
            string depSrc = FindPluginSourceDir(webModules, dep);
            if (string.IsNullOrEmpty(depSrc)) continue;
            try
            {
                CopyPluginWithDependencies(webModules, destRoot, dep, visited);
                Log("  Preserved dependency: " + dep + " (for " + packageName + ")");
            }
            catch (Exception ex)
            {
                Log("  Failed to preserve dependency " + dep + " (for " + packageName + "): " + ex.Message);
            }
        }
        return true;
    }

    static List<string> ReadDependencyKeys(string pkgFile)
    {
        List<string> keys = new List<string>();
        if (string.IsNullOrEmpty(pkgFile) || !File.Exists(pkgFile)) return keys;
        try
        {
            string json = File.ReadAllText(pkgFile);
            System.Web.Script.Serialization.JavaScriptSerializer serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            Dictionary<string, object> pkg = serializer.Deserialize<Dictionary<string, object>>(json);
            if (pkg == null) return keys;
            foreach (string section in new string[] { "dependencies", "peerDependencies", "optionalDependencies" })
            {
                object sectionObj;
                if (!pkg.TryGetValue(section, out sectionObj)) continue;
                Dictionary<string, object> deps = sectionObj as Dictionary<string, object>;
                if (deps == null) continue;
                foreach (string key in deps.Keys)
                {
                    if (!keys.Contains(key)) keys.Add(key);
                }
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to parse dependency keys from " + pkgFile + ": " + ex.Message);
        }
        return keys;
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
            try
            {
                FileAttributes attr = File.GetAttributes(sub);
                if ((attr & FileAttributes.ReparsePoint) != 0)
                {
                    Log("  Skipping reparse point/junction during copy: " + sub);
                    continue;
                }
            }
            catch (Exception)
            {
                Log("  Warning: non-fatal error ignored.");
            }
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
                // Delete every "dsh-" prefixed temp directory: the prefix is
                // reserved for this application family (spill, subprocess,
                // tauri pages, uninstaller temp copies, etc.).
                bool nameMatch = name.StartsWith("dsh-", StringComparison.OrdinalIgnoreCase);


                if (!nameMatch)
                {
                    Log("  Skipping non-DSH temp: " + d + " (nameMatch=False)");
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
