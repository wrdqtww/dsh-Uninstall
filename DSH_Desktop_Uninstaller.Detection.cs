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
        catch
        {
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
                catch
                {
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
        catch
        {
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
            catch
            {
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
                if (string.IsNullOrEmpty(path)) continue;

                string fileName = Path.GetFileName(path);
                if (!IsKnownExeName(fileName))
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
                catch
                {
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && (HasDshExecutable(dir) || HasDshSignature(dir)))
                {
                    AddInstallDir(dirs, dir);
                }
            }
            catch
            {
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

        string lower = path.ToLowerInvariant();
        if (lower.Contains("deepseek-ai") || lower.Contains("deepseek_ai")) return "官方 deepseek-ai/deepseek-harness";
        if (lower.Contains("dsh-edge-app")) return "第三方 2633352305/DeepSeekHarness-Desktop";
        if (lower.Contains("dsh-integration")) return "第三方 lai-133/dsh-integration";
        if (lower.Contains("ackow")) return "第三方 Ackow/dsh-desktop";
        if (lower.Contains("lburny")) return "第三方 LBurny/deepseek-harness-desktop";
        if (lower.Contains("amazingboycrazy")) return "第三方 AmazingBoyCrazy/dsh_desktop";
        if (lower.Contains("easyhoov") || lower.Contains("deepseek-harness-desktop-windows")) return "第三方 Easyhoov/deepseek-harness-desktop-windows";
        if (lower.Contains("deepseek-harness-eac") || lower.Contains("deepseek harness eac")) return "第三方 zouyuxuan122/Deepseek-Harness-EAC";
        if (lower.Contains("steven-kid")) return "第三方 steven-kid/deepseek-harness-desktop";
        if (lower.Contains("deepseek harness desktop")) return "第三方 Easyhoov/deepseek-harness-desktop-windows";
        if (lower.Contains("dsh-desktop-hub") || lower.Contains("dsh desktop hub")) return "第三方 FlashingChen/dsh-desktop-hub";
        if (lower.Contains("dsh-desktop-client")) return string.Empty; // npm plugin, not a desktop repo
        if (lower.Contains("dsh-cockpit") || lower.Contains("dsh cockpit")) return "第三方 Lxiayu/DshCockpit";
        if (lower.Contains("dsh-studio")) return "第三方 gxcsoccer/dsh-studio";
        if (lower.Contains("dsh-electron-shell")) return "第三方 citrusli2026/dsh-electron-shell";
        if (lower.Contains("dsh-web") || lower.Contains("dsh web")) return "第三方 ding7015869-alt/dsh-web-desktop";
        if (lower.Contains("dsh-client")) return "第三方 hastings0714/dsh-client";
        if (lower.Contains("deepseek-harness")) return "第三方 steven-kid/deepseek-harness-desktop";
        if (lower.Contains("dsh desktop") || lower.Contains("dsh-desktop")) return "第三方 myYangyunfan/dsh_desktop";
        if (lower.Contains("dsh-desk") || lower.Contains("dsh desk")) return "第三方 majiayu000/dsh-desk";

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
                catch
                {
                }
            }
        }
        return labels;
    }
    static string ResolveVariantLabelFromRegistryEntry(string keyName, string displayName, string publisher, string urlInfoAbout, string pathForHeuristic)
    {
        // 1) Display names that are unique to one repo win first (several repos
        //    share appIds or contain each other's substrings).
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            string dn = displayName.Trim();
            if (dn.IndexOf("EAC", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 zouyuxuan122/Deepseek-Harness-EAC";
            if (dn.IndexOf("DSH Desktop Hub", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 FlashingChen/dsh-desktop-hub";
            if (dn.IndexOf("DshCockpit", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 Lxiayu/DshCockpit";
            if (dn.IndexOf("DSH-Web", StringComparison.OrdinalIgnoreCase) >= 0 || dn.IndexOf("dsh-web", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 ding7015869-alt/dsh-web-desktop";
            if (dn.IndexOf("DSHDesktop", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 Ackow/dsh-desktop";
            if (dn.IndexOf("DeepSeek Harness Desktop", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 Easyhoov/deepseek-harness-desktop-windows";
            if (dn.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 steven-kid/deepseek-harness-desktop";
            if (dn.IndexOf("dsh-desktop", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 myYangyunfan/dsh_desktop";
            if (dn.IndexOf("DSH Desktop", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 myYangyunfan/dsh_desktop";
            if (dn.IndexOf("DSH Desk", StringComparison.OrdinalIgnoreCase) >= 0) return "第三方 majiayu000/dsh-desk";
        }

        // 2) Exact uninstall-key appId is authoritative for every repo except
        //    com.deepseek.dsh.desktop, which both official DSH Desktop and the
        //    EAC variant use; EAC was already handled above by display name.
        if (!string.IsNullOrWhiteSpace(keyName))
        {
            VariantProfile p = VariantCatalog.FindByAppId(keyName);
            if (p != null) return p.Label;
        }

        // 3) Publisher / URL hints.
        if (!string.IsNullOrWhiteSpace(urlInfoAbout))
        {
            string lowerUrl = urlInfoAbout.ToLowerInvariant();
            if (lowerUrl.Contains("easyhoov")) return "第三方 Easyhoov/deepseek-harness-desktop-windows";
            if (lowerUrl.Contains("steven-kid")) return "第三方 steven-kid/deepseek-harness-desktop";
            if (lowerUrl.Contains("amazingboycrazy")) return "第三方 AmazingBoyCrazy/dsh_desktop";
            if (lowerUrl.Contains("lburny")) return "第三方 LBurny/deepseek-harness-desktop";
            if (lowerUrl.Contains("ackow")) return "第三方 Ackow/dsh-desktop";
            if (lowerUrl.Contains("citrusli2026")) return "第三方 citrusli2026/dsh-electron-shell";
            if (lowerUrl.Contains("flashingchen")) return "第三方 FlashingChen/dsh-desktop-hub";
            if (lowerUrl.Contains("majiayu000")) return "第三方 majiayu000/dsh-desk";
            if (lowerUrl.Contains("ding7015869")) return "第三方 ding7015869-alt/dsh-web-desktop";
            if (lowerUrl.Contains("lxiayu")) return "第三方 Lxiayu/DshCockpit";
            if (lowerUrl.Contains("zouyuxuan122")) return "第三方 zouyuxuan122/Deepseek-Harness-EAC";
        }

        // 4) Path heuristics.
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
            catch
            {
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
            catch
            {
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
    // to a WMI Win32_Process query so process detection still works for those.
    static string GetProcessExecutablePath(Process p)
    {
        try
        {
            string path = p.MainModule.FileName;
            if (!string.IsNullOrEmpty(path)) return path;
        }
        catch
        {
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
        catch
        {
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
            // DSH_HOME may point to a custom user-data location.
            string env = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrWhiteSpace(env))
            {
                string candidate = env.Trim().TrimEnd('\\');
                string full = Path.GetFullPath(candidate);
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string root = Path.GetPathRoot(full);

                bool isSafe =
                    !string.IsNullOrEmpty(full) &&
                    !full.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                    !full.Equals(userProfile, StringComparison.OrdinalIgnoreCase) &&
                    !full.Equals(windowsDir, StringComparison.OrdinalIgnoreCase) &&
                    (Path.GetFileName(full).StartsWith(".dsh", StringComparison.OrdinalIgnoreCase) ||
                     Directory.Exists(Path.Combine(full, ".agent-presets")) ||
                     Directory.Exists(Path.Combine(full, "sessions")) ||
                     Directory.Exists(Path.Combine(full, "skills")));

                if (isSafe)
                {
                    return full;
                }
            }
        }
        catch
        {
            // Invalid DSH_HOME values must never make uninstallation unsafe.
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
    }
#endregion

}
