using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Management;
using Microsoft.Win32;

partial class DSHDesktopUninstaller
{

#region Install Detection
    static string ResolveDshInstallDir()
    {
        List<string> dirs = ResolveDshInstallDirs();
        return dirs.Count > 0 ? dirs[0] : string.Empty;
    }

    static List<string> ResolveDshInstallDirs()
    {
        List<string> dirs = new List<string>();

        // 1) Registry uninstall entries: collect every DSH-related install dir.
        foreach (string dir in FindDshInstallDirsFromRegistry())
        {
            AddInstallDir(dirs, dir);
        }

        // 2) Known variant published install paths.
        foreach (string dir in FindDshInstallDirsInVariantLocations())
        {
            AddInstallDir(dirs, dir);
        }

        // 3) The uninstaller's own directory when it sits inside an install.
        try
        {
            string currentDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (!string.IsNullOrEmpty(currentDir) && (HasDshExecutable(currentDir) || HasDshSignature(currentDir)))
            {
                AddInstallDir(dirs, currentDir);
            }
        }
        catch (Exception)
        {
                Log("  Warning: non-fatal error ignored.");
        }

        // 4) Generic scan of common install roots.
        foreach (string dir in FindDshInstallDirsInKnownLocations())
        {
            AddInstallDir(dirs, dir);
        }

        return dirs;
    }

    static void AddInstallDir(List<string> dirs, string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        string full;
        try { full = Path.GetFullPath(dir).TrimEnd('\\'); }
        catch { return; }
        if (string.IsNullOrEmpty(full)) return;
        foreach (string existing in dirs)
        {
            if (existing.Equals(full, StringComparison.OrdinalIgnoreCase)) return;
        }
        dirs.Add(full);
    }
    static List<string> FindDshInstallDirsInVariantLocations()
    {
        List<string> dirs = new List<string>();
        string[] names = KnownInstallDirNames;
        if (names == null || names.Length == 0) return dirs;

        List<string> roots = new List<string>();
        try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")); } catch { }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)); } catch { }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch { }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch { }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); } catch { }

        foreach (string root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;
                string dir = Path.Combine(root, name);
                if (Directory.Exists(dir) && (HasDshExecutable(dir) || HasDshSignature(dir)))
                {
                    Log("Using variant install path: " + dir);
                    AddInstallDir(dirs, dir);
                }
            }
        }
        return dirs;
    }
    static List<string> FindDshInstallDirsFromRegistry()
    {
        List<string> existingCandidates = new List<string>();
        List<string> knownExeCandidates = new List<string>();

        RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };

        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstallRoot = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstallRoot == null) continue;
                        foreach (string name in uninstallRoot.GetSubKeyNames())
                        {
                            using (RegistryKey sub = uninstallRoot.OpenSubKey(name))
                            {
                                if (sub == null) continue;
                                string displayName = sub.GetValue("DisplayName") as string;
                                string displayIcon = sub.GetValue("DisplayIcon") as string;
                                string uninstallString = sub.GetValue("UninstallString") as string;
                                string quietUninstallString = sub.GetValue("QuietUninstallString") as string;
                                string installLocation = sub.GetValue("InstallLocation") as string;
                                string bundleCachePath = sub.GetValue("BundleCachePath") as string;
                                string publisher = sub.GetValue("Publisher") as string;
                                string urlInfoAbout = sub.GetValue("URLInfoAbout") as string;
                                if (!MatchesKnownAppId(name) && !IsDshUninstallEntry(displayName, displayIcon, uninstallString, quietUninstallString, installLocation, bundleCachePath, publisher, urlInfoAbout))
                                {
                                    continue;
                                }

                                string dir = ResolveInstallDirFromRegistryEntry(displayIcon, uninstallString, quietUninstallString, installLocation, bundleCachePath);
                                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                                if (HasDshExecutable(dir) || HasDshSignature(dir))
                                {
                                    AddInstallDir(knownExeCandidates, dir);
                                }
                                else
                                {
                                    AddInstallDir(existingCandidates, dir);
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                Log("  Warning: non-fatal error ignored.");
                }
            }
        }

        List<string> result = new List<string>();
        foreach (string dir in knownExeCandidates) AddInstallDir(result, dir);
        foreach (string dir in existingCandidates) AddInstallDir(result, dir);
        return result;
    }
    static string ResolveInstallDirFromRegistryEntry(string displayIcon, string uninstallString, string quietUninstallString, string installLocation, string bundleCachePath)
    {
        // Prefer InstallLocation when the installer actually filled it.
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            string dir = NormalizeRegistryPath(installLocation);
            if (Directory.Exists(dir)) return dir;
        }

        // Some Electron/NSIS installers only expose BundleCachePath.
        if (!string.IsNullOrWhiteSpace(bundleCachePath))
        {
            string dir = NormalizeRegistryPath(bundleCachePath);
            if (Directory.Exists(dir)) return dir;
            // BundleCachePath may point at a file inside the app directory.
            string parent = Path.GetDirectoryName(dir);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) return parent;
        }

        string iconDir = ParseDirFromDisplayIcon(displayIcon);
        if (!string.IsNullOrEmpty(iconDir) && Directory.Exists(iconDir)) return iconDir;

        string exePath = ParseExePathFromCommandLine(uninstallString);
        if (!string.IsNullOrEmpty(exePath))
        {
            string dir = Path.GetDirectoryName(exePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        }

        // QuietUninstallString sometimes differs from UninstallString (e.g. /S stub).
        string quietExePath = ParseExePathFromCommandLine(quietUninstallString);
        if (!string.IsNullOrEmpty(quietExePath))
        {
            string dir = Path.GetDirectoryName(quietExePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
        }

        return string.Empty;
    }

    static string NormalizeRegistryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string path = value.Trim().Trim('"').Trim();
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception)
        {
                Log("  Warning: non-fatal error ignored.");
        }
        return path.TrimEnd('\\');
    }

    static string ParseDirFromDisplayIcon(string displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return string.Empty;
        string iconPath = displayIcon.Split(',')[0].Trim().Trim('"');
        if (string.IsNullOrEmpty(iconPath)) return string.Empty;
        string dir = Path.GetDirectoryName(iconPath);
        if (string.IsNullOrEmpty(dir)) return string.Empty;
        return dir;
    }

    static List<string> FindDshInstallDirsInKnownLocations()
    {
        List<string> dirs = new List<string>();
        List<string> roots = new List<string>();
        try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")); } catch { }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch { }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch { }

        foreach (string root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            try
            {
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    if (IsDshRelatedName(name) && (HasDshExecutable(dir) || HasDshSignature(dir)))
                    {
                        AddInstallDir(dirs, dir);
                    }
                }
            }
            catch (Exception)
            {
                Log("  Warning: non-fatal error ignored.");
            }
        }

        // Direct common locations that may not sit under "Programs".
        List<string> directCandidates = new List<string>();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string name in KnownLocalAppDataDirNames)
        {
            directCandidates.Add(Path.Combine(localAppData, name));
            directCandidates.Add(Path.Combine(localAppData, "Programs", name));
            directCandidates.Add(Path.Combine(userProfile, name));
        }
        foreach (string name in KnownRoamingDirNames)
        {
            directCandidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name));
        }
        foreach (string dir in directCandidates)
        {
            if (Directory.Exists(dir) && (HasDshExecutable(dir) || HasDshSignature(dir)))
            {
                AddInstallDir(dirs, dir);
            }
        }

        return dirs;
    }
    static string FindRunningDshInstallDir()
    {
        List<string> dirs = FindRunningDshInstallDirs();
        return dirs.Count > 0 ? dirs[0] : string.Empty;
    }

    static List<string> FindRunningDshInstallDirs()
    {
        List<string> dirs = new List<string>();
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                string path = GetProcessExecutablePath(p);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string fileName = Path.GetFileName(path);

                // Running-process detection always uses the broad all-variant
                // list. Do not use IsKnownExeName here: after a variant profile
                // is applied, KnownExeNames is narrowed to one variant, and the
                // /DetectRunning mode would stop recognizing other running DSH
                // desktops.
                bool isDshExe = NameMatcher.EqualsToken(fileName, VariantCatalog.AllExeNames);

                // The edge-shortcut variant (2633352305) has no exe; it runs
                // launcher.vbs through wscript.exe. Detect it via the command
                // line and map it to %LOCALAPPDATA%\dsh-edge-app.
                if (!isDshExe &&
                    (fileName.Equals("wscript.exe", StringComparison.OrdinalIgnoreCase) ||
                     fileName.Equals("wscript", StringComparison.OrdinalIgnoreCase)))
                {
                    string cmd = GetProcessCommandLine(p.Id);
                    if (!string.IsNullOrEmpty(cmd) &&
                        cmd.IndexOf("dsh-edge-app", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        cmd.IndexOf("launcher.vbs", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string edgeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dsh-edge-app");
                        if (Directory.Exists(edgeDir)) AddInstallDir(dirs, edgeDir);
                    }
                    continue;
                }

                if (!isDshExe)
                {
                    continue;
                }

                try
                {
                    if (path.Equals(Assembly.GetEntryAssembly().Location, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                catch (Exception)
                {
                    Log("  Warning: non-fatal error ignored.");
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && (HasDshExecutable(dir) || HasDshSignature(dir)))
                {
                    AddInstallDir(dirs, dir);
                }
            }
            catch (Exception)
            {
                Log("  Warning: non-fatal error ignored.");
            }
        }
        return dirs;
    }

    static string ResolveVariantLabel()
    {
        string registryLabel = ResolveVariantLabelFromRegistry();
        if (!string.IsNullOrEmpty(registryLabel)) return registryLabel;

        string dir = DshInstallDir;
        if (string.IsNullOrEmpty(dir)) dir = DetectedRunningDshDir;

        string label = ResolveLabelFromPath(dir);
        if (!string.IsNullOrEmpty(label)) return label;

        return "未知";
    }

    static List<string> ResolveAllVariantLabels()
    {
        List<string> labels = new List<string>();
        foreach (string label in ResolveVariantLabelsFromRegistry())
        {
            if (!string.IsNullOrWhiteSpace(label) && !labels.Contains(label, StringComparer.OrdinalIgnoreCase)) labels.Add(label);
        }

        string dir = DshInstallDir;
        if (string.IsNullOrEmpty(dir)) dir = DetectedRunningDshDir;

        string dirLabel = ResolveLabelFromPath(dir);
        if (!string.IsNullOrWhiteSpace(dirLabel) && !labels.Contains(dirLabel, StringComparer.OrdinalIgnoreCase)) labels.Add(dirLabel);

        if (labels.Count == 0) labels.Add("\u672a\u77e5");
        return labels;
    }
    static string ResolveLabelFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        // Official repo token wins, then the data-driven path-hint map in
        // VariantCatalog (single source of truth, no second label copy here).
        string lower = path.ToLowerInvariant();
        if (lower.Contains("deepseek-ai") || lower.Contains("deepseek_ai")) return "官方 deepseek-ai/deepseek-harness";
        if (lower.Contains("dsh-desktop-client")) return string.Empty; // npm plugin, not a desktop repo

        string label = VariantCatalog.FindLabelByPath(path);
        if (!string.IsNullOrEmpty(label)) return label;

        return string.Empty;
    }
    static string ResolveVariantLabelFromRegistry()
    {
        List<string> labels = ResolveVariantLabelsFromRegistry();
        return labels.Count > 0 ? labels[0] : string.Empty;
    }

    static List<string> ResolveVariantLabelsFromRegistry()
    {
        List<string> labels = new List<string>();
        RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey uninstallRoot = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstallRoot == null) continue;
                        foreach (string name in uninstallRoot.GetSubKeyNames())
                        {
                            using (RegistryKey sub = uninstallRoot.OpenSubKey(name))
                            {
                                if (sub == null) continue;

                                string displayName = sub.GetValue("DisplayName") as string;
                                string displayIcon = sub.GetValue("DisplayIcon") as string;
                                string uninstallString = sub.GetValue("UninstallString") as string;
                                string quietUninstallString = sub.GetValue("QuietUninstallString") as string;
                                string installLocation = sub.GetValue("InstallLocation") as string;
                                string bundleCachePath = sub.GetValue("BundleCachePath") as string;
                                string publisher = sub.GetValue("Publisher") as string;
                                string urlInfoAbout = sub.GetValue("URLInfoAbout") as string;
                                if (!IsDshUninstallEntry(displayName, displayIcon, uninstallString, quietUninstallString, installLocation, bundleCachePath, publisher, urlInfoAbout)) continue;

                                string pathForHeuristic = (installLocation + "|" + displayIcon + "|" + uninstallString + "|" + quietUninstallString + "|" + bundleCachePath);
                                string label = ResolveVariantLabelFromRegistryEntry(name, displayName, publisher, urlInfoAbout, pathForHeuristic);
                                if (!string.IsNullOrEmpty(label) && !labels.Contains(label, StringComparer.OrdinalIgnoreCase)) labels.Add(label);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                Log("  Warning: non-fatal error ignored.");
                }
            }
        }
        return labels;
    }
    static string ResolveVariantLabelFromRegistryEntry(string keyName, string displayName, string publisher, string urlInfoAbout, string pathForHeuristic)
    {
        if (!string.IsNullOrWhiteSpace(keyName))
        {
            VariantProfile p = VariantCatalog.FindByAppId(keyName);
            if (p != null)
            {
                if (keyName.Equals("com.deepseek.dsh.desktop", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(displayName)
                    && displayName.IndexOf("EAC", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "第三方 zouyuxuan122/Deepseek-Harness-EAC";
                }
                return p.Label;
            }
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            VariantProfile dp = VariantCatalog.FindByDisplayName(displayName);
            if (dp != null) return dp.Label;
        }

        if (!string.IsNullOrWhiteSpace(urlInfoAbout))
        {
            VariantProfile up = VariantCatalog.FindByRepoToken(urlInfoAbout);
            if (up != null) return up.Label;
        }

        if (!string.IsNullOrWhiteSpace(pathForHeuristic))
        {
            string label = ResolveLabelFromPath(pathForHeuristic);
            if (!string.IsNullOrEmpty(label)) return label;
        }

        return string.Empty;
    }

    static bool HasDshExecutable(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        string[] names = KnownExeNames;
        foreach (string name in names)
        {
            if (File.Exists(Path.Combine(dir, name))) return true;
        }
        return false;
    }

    // Some variants keep the app under resources\app or only ship package.json.
    // HasDshSignature accepts those install dirs even when the main exe name is
    // not in KnownExeNames (e.g. Electron apps named differently).
    static bool HasDshSignature(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

        if (File.Exists(Path.Combine(dir, "resources", "app.asar"))) return true;
        if (Directory.Exists(Path.Combine(dir, "resources", "app"))) return true;
        if (Directory.Exists(Path.Combine(dir, "resources", "app.asar.unpacked"))) return true;

        string packageJson = Path.Combine(dir, "package.json");
        if (File.Exists(packageJson))
        {
            try
            {
                string text = File.ReadAllText(packageJson);
                if (text.IndexOf("\"dsh\"", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("\"deepseek\"", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                Log("  Warning: non-fatal error ignored.");
            }
        }

        string appPackage = Path.Combine(dir, "resources", "app", "package.json");
        if (File.Exists(appPackage))
        {
            try
            {
                string text = File.ReadAllText(appPackage);
                if (text.IndexOf("\"dsh\"", StringComparison.OrdinalIgnoreCase) >= 0
                    || text.IndexOf("\"deepseek\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                Log("  Warning: non-fatal error ignored.");
            }
        }

        return false;
    }

    static bool IsKnownExeName(string fileName)
    {
        return NameMatcher.EqualsToken(fileName, KnownExeNames);
    }

    static bool IsKnownProcessName(string processName)
    {
        return NameMatcher.EqualsToken(processName, KnownProcessNames);
    }

    // MainModule.FileName throws for elevated/other-session processes. Fall back

    // WMI CommandLine lookup for script-host processes (wscript.exe running
    // launcher.vbs). Returns string.Empty when unavailable.
    static string GetProcessCommandLine(int pid)
    {
        try
        {
            using (System.Management.ManagementObjectSearcher searcher =
                new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
            {
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    string cmd = obj["CommandLine"] as string;
                    return string.IsNullOrEmpty(cmd) ? string.Empty : cmd;
                }
            }
        }
        catch (Exception)
        {
            Log("  Warning: non-fatal error ignored.");
        }
        return string.Empty;
    }
    // to a WMI Win32_Process query so process detection still works for those.
    static string GetProcessExecutablePath(Process p)
    {
        try
        {
            string path = p.MainModule.FileName;
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch (Exception)
        {
                Log("  Warning: non-fatal error ignored.");
        }

        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + p.Id))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    string path = mo["ExecutablePath"] as string;
                    if (!string.IsNullOrEmpty(path)) return path;
                }
            }
        }
        catch (Exception)
        {
                Log("  Warning: non-fatal error ignored.");
        }

        return string.Empty;
    }

    static bool IsDshUninstallEntry(string displayName, string displayIcon, string uninstallString, string quietUninstallString, string installLocation, string bundleCachePath, string publisher, string urlInfoAbout)
    {
        if (IsDshRelatedName(displayName)) return true;
        if (IsDshRelatedName(publisher)) return true;
        if (IsDshRelatedName(urlInfoAbout)) return true;
        if (IsDshRelatedPath(displayIcon)) return true;
        if (IsDshRelatedPath(uninstallString)) return true;
        if (IsDshRelatedPath(quietUninstallString)) return true;
        if (IsDshRelatedPath(installLocation)) return true;
        if (IsDshRelatedPath(bundleCachePath)) return true;
        if (IsDshRelatedPath(urlInfoAbout)) return true;
        return false;
    }


    static bool IsDshRelatedName(string text)
    {
        if (NameMatcher.ContainsToken(text, NameMatcher.RelatedTokens)) return true;
        return NameMatcher.EqualsToken(text, new string[] { "dsh", ".dsh" });
    }

    static bool IsDshRelatedPath(string path)
    {
        if (NameMatcher.ContainsToken(path, NameMatcher.PathTokens)) return true;
        return NameMatcher.ContainsPathSegment(path, "dsh", ".dsh");
    }

    static string ParseExePathFromCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;

        string cmd = commandLine.Trim();
        if (cmd.StartsWith("\""))
        {
            int end = cmd.IndexOf('"', 1);
            if (end > 0) return cmd.Substring(1, end - 1);
        }
        int space = cmd.IndexOf(' ');
        return space > 0 ? cmd.Substring(0, space) : cmd;
    }

    static string ResolveDshHome()
    {
        try
        {
            string env = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrWhiteSpace(env))
            {
                string candidate = env.Trim().TrimEnd('\\');
                string full = Path.GetFullPath(candidate);
                bool isSafe = !IsUnsafeRootPath(full) &&
                    (Path.GetFileName(full).StartsWith(".dsh", StringComparison.OrdinalIgnoreCase) ||
                     Directory.Exists(Path.Combine(full, ".agent-presets")) ||
                     Directory.Exists(Path.Combine(full, "sessions")) ||
                     Directory.Exists(Path.Combine(full, "skills")));
                if (isSafe) return full;
            }
        }
        catch (Exception)
        {
            Log("  Warning: non-fatal error ignored.");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
    }
#endregion

}
