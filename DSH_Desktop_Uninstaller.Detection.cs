using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Management;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Microsoft.Win32;

partial class DSHDesktopUninstaller
{

#region Install Detection
    static string ResolveDshInstallDir()
    {
        // Prefer a DSH Desktop uninstall entry: this works across versions,
        // install locations, drive letters and both HKLM/HKCU 32/64-bit views.
        string registryDir = FindDshInstallDirFromRegistry();
        if (!string.IsNullOrEmpty(registryDir))
        {
            return registryDir;
        }

        // Fallback: if this uninstaller is copied into the DSH Desktop install
        // folder, use its own directory.
        try
        {
            string currentDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (!string.IsNullOrEmpty(currentDir) && (HasDshExecutable(currentDir) || HasDshSignature(currentDir)))
            {
                return currentDir;
            }
        }
        catch
        {
        }

        // Fallback: scan common per-user / per-machine install roots. This
        // covers future installers that do not write an uninstall key yet.
        string fallback = FindDshInstallDirInKnownLocations();
        if (!string.IsNullOrEmpty(fallback))
        {
            return fallback;
        }

        // No hardcoded fallback: if the install folder cannot be detected,
        // skip deleting it instead of risking the wrong path on another PC.
        return string.Empty;
    }

    static string FindDshInstallDirFromRegistry()
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
                                    knownExeCandidates.Add(dir);
                                }
                                else
                                {
                                    existingCandidates.Add(dir);
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

        if (knownExeCandidates.Count > 0)
        {
            return knownExeCandidates[0];
        }
        if (existingCandidates.Count > 0)
        {
            return existingCandidates[0];
        }
        return string.Empty;
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

    static string FindDshInstallDirInKnownLocations()
    {
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
                        return dir;
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
                return dir;
            }
        }

        return string.Empty;
    }

    static string FindRunningDshInstallDir()
    {
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
                    return dir;
                }
            }
            catch
            {
            }
        }
        return string.Empty;
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
                                if (!string.IsNullOrEmpty(label)) return label;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }
        return string.Empty;
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
            string label;
            if (KnownAppIdLabels.TryGetValue(keyName, out label)) return label;
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
        return NameMatcher.ContainsToken(text, NameMatcher.RelatedTokens);
    }

    static bool IsDshRelatedPath(string path)
    {
        return NameMatcher.ContainsToken(path, NameMatcher.PathTokens);
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
