using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Management;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using Microsoft.Win32;

class DSHDesktopUninstaller
{
#region Fields, Constants & Paths
    static bool silent = false;
    static bool dryRun = false;
    static bool helpRequested = false;
    static string manualInstallDir = string.Empty;
    static ProgressForm progressForm = null;
    static bool keepAgentPresets = false;
    static bool keepRuntime = false;
    static bool keepAppSettings = false;
    static bool keepModelConfig = false;
    static bool keepOtherUserData = false;
    static bool keepChatData = false;
    static bool keepPlugins = false;
    static bool keepSkills = false;
    static List<string> keepPresetNames = new List<string>();
    static List<string> keepPluginNames = new List<string>();
    static List<string> keepSkillNames = new List<string>();

    // Multi-variant support: official DSH Desktop, collection/integrated
    // builds (DeepSeek Harness Desktop, dsh-desktop), and lite/simple
    // variants (deepseek-harness, dsh-edge-app, DSHDesktop, dshdesktop).
    static readonly string[] AllExeNames = new string[]
    {
        "DSH Desktop.exe",
        "dsh-desktop.exe",
        "DeepSeek Harness Desktop.exe",
        "DeepSeek Harness.exe",
        "deepseek-harness.exe",
        "DSHDesktop.exe",
        "dshdesktop.exe",
        "deepseek-harness-desktop.exe",
        "DSH-Desktop.exe",
        "DeepSeek-runtime-Desktop.exe",
        "dsh-desk.exe",
        "dsh-studio.exe",
        "dsh-desktop-hub.exe",
        "dsh-cockpit.exe",
        "dsh-client.exe",
        "dsh-web-desktop.exe",
        "dsh-electron-shell.exe",
        "Deepseek Harness EAC.exe",
        "DSH Desktop Hub.exe",
        "DSH-Web.exe",
        "DshCockpit.exe",
        "DSH Desk.exe"
    };
    static readonly string[] AllProcessNames = new string[]
    {
        "DSH Desktop",
        "dsh-desktop",
        "DeepSeek Harness Desktop",
        "DeepSeek Harness",
        "deepseek-harness",
        "DSHDesktop",
        "dshdesktop",
        "deepseek-harness-desktop",
        "DSH-Desktop",
        "DeepSeek-runtime-Desktop",
        "dsh-desk",
        "dsh-studio",
        "dsh-desktop-hub",
        "dsh-cockpit",
        "dsh-client",
        "dsh-web-desktop",
        "dsh-electron-shell",
        "Deepseek Harness EAC",
        "DSH Desktop Hub",
        "DSH-Web",
        "DshCockpit",
        "DSH Desk"
    };
    static readonly string[] AllShortcutNames = new string[]
    {
        "DSH Desktop.lnk",
        "dsh-desktop.lnk",
        "DeepSeek Harness Desktop.lnk",
        "DeepSeek Harness.lnk",
        "DSHDesktop.lnk",
        "dshdesktop.lnk",
        "deepseek-harness.lnk",
        "deepseek-harness-desktop.lnk",
        "DSH-Desktop.lnk",
        "DeepSeek-runtime-Desktop.lnk",
        "dsh-desk.lnk",
        "dsh-studio.lnk",
        "dsh-desktop-hub.lnk",
        "dsh-cockpit.lnk",
        "dsh-client.lnk",
        "dsh-web-desktop.lnk",
        "dsh-electron-shell.lnk",
        "Deepseek Harness EAC.lnk",
        "DSH Desktop Hub.lnk",
        "DSH-Web.lnk",
        "DshCockpit.lnk",
        "DSH Desk.lnk"
    };
    static readonly string[] AllUpdaterDirNames = new string[]
    {
        "dsh-desktop-updater",
        "dsh-launcher-updater",
        "dsh-updater"
    };
    static readonly string[] AllRoamingDirNames = new string[]
    {
        "DSH Desktop",
        "dsh-desktop",
        "DeepSeek Harness Desktop",
        "DeepSeek Harness",
        "DSHDesktop",
        "dshdesktop",
        "deepseek-harness",
        "deepseek-harness-desktop",
        "DSH-Desktop",
        "DeepSeek-runtime-Desktop",
        "dsh-desk",
        "dsh-studio",
        "dsh-desktop-hub",
        "dsh-cockpit",
        "dsh-client",
        "dsh-web-desktop",
        "dsh-electron-shell",
        "Deepseek Harness EAC",
        "DSH Desktop Hub",
        "DSH-Web",
        "DshCockpit",
        "DSH Desk"
    };
    static readonly string[] AllLocalAppDataDirNames = new string[]
    {
        "DSH Desktop",
        "dsh-desktop",
        "DeepSeek Harness Desktop",
        "DeepSeek Harness",
        "DSHDesktop",
        "dshdesktop",
        "dsh-edge-app",
        "deepseek-harness",
        "deepseek-harness-desktop",
        "DSH-Desktop",
        "DeepSeek-runtime-Desktop",
        "dsh-desk",
        "dsh-studio",
        "dsh-desktop-hub",
        "dsh-cockpit",
        "dsh-client",
        "dsh-web-desktop",
        "dsh-electron-shell",
        "Deepseek Harness EAC",
        "DSH Desktop Hub",
        "DSH-Web",
        "DshCockpit",
        "DSH Desk"
    };

    // When a specific variant repo is recognized, these override the broad
    // "all variants" lists so cleanup targets that variant's known names only.
    static string[] variantExeNames = null;
    static string[] variantProcessNames = null;
    static string[] variantShortcutNames = null;
    static string[] variantUpdaterDirNames = null;
    static string[] variantRoamingDirNames = null;
    static string[] variantLocalAppDataDirNames = null;

    static string[] KnownExeNames { get { return variantExeNames ?? AllExeNames; } }
    static string[] KnownProcessNames { get { return variantProcessNames ?? AllProcessNames; } }
    static string[] KnownShortcutNames { get { return variantShortcutNames ?? AllShortcutNames; } }
    static string[] KnownUpdaterDirNames { get { return variantUpdaterDirNames ?? AllUpdaterDirNames; } }
    static string[] KnownRoamingDirNames { get { return variantRoamingDirNames ?? AllRoamingDirNames; } }
    static string[] KnownLocalAppDataDirNames { get { return variantLocalAppDataDirNames ?? AllLocalAppDataDirNames; } }


    static readonly string[] KnownAppIds = new string[]
    {
        "com.deepseek.dsh.desktop",
        "io.github.amazingboycrazy.dsh-desktop",
        "com.deepseek.harness.desktop",
        "io.dsh.desktop",
        "io.github.steven-kid.deepseek-harness-desktop",
        "com.dshdesktop.desktop",
        "ai.deepseek.harness.desk",
        "com.dshdesktophub.app",
        "io.github.citrusli2026.dsh-electron-shell",
        "com.dshcockpit.app"
    };

    // Known DSH desktop variants -> GUI label shown at the top of the popup.
    static readonly Dictionary<string, string> KnownAppIdLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "com.deepseek.dsh.desktop", "官方 deepseek-ai/deepseek-harness" },
        { "io.dsh.desktop", "第三方 dataelement/dsh-desktop" },
        { "io.github.amazingboycrazy.dsh-desktop", "第三方 AmazingBoyCrazy/dsh_desktop" },
        { "com.deepseek.harness.desktop", "第三方 Easyhoov/deepseek-harness-desktop-windows" },
        { "io.github.steven-kid.deepseek-harness-desktop", "第三方 steven-kid/deepseek-harness-desktop" },
        { "com.dshdesktop.desktop", "第三方 LBurny/deepseek-harness-desktop" },
        { "ai.deepseek.harness.desk", "第三方 majiayu000/dsh-desk" },
        { "com.dshdesktophub.app", "第三方 FlashingChen/dsh-desktop-hub" },
        { "io.github.citrusli2026.dsh-electron-shell", "第三方 citrusli2026/dsh-electron-shell" },
        { "com.dshcockpit.app", "第三方 Lxiayu/DshCockpit" },
    };

    static string DshInstallDir = SafeResolveDshInstallDir();
    const string LegacyUninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\62276e9d-c5f3-5091-b4ee-c7144d6db450";
    static string MachineEnvKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    static string DshHome = ResolveDshHome();
    static string DshRuntime = ResolveDshRuntime();

    static string ResolveDshRuntime()
    {
        try
        {
            string fullHome = Path.GetFullPath(DshHome);
            string parent = Path.GetDirectoryName(fullHome);

            // DshHome may be a drive root (e.g. "C:\"), where GetDirectoryName
            // returns null; fall back to the user profile so Combine is safe.
            if (string.IsNullOrEmpty(parent) || parent == fullHome)
            {
                parent = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(parent, ".dsh-runtime");
        }
        catch
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh-runtime");
        }
    }
    static bool useDetectedRunningDsh = false;
    static string selfTempDir = string.Empty;
    static string DetectedRunningDshDir = SafeFindRunningDshInstallDir();
    static string DetectedVariantLabel = SafeResolveVariantLabel();
    static bool VariantProfileApplied = ApplyVariantProfile();

    static string LogFilePath = ResolveLogFilePath();

    // Always write Log.log next to the running uninstaller, never to a
    // fixed C-drive location. The current directory may be read-only in
    // some edge cases; Log() swallows the failure so cleanup still runs.
    static string ResolveLogFilePath()
    {
        try
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "Log.log");
        }
        catch
        {
            return "Log.log";
        }
    }

    // When a known repository/variant is recognized, narrow the cleanup target
    // lists to that variant's published names so an unrelated DSH desktop is
    // not removed together with the detected one. Unknown variants keep the
    // broad generic lists.
    static bool ApplyVariantProfile()
    {
        try
        {
            string repo = ExtractRepoFromLabel(DetectedVariantLabel);
            if (string.IsNullOrEmpty(repo))
            {
                return true;
            }

            string[] exe = null, proc = null, shortcuts = null, updaters = null, roaming = null, local = null;

            if (repo.IndexOf("deepseek-ai", StringComparison.OrdinalIgnoreCase) >= 0
                || repo.IndexOf("myyangyunfan", StringComparison.OrdinalIgnoreCase) >= 0
                || repo.IndexOf("dataelement", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DSH Desktop.exe", "dsh-desktop.exe" };
                proc = new string[] { "DSH Desktop", "dsh-desktop" };
                shortcuts = new string[] { "DSH Desktop.lnk", "dsh-desktop.lnk" };
                updaters = new string[] { "dsh-desktop-updater", "dsh-launcher-updater" };
                roaming = new string[] { "dsh-desktop", "DSH Desktop" };
                local = new string[] { "DSH Desktop", "dsh-desktop" };
            }
            else if (repo.IndexOf("zouyuxuan122", StringComparison.OrdinalIgnoreCase) >= 0
                     || repo.IndexOf("deepseek-harness-eac", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "Deepseek Harness EAC.exe" };
                proc = new string[] { "Deepseek Harness EAC" };
                shortcuts = new string[] { "Deepseek Harness EAC.lnk" };
                updaters = new string[] { "dsh-desktop-updater", "dsh-launcher-updater" };
                roaming = new string[] { "Deepseek Harness EAC" };
                local = new string[] { "Deepseek Harness EAC" };
            }
            else if (repo.IndexOf("amazingboycrazy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DeepSeek Harness Desktop.exe" };
                proc = new string[] { "DeepSeek Harness Desktop" };
                shortcuts = new string[] { "DeepSeek Harness Desktop.lnk" };
                updaters = new string[] { "dsh-desktop-updater", "dsh-launcher-updater", "dsh-updater" };
                roaming = new string[] { "DeepSeek Harness Desktop" };
                local = new string[] { "DeepSeek Harness Desktop" };
            }
            else if (repo.IndexOf("easyhoov", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DeepSeek Harness Desktop.exe" };
                proc = new string[] { "DeepSeek Harness Desktop" };
                shortcuts = new string[] { "DeepSeek Harness Desktop.lnk" };
                updaters = new string[] { "dsh-desktop-updater", "dsh-launcher-updater", "dsh-updater" };
                roaming = new string[] { "DeepSeek Harness Desktop" };
                local = new string[] { "DeepSeek Harness Desktop" };
            }
            else if (repo.IndexOf("steven-kid", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DeepSeek Harness.exe", "deepseek-harness.exe" };
                proc = new string[] { "DeepSeek Harness", "deepseek-harness" };
                shortcuts = new string[] { "DeepSeek Harness.lnk", "deepseek-harness.lnk" };
                updaters = new string[] { "dsh-updater", "dsh-launcher-updater" };
                roaming = new string[] { "DeepSeek Harness", "deepseek-harness" };
                local = new string[] { "DeepSeek Harness", "deepseek-harness" };
            }
            else if (repo.IndexOf("lburny", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DSHDesktop.exe", "dshdesktop.exe" };
                proc = new string[] { "DSHDesktop", "dshdesktop" };
                shortcuts = new string[] { "DSHDesktop.lnk", "dshdesktop.lnk" };
                updaters = new string[] { "dsh-updater" };
                roaming = new string[] { "DSHDesktop", "dshdesktop" };
                local = new string[] { "DSHDesktop", "dshdesktop" };
            }
            else if (repo.IndexOf("ackow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DSHDesktop.exe", "dshdesktop.exe" };
                proc = new string[] { "DSHDesktop", "dshdesktop" };
                shortcuts = new string[] { "DSHDesktop.lnk", "dshdesktop.lnk" };
                updaters = new string[] { };
                roaming = new string[] { "DSHDesktop", "dshdesktop" };
                local = new string[] { "DSHDesktop", "dshdesktop" };
            }
            else if (repo.IndexOf("2633352305", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Edge shortcut variant: no exe of its own, only a wscript launcher.
                exe = new string[] { };
                proc = new string[] { };
                shortcuts = new string[] { "DeepSeek Harness.lnk" };
                updaters = new string[] { };
                roaming = new string[] { };
                local = new string[] { "dsh-edge-app" };
            }
            else if (repo.IndexOf("majiayu000", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DSH Desk.exe" };
                proc = new string[] { "DSH Desk" };
                shortcuts = new string[] { "DSH Desk.lnk" };
                updaters = new string[] { "dsh-updater" };
                roaming = new string[] { "DSH Desk" };
                local = new string[] { "DSH Desk" };
            }
            else if (repo.IndexOf("flashingchen", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DSH Desktop Hub.exe" };
                proc = new string[] { "DSH Desktop Hub" };
                shortcuts = new string[] { "DSH Desktop Hub.lnk" };
                updaters = new string[] { "dsh-updater" };
                roaming = new string[] { "DSH Desktop Hub" };
                local = new string[] { "DSH Desktop Hub" };
            }
            else if (repo.IndexOf("lxiayu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DshCockpit.exe" };
                proc = new string[] { "DshCockpit" };
                shortcuts = new string[] { "DshCockpit.lnk" };
                updaters = new string[] { };
                roaming = new string[] { "DshCockpit" };
                local = new string[] { "DshCockpit" };
            }
            else if (repo.IndexOf("ding7015869", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "DSH-Web.exe" };
                proc = new string[] { "DSH-Web" };
                shortcuts = new string[] { "DSH-Web.lnk" };
                updaters = new string[] { };
                roaming = new string[] { "DSH-Web" };
                local = new string[] { "DSH-Web" };
            }
            else if (repo.IndexOf("citrusli2026", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "dsh-desktop.exe" };
                proc = new string[] { "dsh-desktop" };
                shortcuts = new string[] { "dsh-desktop.lnk" };
                updaters = new string[] { "dsh-updater" };
                roaming = new string[] { "dsh-desktop" };
                local = new string[] { "dsh-desktop" };
            }
            else if (repo.IndexOf("hastings0714", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                exe = new string[] { "dsh-client.exe" };
                proc = new string[] { "dsh-client" };
                shortcuts = new string[] { "dsh-client.lnk" };
                updaters = new string[] { };
                roaming = new string[] { "dsh-client" };
                local = new string[] { "dsh-client" };
            }

            if (exe != null)
            {
                variantExeNames = exe;
                variantProcessNames = proc;
                variantShortcutNames = shortcuts;
                variantUpdaterDirNames = updaters;
                variantRoamingDirNames = roaming;
                variantLocalAppDataDirNames = local;
                Log("Variant profile applied for: " + repo);
            }
        }
        catch
        {
        }
        return true;
    }
    static string ExtractRepoFromLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return string.Empty;
        string value = label.Trim();
        if (value.StartsWith("官方 ", StringComparison.OrdinalIgnoreCase))
        {
            return value.Substring(3).Trim();
        }
        if (value.StartsWith("第三方 ", StringComparison.OrdinalIgnoreCase))
        {
            return value.Substring(4).Trim();
        }
        return value;
    }


    // Safe static-initialization wrappers: these run before Main, so any
    // exception must be contained here instead of crashing the uninstaller.
    static string SafeResolveDshInstallDir()
    {
        try { return ResolveDshInstallDir(); }
        catch { return string.Empty; }
    }

    static string SafeFindRunningDshInstallDir()
    {
        try { return FindRunningDshInstallDir(); }
        catch { return string.Empty; }
    }

    static string SafeResolveVariantLabel()
    {
        try { return ResolveVariantLabel(); }
        catch { return "未知"; }
    }

    // Counts cleanup failures so /S (silent) mode can return a non-zero exit
    // code that scripts can check.
    static int failureCount = 0;


    class PresetInfo
    {
        public string FolderName;
        public string DisplayName;

        public PresetInfo(string folderName, string displayName)
        {
            FolderName = folderName;
            DisplayName = displayName;
        }
    }

    class PluginInfo
    {
        public string PackageName;
        public string DisplayName;

        public PluginInfo(string packageName, string displayName)
        {
            PackageName = packageName;
            DisplayName = displayName;
        }
    }

    class SkillInfo
    {
        public string Name;
        public string DisplayName;

        public SkillInfo(string name, string displayName)
        {
            Name = name;
            DisplayName = displayName;
        }
    }



#endregion

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
        if (lower.Contains("dsh-desktop-client")) return "第三方 Ackow/dshdesktop-client";
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
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        foreach (string name in KnownExeNames)
        {
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static bool IsKnownProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        foreach (string name in KnownProcessNames)
        {
            if (name.Equals(processName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
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
        if (string.IsNullOrWhiteSpace(text)) return false;
        string value = text.Trim();
        return value.IndexOf("DSH Desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("DSHDesktop", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("DSH桌面", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dshdesktop", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-edge-app", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-desk", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh desk", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-studio", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-cockpit", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-client", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-web-desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-web", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("DSH-Web", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("dsh-electron-shell", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("DeepSeek Harness Desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("deepseek-harness", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsDshRelatedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return path.IndexOf("DSH Desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("DSHDesktop", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dshdesktop", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-edge-app", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-desk", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh desk", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-studio", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-cockpit", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-client", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-web-desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-web", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("DSH-Web", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("dsh-electron-shell", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("DeepSeek Harness Desktop", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0
            || path.IndexOf("deepseek-harness", StringComparison.OrdinalIgnoreCase) >= 0;
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

#region Entry Point & CLI Parsing
    static string BuildDeletionTargetsSummary()
    {
        List<string> targets = new List<string>();
        if (!string.IsNullOrEmpty(DshInstallDir))
        {
            targets.Add("安装目录：" + DshInstallDir);
        }
        foreach (string dir in GetKnownExtraDirectories())
        {
            if (string.IsNullOrEmpty(dir)) continue;
            if (!string.IsNullOrEmpty(DshInstallDir) && dir.Equals(DshInstallDir, StringComparison.OrdinalIgnoreCase)) continue;
            targets.Add("已知附加目录：" + dir);
        }
        if (Directory.Exists(DshHome))
        {
            targets.Add("用户数据：" + DshHome);
        }
        if (!keepRuntime && Directory.Exists(DshRuntime))
        {
            targets.Add("运行时：" + DshRuntime);
        }
        targets.Add("注册表卸载项 / PATH 条目 / 快捷方式 / Run 启动项 / dsh-* 临时目录");
        return string.Join("\r\n", targets.ToArray());
    }

    static bool ConfirmAndSelectRetention()
    {
        if (silent) return true;

        Application.EnableVisualStyles();
        using (RetentionForm form = new RetentionForm())
        {
            form.SetRetentionOptions(keepAgentPresets, keepRuntime, keepChatData, keepAppSettings, keepModelConfig, keepOtherUserData, keepPlugins, keepSkills, keepPresetNames, keepPluginNames, keepSkillNames);

            if (form.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            keepAgentPresets = form.KeepAgentPresets;
            keepRuntime = form.KeepRuntime;
            keepChatData = form.KeepChatData;
            keepAppSettings = form.KeepAppSettings;
            keepModelConfig = form.KeepModelConfig;
            keepOtherUserData = form.KeepOtherUserData;
            keepPlugins = form.KeepPlugins;
            keepSkills = form.KeepSkills;
            keepPresetNames = form.KeepPresetNames;
            keepPluginNames = form.KeepPluginNames;
            keepSkillNames = form.KeepSkillNames;
            useDetectedRunningDsh = form.UseDetectedRunningDsh;

            // Second confirmation: show exactly what will be retained and what
            // will be deleted before starting.
            string summary = RetentionSummary();
            string message = summary == "(none)"
                ? "确定卸载 DSH / DeepSeek Harness 桌面端并删除所有用户数据吗？"
                : "确定卸载 DSH / DeepSeek Harness 桌面端并保留以下内容吗？\r\n\r\n保留：\r\n" + summary;
            message += "\r\n\r\n将删除：\r\n" + BuildDeletionTargetsSummary();
            if (string.IsNullOrEmpty(DshInstallDir))
            {
                message += "\r\n\r\n⚠️ 未检测到 DSH 安装目录，将仅清理用户数据与已知额外目录。";
            }
            if (MessageBox.Show(message, "确认卸载", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            {
                return false;
            }

            return true;
        }
    }

    // When the uninstaller relaunches itself from a temp copy, the temp copy
    // cannot delete its own running exe. Schedule a delayed cmd to remove the
    // whole temp folder after the process has exited.
    static void ScheduleSelfTempDeletion()
    {
        if (string.IsNullOrEmpty(selfTempDir)) return;
        try
        {
            string cmd = "/C ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"" + selfTempDir + "\"";
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi);
        }
        catch
        {
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        ParseArgs(args);
        InitializeLog();
        Log("Detected DSH: " + DetectedVariantLabel);
        if (VariantProfileApplied)
        {
            string repo = ExtractRepoFromLabel(DetectedVariantLabel);
            if (!string.IsNullOrEmpty(repo)) Log("Variant profile applied for: " + repo);
        }

        if (helpRequested)
        {
            PrintUsage();
            return 0;
        }

        if (dryRun)
        {
            RunDryRun();
            return 0;
        }

        if (!IsAdministrator())
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Assembly.GetEntryAssembly().Location;
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                psi.Arguments = BuildQuotedArguments(args);
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Administrator rights are required. Right-click and select Run as administrator.");
                Console.WriteLine(ex.Message);
                Pause();
                return 1;
            }
        }

        // If this uninstaller itself is running from the DSH install directory,
        // it cannot be deleted while running. Relaunch a temporary copy so the
        // original process exits and releases the lock before cleanup.
        if (IsRunningFromDshInstallDir())
        {
            try
            {
                string srcExe = Assembly.GetEntryAssembly().Location;
                string tempDir = Path.Combine(Path.GetTempPath(), "dsh-uninstaller-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tempDir);
                selfTempDir = tempDir;
                string tempExe = Path.Combine(tempDir, Path.GetFileName(srcExe));
                File.Copy(srcExe, tempExe, true);
                Log("Uninstaller runs from install dir; relaunching from temp: " + tempExe);
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = tempExe;
                psi.UseShellExecute = false;
                psi.Arguments = BuildQuotedArguments(args);
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    ScheduleSelfTempDeletion();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to relocate uninstaller for self-deletion: " + ex.Message);
            }
        }

        try
        {
            if (!ConfirmAndSelectRetention())
            {
                Log("Uninstall cancelled by user.");
                ScheduleSelfTempDeletion();
                Pause();
                return 0;
            }

            if (!silent)
            {
                progressForm = new ProgressForm();
                progressForm.Show();
            }
            try
            {
                Run();
            }
            finally
            {
                try
                {
                    if (progressForm != null)
                    {
                        progressForm.Close();
                        progressForm.Dispose();
                    }
                }
                catch
                {
                }
                progressForm = null;
            }
            Log("===== Uninstaller exit =====");
            ScheduleSelfTempDeletion();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            Console.WriteLine("Error: " + ex.Message);
            ScheduleSelfTempDeletion();
            Pause();
            return 1;
        }

        ScheduleSelfTempDeletion();
        Pause();
        // In silent mode report cleanup failures via a non-zero exit code so
        // scripts can detect a partial uninstall; interactive mode returns 0.
        return (silent && failureCount > 0) ? 1 : 0;
    }

    static void ParseArgs(string[] args)
    {
        foreach (string raw in args)
        {
            string arg = raw;
            string value = null;
            int eq = arg.IndexOf('=');
            if (eq >= 0)
            {
                value = arg.Substring(eq + 1);
                arg = arg.Substring(0, eq);
            }

            if (arg.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-S", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-silent", StringComparison.OrdinalIgnoreCase))
            {
                silent = true;
            }
            else if (arg.Equals("/KeepPresets", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepPresets", StringComparison.OrdinalIgnoreCase))
            {
                keepAgentPresets = true;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    keepPresetNames = ParsePresetNames(value);
                }
            }
            else if (arg.Equals("/KeepRuntime", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepRuntime", StringComparison.OrdinalIgnoreCase))
            {
                keepRuntime = true;
            }
            else if (arg.Equals("/KeepPlugins", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepPlugins", StringComparison.OrdinalIgnoreCase))
            {
                keepPlugins = true;
                keepRuntime = true;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    keepPluginNames = ParsePresetNames(value);
                }
            }
            else if (arg.Equals("/KeepVision", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepVision", StringComparison.OrdinalIgnoreCase))
            {
                keepPlugins = true;
                keepRuntime = true;
                if (!keepPluginNames.Contains("@dsh-external/dsh-vision", StringComparer.OrdinalIgnoreCase))
                {
                    keepPluginNames.Add("@dsh-external/dsh-vision");
                }
            }
            else if (arg.Equals("/KeepSkills", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepSkills", StringComparison.OrdinalIgnoreCase))
            {
                keepSkills = true;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    keepSkillNames = ParsePresetNames(value);
                }
            }
            else if (arg.Equals("/KeepAppSettings", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepAppSettings", StringComparison.OrdinalIgnoreCase))
            {
                keepAppSettings = true;
            }
            else if (arg.Equals("/KeepModelConfig", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepModelConfig", StringComparison.OrdinalIgnoreCase))
            {
                keepModelConfig = true;
            }
            else if (arg.Equals("/KeepOtherUserData", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepOtherUserData", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("/KeepOtherData", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepOtherData", StringComparison.OrdinalIgnoreCase))
            {
                keepOtherUserData = true;
            }
            else if (arg.Equals("/KeepChatData", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepChatData", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("/KeepChat", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepChat", StringComparison.OrdinalIgnoreCase))
            {
                keepChatData = true;
            }
            else if (arg.Equals("/KeepAll", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-KeepAll", StringComparison.OrdinalIgnoreCase))
            {
                keepAgentPresets = true;
                keepRuntime = true;
                keepPlugins = true;
                keepChatData = true;
                keepAppSettings = true;
                keepModelConfig = true;
                keepOtherUserData = true;
                keepSkills = true;
            }
            else if (arg.Equals("/DetectRunning", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-DetectRunning", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("/DetectDSH", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-DetectDSH", StringComparison.OrdinalIgnoreCase))
            {
                useDetectedRunningDsh = true;
            }
            else if (arg.Equals("/Default", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-Default", StringComparison.OrdinalIgnoreCase))
            {
                useDetectedRunningDsh = false;
            }
            else if (arg.Equals("/InstallDir", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-InstallDir", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("/Dir", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-Dir", StringComparison.OrdinalIgnoreCase))
            {
                manualInstallDir = (value ?? string.Empty).Trim().Trim('"').TrimEnd('\\');
            }
            else if (arg.Equals("/DryRun", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-DryRun", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("/Preview", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-Preview", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
            }
            else if (arg.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-help", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-?", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                helpRequested = true;
            }
        }
    }

    // Rebuild a command line that preserves each argument as a single token,
    // quoting arguments that contain spaces or quotes. Without this, a value
    // like "/KeepPresets=我的预设,预设2" would be split across the UAC re-launch.
    static string BuildQuotedArguments(string[] args)
    {
        if (args == null || args.Length == 0) return string.Empty;
        List<string> quoted = new List<string>();
        foreach (string a in args)
        {
            if (string.IsNullOrEmpty(a))
            {
                quoted.Add("\"\"");
                continue;
            }
            bool needsQuote = a.IndexOf(' ') >= 0 || a.IndexOf('"') >= 0 || a.IndexOf('\t') >= 0;
            if (!needsQuote)
            {
                quoted.Add(a);
                continue;
            }
            quoted.Add("\"" + a.Replace("\"", "\\\"") + "\"");
        }
        return string.Join(" ", quoted.ToArray());
    }

    static void PrintUsage()
    {
        Console.WriteLine("DSH / DeepSeek Harness 桌面端卸载器");
        Console.WriteLine();
        Console.WriteLine("用法: Uninstall_DSH_Desktop.exe [选项]");
        Console.WriteLine();
        Console.WriteLine("  卸载模式:");
        Console.WriteLine("    /S 或 /silent          静默卸载（不显示界面）");
        Console.WriteLine("    /DetectRunning         优先检测正在运行的 DSH 安装目录");
        Console.WriteLine("    /Default               使用默认检测（注册表/常见路径）");
        Console.WriteLine("    /InstallDir=<路径>     手动指定安装目录");
        Console.WriteLine("    /DryRun                只检测并列出将删除/保留的内容，不实际删除");
        Console.WriteLine("    /help 或 /?            显示本帮助");
        Console.WriteLine();
        Console.WriteLine("  保留选项:");
        Console.WriteLine("    /KeepPresets[=名称]    保留预设（不填=全部；多个用逗号分隔）");
        Console.WriteLine("    /KeepSkills[=名称]     保留 skills（不填=全部；多个用逗号分隔）");
        Console.WriteLine("    /KeepChatData          保留聊天数据 (sessions)");
        Console.WriteLine("    /KeepAppSettings       保留应用设置 (settings.yaml)");
        Console.WriteLine("    /KeepModelConfig       保留模型配置与凭据");
        Console.WriteLine("    /KeepOtherUserData     保留其他 .dsh 数据");
        Console.WriteLine("    /KeepPlugins[=名称]    保留插件（不填=全部）");
        Console.WriteLine("    /KeepRuntime           保留 .dsh-runtime");
        Console.WriteLine("    /KeepAll               保留以上全部");
    }

    static void RunDryRun()
    {
        Console.WriteLine("===== DSH Desktop Uninstaller Dry-Run =====");
        Console.WriteLine("安装目录:   " + (string.IsNullOrEmpty(DshInstallDir) ? "(未检测到)" : DshInstallDir));
        Console.WriteLine("当前DSH:    " + DetectedVariantLabel);
        Console.WriteLine("用户数据:   " + DshHome);
        Console.WriteLine("运行时:     " + DshRuntime);
        Console.WriteLine("保留:       " + RetentionSummary());
        Console.WriteLine();
        Console.WriteLine("将删除的主要内容:");
        Console.WriteLine("  - 安装目录: " + (string.IsNullOrEmpty(DshInstallDir) ? "(未检测到，跳过)" : DshInstallDir));
        foreach (string dir in GetKnownExtraDirectories())
        {
            if (!string.IsNullOrEmpty(dir)) Console.WriteLine("  - 额外目录: " + dir);
        }
        Console.WriteLine("  - 快捷方式: 桌面/开始菜单中的 DSH 相关 .lnk");
        Console.WriteLine("  - 注册表:   卸载键 + 通知设置 + PATH 条目 + Run 启动项");
        Console.WriteLine("  - 用户数据: " + DshHome + (keepAgentPresets || keepChatData || keepSkills || keepAppSettings || keepModelConfig || keepOtherUserData ? "（按选项保留）" : "（全部删除）"));
        Console.WriteLine("  - 运行时:   " + (keepRuntime ? "保留" : DshRuntime));
        Console.WriteLine("===== Dry-run end =====");
    }


    static List<string> ParsePresetNames(string value)
    {
        List<string> result = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return result;
        string[] parts = value.Split(new char[] { ',', ';', '，' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string name = part.Trim();
            if (name.Length > 0 && name != "*" && !name.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(name);
            }
        }
        return result;
    }
#endregion

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

#region Uninstall Pipeline
    static bool IsAdministrator()
    {
        WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static bool IsRunningFromDshInstallDir()
    {
        try
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            return !string.IsNullOrEmpty(exeDir) &&
                   !string.IsNullOrEmpty(DshInstallDir) &&
                   exeDir.Equals(DshInstallDir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static bool IsSafeInstallDir(string dir)
    {
        try
        {
            string full = Path.GetFullPath(dir);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string root = Path.GetPathRoot(full);

            return !string.IsNullOrEmpty(full) &&
                   !full.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                   !full.Equals(userProfile, StringComparison.OrdinalIgnoreCase) &&
                   !full.Equals(windowsDir, StringComparison.OrdinalIgnoreCase) &&
                   !full.Equals(programFiles, StringComparison.OrdinalIgnoreCase) &&
                   !full.Equals(programFilesX86, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static void Run()
    {
        if (!string.IsNullOrEmpty(manualInstallDir))
        {
            if (Directory.Exists(manualInstallDir) && IsSafeInstallDir(manualInstallDir) &&
                (HasDshExecutable(manualInstallDir) || HasDshSignature(manualInstallDir)))
            {
                DshInstallDir = manualInstallDir;
                Log("Uninstall mode: manual install dir -> " + manualInstallDir);
            }
            else
            {
                Log("WARNING: manual install dir does not look like a DSH desktop, ignored: " + manualInstallDir);
            }
        }

        if (useDetectedRunningDsh)
        {
            string runningDir = FindRunningDshInstallDir();
            if (!string.IsNullOrEmpty(runningDir))
            {
                DshInstallDir = runningDir;
                Log("Uninstall mode: detect running DSH -> " + runningDir);
            }
            else
            {
                Log("Uninstall mode: detect running DSH requested, but no running DSH found; falling back to default detection.");
            }
        }
        else
        {
            Log("Uninstall mode: default detection.");
        }

        Log("===== DSH Desktop / DeepSeek Harness Complete Uninstaller =====");
        Log("Retention: " + RetentionSummary());
        Log("Install dir: " + (string.IsNullOrEmpty(DshInstallDir) ? "(not detected)" : DshInstallDir));
        if (string.IsNullOrEmpty(DshInstallDir))
        {
            Log("  WARNING: no DSH install directory detected; only user data and known extra directories will be cleaned.");
        }
        Log("");

        KillDSHProcesses();
        DeleteDirectoryWithRetry(DshInstallDir);
        foreach (string dir in GetKnownExtraDirectories())
        {
            if (string.IsNullOrEmpty(dir)) continue;
            if (!string.IsNullOrEmpty(DshInstallDir) && dir.Equals(DshInstallDir, StringComparison.OrdinalIgnoreCase)) continue;
            if (Directory.Exists(dir))
            {
                DeleteDirectoryWithRetry(dir);
            }
        }
        DeleteKnownDshShortcuts();
        DeleteRegistryKeys();
        CleanupMachinePath();
        CleanupUserPath();
        CleanupRunKeys();
        BroadcastEnvironmentChange();
        PreserveSelectedPlugins();
        CleanDshHome();
        if (!keepRuntime)
        {
            DeleteDirectoryWithRetry(DshRuntime);
        }
        CleanupTemp();

        Log("");
        Log("===== Uninstall finished =====");
        Log("Removed DSH Desktop / DeepSeek Harness app, updaters, caches, shortcuts, uninstall registry key and DSH user data.");
        Log("Kept: " + RetentionSummary());
    }


    static IEnumerable<string> GetKnownExtraDirectories()
    {
        List<string> dirs = new List<string>();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Updater directories are DSH-specific names; no generic-name collision
        // risk, so add them without extra verification.
        foreach (string name in KnownUpdaterDirNames)
        {
            dirs.Add(Path.Combine(localAppData, name));
        }
        // Generic per-user/per-machine folders must be verified to actually be
        // DSH installs before they are added, so a user's own same-named folder
        // (e.g. "%USERPROFILE%\DSH Desktop") is never deleted by mistake.
        foreach (string name in KnownLocalAppDataDirNames)
        {
            AddVerifiedDshDir(dirs, Path.Combine(localAppData, name));
            AddVerifiedDshDir(dirs, Path.Combine(localAppData, "Programs", name));
            AddVerifiedDshDir(dirs, Path.Combine(userProfile, name));
        }
        foreach (string name in KnownRoamingDirNames)
        {
            AddVerifiedDshDir(dirs, Path.Combine(appData, name));
        }
        // Lite/edge variants may install the CLI globally via npm (@deepseek-ai/dsh).
        dirs.Add(Path.Combine(appData, "npm", "node_modules", "@deepseek-ai", "dsh"));
        return dirs;
    }

    // Add a candidate directory only if it does not exist or is verified as a
    // DSH/Electron install; skip existing non-DSH directories to avoid deleting
    // a user's unrelated folder that happens to share the same name.
    static void AddVerifiedDshDir(List<string> dirs, string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!Directory.Exists(dir))
        {
            dirs.Add(dir);
            return;
        }
        if (IsLikelyDshDirectory(dir))
        {
            dirs.Add(dir);
        }
        else
        {
            Log("  Skipping non-DSH directory: " + dir);
        }
    }

    static bool IsLikelyDshDirectory(string dir)
    {
        try
        {
            if (HasDshExecutable(dir)) return true;
            if (File.Exists(Path.Combine(dir, "app.asar"))) return true;
            if (Directory.Exists(Path.Combine(dir, "resources", "app"))) return true;
            string pkgFile = Path.Combine(dir, "package.json");
            if (File.Exists(pkgFile))
            {
                string json = File.ReadAllText(pkgFile);
                if (json.IndexOf("dsh", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    json.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    json.IndexOf("electron", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    static void DeleteKnownDshShortcuts()
    {
        string[] roots = new string[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs")
        };
        foreach (string root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            try
            {
                // Some installers place shortcuts in sub-folders (for example
                // "...\Start Menu\Programs\DSH Desktop\DSH Desktop.lnk"), so
                // scan recursively instead of only checking the root.
                foreach (string file in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(file);
                    bool isKnown = false;
                    foreach (string known in KnownShortcutNames)
                    {
                        if (known.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            isKnown = true;
                            break;
                        }
                    }
                    if (isKnown)
                    {
                        DeleteFileIfExists(file);
                    }
                }
            }
            catch
            {
            }
        }
    }

    static string RetentionSummary()
    {
        List<string> kept = new List<string>();
        if (keepAgentPresets)
        {
            if (keepPresetNames.Count > 0)
            {
                kept.Add(".agent-presets (" + string.Join(", ", keepPresetNames.ToArray()) + ")");
            }
            else
            {
                kept.Add(".agent-presets (all)");
            }
        }
        if (keepChatData)
        {
            kept.Add("聊天数据 (sessions)");
        }
        if (keepAppSettings)
        {
            kept.Add("应用设置 (settings.yaml)");
        }
        if (keepModelConfig)
        {
            kept.Add("模型配置 (credentials + settings.yaml 模型部分)");
        }
        if (keepOtherUserData)
        {
            kept.Add("其他 .dsh 数据 (graph-memory/storages/profiles 等)");
        }
        if (keepRuntime)
        {
            kept.Add(".dsh-runtime");
        }
        if (keepPlugins)
        {
            if (keepPluginNames.Count > 0)
            {
                kept.Add("插件 (" + string.Join(", ", keepPluginNames.ToArray()) + ")");
            }
            else
            {
                kept.Add("插件 (all)");
            }
        }
        if (keepSkills)
        {
            if (keepSkillNames.Count > 0)
            {
                kept.Add("skills (" + string.Join(", ", keepSkillNames.ToArray()) + ")");
            }
            else
            {
                kept.Add("skills (all)");
            }
        }
        return kept.Count == 0 ? "(none)" : string.Join(", ", kept.ToArray());
    }

#endregion
#region Process & File Cleanup
    static void KillDSHProcesses()
    {
        Log("[1/9] Stopping DSH Desktop processes...");

        // First pass: try graceful close when a main window exists,
        // otherwise terminate the process directly.
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (IsDshProcess(p))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        p.CloseMainWindow();
                        Log("  Sent close to: " + p.ProcessName + " (PID " + p.Id + ")");
                    }
                    else
                    {
                        p.Kill();
                        Log("  Killed: " + p.ProcessName + " (PID " + p.Id + ")");
                    }
                }
            }
            catch
            {
            }
        }

        // Wait up to 3 seconds for graceful shutdown, re-enumerating each time.
        for (int i = 0; i < 10; i++)
        {
            bool anyAlive = false;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (IsDshProcess(p))
                    {
                        anyAlive = true;
                        break;
                    }
                }
                catch
                {
                }
            }
            if (!anyAlive) break;
            Thread.Sleep(300);
        }

        // Second pass: force-kill any remaining DSH processes.
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (IsDshProcess(p))
                {
                    p.Kill();
                    Log("  Force killed: " + p.ProcessName + " (PID " + p.Id + ")");
                }
            }
            catch
            {
            }
        }

        // Final pass: use taskkill /F /T so Electron child-process trees are
        // removed as a whole (main process alone may leave renderer/gpu children).
        // Try both image-name spellings; some processes report no .exe suffix.
        foreach (string name in KnownProcessNames)
        {
            RunTaskKill("/F /IM \"" + name + ".exe\" /T");
            RunTaskKill("/F /IM \"" + name + "\" /T");
        }
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (IsDshProcess(p))
                {
                    RunTaskKill("/F /T /PID " + p.Id);
                }
            }
            catch
            {
            }
        }

        Thread.Sleep(500);
    }

    static void RunTaskKill(string arguments)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(10000);
            }
        }
        catch
        {
        }
    }

    static bool IsDshProcess(Process p)
    {
        try
        {
            string path = GetProcessExecutablePath(p);
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    if (path.Equals(Assembly.GetEntryAssembly().Location, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
                catch
                {
                }

                if (!string.IsNullOrEmpty(DshInstallDir) &&
                    path.StartsWith(DshInstallDir + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string fileName = Path.GetFileName(path);
                if (IsKnownExeName(fileName))
                {
                    return true;
                }
            }

            if (IsKnownProcessName(p.ProcessName))
            {
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    static void DeleteDirectoryWithRetry(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!Directory.Exists(dir)) return;

        for (int i = 0; i < 8; i++)
        {
            try
            {
                DeleteDirectorySafe(dir);
                Log("  Deleted directory: " + dir);
                return;
            }
            catch (Exception ex)
            {
                if (i == 7)
                {
                    Log("  Failed to delete (may be in use): " + dir + " -> " + ex.Message);
                    failureCount++;
                }
                else
                {
                    Thread.Sleep(800);
                }
            }
        }
    }

    static void DeleteDirectorySafe(string path)
    {
        if (!Directory.Exists(path)) return;

        FileAttributes attr = File.GetAttributes(path);
        if ((attr & FileAttributes.ReparsePoint) != 0)
        {
            // Never follow a reparse point into its target.
            Directory.Delete(path, false);
            return;
        }

        // Clear read-only attributes and delete all files first. Do not swallow
        // exceptions: a locked file must make DeleteDirectoryWithRetry retry the
        // whole directory and eventually count the failure in silent mode.
        foreach (string file in Directory.GetFiles(path))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }

        // Recurse into subdirectories, then remove the now-empty directory.
        // Exceptions propagate so the outer retry loop sees the failure.
        foreach (string sub in Directory.GetDirectories(path))
        {
            DeleteDirectorySafe(sub);
        }

        Directory.Delete(path, false);
    }

    static void DeleteFileIfExists(string file)
    {
        if (string.IsNullOrEmpty(file)) return;
        if (!File.Exists(file)) return;

        try
        {
            File.Delete(file);
            Log("  Deleted file: " + file);
        }
        catch (Exception ex)
        {
            Log("  Failed to delete file: " + file + " -> " + ex.Message);
            failureCount++;
        }
    }
#endregion

#region Registry & PATH Cleanup
    static void DeleteRegistryKeys()
    {
        Log("[2/9] Cleaning registry...");

        DeleteMatchingUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM 64-bit");
        DeleteMatchingUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM 32-bit");
        DeleteMatchingUninstallKeys(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU 64-bit");
        DeleteMatchingUninstallKeys(RegistryHive.CurrentUser, RegistryView.Registry32, "HKCU 32-bit");

        // Legacy hardcoded key: harmless fallback if a future entry stops
        // exposing usual values (DisplayName/DisplayIcon/UninstallString).
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
            {
                if (key != null && key.OpenSubKey("62276e9d-c5f3-5091-b4ee-c7144d6db450") != null)
                {
                    key.DeleteSubKeyTree("62276e9d-c5f3-5091-b4ee-c7144d6db450", false);
                    Log("  Deleted legacy HKLM uninstall key: " + LegacyUninstallRegKey);
                }
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to delete legacy HKLM uninstall key: " + ex.Message);
        }

        foreach (string appId in KnownAppIds)
        {
            DeleteRegSubKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + appId, "HKCU notification settings");
            DeleteRegSubKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications\Backup\" + appId, "HKCU push backup");
        }

        // 历史遗留变量，某些旧版本 DSH 曾使用，卸载时清理
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Environment", true))
            {
                if (key != null && key.GetValue("VIPSHOME") != null)
                {
                    key.DeleteValue("VIPSHOME");
                    Log("  Deleted user environment variable VIPSHOME");
                }
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to delete VIPSHOME: " + ex.Message);
        }
    }

    static bool IsKnownAppId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (string known in KnownAppIds)
        {
            if (known.Equals(id, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // Some uninstall registry keys are named "<AppId>_is1" or "<AppId>-extra".
    static bool MatchesKnownAppId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        if (IsKnownAppId(id)) return true;
        foreach (string known in KnownAppIds)
        {
            if (id.StartsWith(known + "_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(known + "-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    static void DeleteMatchingUninstallKeys(RegistryHive hive, RegistryView view, string label)
    {
        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey uninstallRoot = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
            {
                if (uninstallRoot == null) return;
                string[] names = uninstallRoot.GetSubKeyNames();
                foreach (string name in names)
                {
                    bool matched = false;
                    // Each subkey is handled independently so a single
                    // permission/read failure does not abort the whole scan.
                    try
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
                            matched = IsDshUninstallEntry(displayName, displayIcon, uninstallString, quietUninstallString, installLocation, bundleCachePath, publisher, urlInfoAbout) ||
                                      MatchesKnownAppId(name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("  Failed to read " + label + " uninstall key " + name + ": " + ex.Message);
                        continue;
                    }

                    if (matched)
                    {
                        try
                        {
                            uninstallRoot.DeleteSubKeyTree(name, false);
                            Log("  Deleted " + label + " uninstall key: " + Path.Combine(uninstallRoot.Name, name));
                        }
                        catch (Exception ex)
                        {
                            Log("  Failed to delete " + label + " uninstall key " + name + ": " + ex.Message);
                            failureCount++;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to scan " + label + " uninstall keys: " + ex.Message);
        }
    }

    static void DeleteRegSubKey(RegistryKey root, string subKey, string label)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey(subKey))
            {
                if (key == null) return;
            }
            root.DeleteSubKeyTree(subKey, false);
            Log("  Deleted " + label + ": " + subKey);
        }
        catch (Exception ex)
        {
            Log("  Failed to delete " + label + ": " + ex.Message);
            failureCount++;
        }
    }

    static void CleanupMachinePath()
    {
        Log("[3/9] Cleaning machine PATH...");
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(MachineEnvKey, true))
            {
                CleanPathRegistryKey(key, "machine");
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to clean machine PATH: " + ex.Message);
        }
    }

    // Also clean the user-level PATH (HKCU\Environment) which DSH installers
    // may append to; this was previously not handled.
    static void CleanupUserPath()
    {
        Log("[3b/9] Cleaning user PATH...");
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Environment", true))
            {
                CleanPathRegistryKey(key, "user");
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to clean user PATH: " + ex.Message);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    static void BroadcastEnvironmentChange()
    {
        try
        {
            UIntPtr result;
            SendMessageTimeout((IntPtr)0xffff, 0x001A, UIntPtr.Zero, "Environment", 0x0002, 5000, out result);
            Log("  Broadcast WM_SETTINGCHANGE for environment variables.");
        }
        catch (Exception ex)
        {
            Log("  Failed to broadcast environment change: " + ex.Message);
        }
    }

    static void CleanupRunKeys()
    {
        Log("[3c/9] Cleaning Run/RunOnce startup entries...");
        RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
        string[] subKeys = new string[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
        };
        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                foreach (string subKey in subKeys)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey run = baseKey.OpenSubKey(subKey, true))
                        {
                            if (run == null) continue;
                            foreach (string valueName in run.GetValueNames())
                            {
                                try
                                {
                                    string value = run.GetValue(valueName, "").ToString();
                                    if (IsDshRelatedName(valueName) || IsDshRelatedName(value) || IsDshRelatedPath(value))
                                    {
                                        run.DeleteValue(valueName, false);
                                        Log("  Deleted startup entry (" + subKey + "): " + valueName + " = " + value);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log("  Failed to delete startup entry " + valueName + " (" + subKey + "): " + ex.Message);
                                    failureCount++;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log("  Failed to scan startup key (" + subKey + ", " + hive + ", " + view + "): " + ex.Message);
                    }
                }
            }
        }
    }

    static void CleanPathRegistryKey(RegistryKey key, string scope)
    {
        if (key == null) return;
        string path = key.GetValue("Path", "").ToString();
        string[] parts = path.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        List<string> kept = new List<string>();
        bool changed = false;
        foreach (string part in parts)
        {
            string trimmed = part.Trim().TrimEnd('\\');
            if (IsDshPathEntry(trimmed))
            {
                changed = true;
                Log("  Removed from " + scope + " PATH: " + part);
            }
            else
            {
                kept.Add(part);
            }
        }
        if (changed)
        {
            // Preserve the original value kind. Some systems only accept
            // REG_SZ for PATH and reject changing it to REG_EXPAND_SZ.
            RegistryValueKind kind = RegistryValueKind.String;
            try
            {
                kind = key.GetValueKind("Path");
                if (kind == RegistryValueKind.None || kind == RegistryValueKind.Unknown)
                {
                    kind = RegistryValueKind.String;
                }
            }
            catch
            {
                kind = RegistryValueKind.String;
            }
            key.SetValue("Path", string.Join(";", kept.ToArray()), kind);
        }
    }

    static bool IsDshPathEntry(string trimmed)
    {
        if (string.IsNullOrEmpty(trimmed)) return false;
        if (trimmed.Equals(Path.Combine(DshRuntime, "node"), StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Equals(Path.Combine(DshHome, "bin"), StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(DshInstallDir) &&
            trimmed.StartsWith(DshInstallDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        // Broader heuristic for variants not installed in the detected dir.
        return IsDshRelatedPath(trimmed);
    }
#endregion

#region User Data Retention & Cleanup
    static void PreserveSelectedPlugins()
    {
        if (!keepPlugins || !keepRuntime) return;
        Log("[4/9] Preserving selected DSH plugins...");

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

    static void CleanDshHome()
    {
        Log("[5/9] Cleaning DSH user data...");

        if (!Directory.Exists(DshHome))
        {
            Log("  DSH user data directory does not exist: " + DshHome);
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
        Log("[6/9] Cleaning temp dsh-* directories...");
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
                    Log("  Skipping non-DSH temp: " + d);
                    continue;
                }

                // Use the same retrying deleter as install/user-data directories
                // so a briefly locked temp folder does not fail the cleanup.
                DeleteDirectoryWithRetry(d);
            }
        }
        catch
        {
        }
    }
#endregion
#region Logging & Helpers
    static void InitializeLog()
    {
        try
        {
            string dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(LogFilePath, "===== DSH Desktop Uninstaller Log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====" + Environment.NewLine);
        }
        catch
        {
        }
    }

    static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFilePath, message + Environment.NewLine);
        }
        catch
        {
        }
        Console.WriteLine(message);

        try
        {
            if (progressForm != null && !progressForm.IsDisposed)
            {
                progressForm.Append(message);
                Application.DoEvents();
            }
        }
        catch
        {
        }
    }

    static void Pause()
    {
        if (!silent)
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            try
            {
                Console.ReadKey(true);
            }
            catch
            {
            }
        }
    }
#endregion
#region GUI (RetentionForm)
    private class ProgressForm : Form
    {
        private TextBox txtLog;

        public ProgressForm()
        {
            Text = "DSH 卸载进度";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(560, 320);
            Font = new Font("Microsoft YaHei UI", 9F);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Both;
            txtLog.ReadOnly = true;
            txtLog.WordWrap = false;
            txtLog.Dock = DockStyle.Fill;
            Controls.Add(txtLog);
        }

        public void Append(string message)
        {
            try
            {
                txtLog.AppendText(message + Environment.NewLine);
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            catch
            {
            }
        }
    }

    class RetentionForm : Form
    {
        private class PresetListItem
        {
            public string Folder;
            public string Display;
            public string Label;

            public PresetListItem(string folder, string display, string label)
            {
                Folder = folder;
                Display = display;
                Label = label;
            }

            public override string ToString()
            {
                return Label;
            }
        }

        private class PluginListItem
        {
            public string Package;
            public string Label;

            public PluginListItem(string package, string label)
            {
                Package = package;
                Label = label;
            }

            public override string ToString()
            {
                return Label;
            }
        }
        private class SkillListItem
        {
            public string Name;
            public string Label;

            public SkillListItem(string name, string label)
            {
                Name = name;
                Label = label;
            }

            public override string ToString()
            {
                return Label;
            }
        }
        private class GrayableCheckBox : CheckBox
          {
              protected override void OnPaint(PaintEventArgs e)
              {
                  if (Enabled)
                  {
                      base.OnPaint(e);
                      return;
                  }

                  using (SolidBrush bg = new SolidBrush(Parent != null ? Parent.BackColor : BackColor))
                  {
                      e.Graphics.FillRectangle(bg, ClientRectangle);
                  }

                  CheckBoxState state;
                  switch (CheckState)
                  {
                      case CheckState.Checked:
                          state = CheckBoxState.CheckedDisabled;
                          break;
                      case CheckState.Indeterminate:
                          state = CheckBoxState.MixedDisabled;
                          break;
                      default:
                          state = CheckBoxState.UncheckedDisabled;
                          break;
                  }

                  Size glyphSize = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
                  Point boxLocation = new Point(0, Math.Max(0, (Height - glyphSize.Height) / 2));
                  CheckBoxRenderer.DrawCheckBox(e.Graphics, boxLocation, state);

                  Rectangle textBounds = new Rectangle(
                      glyphSize.Width + 4,
                      0,
                      Math.Max(0, Width - glyphSize.Width - 4),
                      Height);
                  TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, SystemColors.GrayText,
                      TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
              }
          }

        private GrayableCheckBox chkPresets;
        private CheckedListBox clbPresets;
        private CheckBox chkRuntime;
        private GrayableCheckBox chkPlugins;
        private CheckedListBox clbPlugins;
        private GrayableCheckBox chkSkills;
        private CheckedListBox clbSkills;
        private CheckBox chkChatData;
        private CheckBox chkAppSettings;
        private CheckBox chkModelConfig;
        private CheckBox chkOtherUserData;
        private RadioButton rbDetectRunning;
        private RadioButton rbDefault;
        private bool updatingSkillState;
        private bool updatingPresetState;
        private bool hasSkills;
        private bool updatingPluginState;
        private bool hasPresets;
        private bool hasPlugins;

        public bool KeepAgentPresets { get { return chkPresets.CheckState != CheckState.Unchecked; } }
        public bool KeepRuntime { get { return chkRuntime.Checked || chkPlugins.CheckState != CheckState.Unchecked; } }
        public bool KeepChatData { get { return chkChatData.Checked; } }
        public bool KeepAppSettings { get { return chkAppSettings.Checked; } }
        public bool KeepModelConfig { get { return chkModelConfig.Checked; } }
        public bool KeepOtherUserData { get { return chkOtherUserData.Checked; } }
        public bool KeepPlugins { get { return chkPlugins.CheckState != CheckState.Unchecked; } }
        public bool KeepSkills { get { return chkSkills.CheckState != CheckState.Unchecked; } }
        public List<string> KeepPresetNames
        {
            get
            {
                if (chkPresets.CheckState == CheckState.Unchecked)
                {
                    return new List<string>();
                }

                List<string> names = new List<string>();
                foreach (object item in clbPresets.CheckedItems)
                {
                    PresetListItem preset = item as PresetListItem;
                    if (preset != null && !string.IsNullOrEmpty(preset.Folder))
                    {
                        names.Add(preset.Folder);
                    }
                }
                return names;
            }
        }
        public List<string> KeepPluginNames
        {
            get
            {
                if (chkPlugins.CheckState == CheckState.Unchecked)
                {
                    return new List<string>();
                }

                List<string> names = new List<string>();
                foreach (object item in clbPlugins.CheckedItems)
                {
                    PluginListItem plugin = item as PluginListItem;
                    if (plugin != null && !string.IsNullOrEmpty(plugin.Package))
                    {
                        names.Add(plugin.Package);
                    }
                }
                return names;
            }
        }
        public List<string> KeepSkillNames
        {
            get
            {
                if (chkSkills.CheckState == CheckState.Unchecked)
                {
                    return new List<string>();
                }

                List<string> names = new List<string>();
                foreach (object item in clbSkills.CheckedItems)
                {
                    SkillListItem skill = item as SkillListItem;
                    if (skill != null && !string.IsNullOrEmpty(skill.Name))
                    {
                        names.Add(skill.Name);
                    }
                }
                return names;
            }
        }
        public bool UseDetectedRunningDsh
        {
            get { return rbDetectRunning.Checked; }
        }

        private void SetAllPresetItems(bool isChecked)
        {
            for (int i = 0; i < clbPresets.Items.Count; i++)
            {
                clbPresets.SetItemChecked(i, isChecked);
            }
        }

        private void UpdatePresetParentState()
        {
            if (updatingPresetState) return;
            updatingPresetState = true;
            try
            {
                int total = clbPresets.Items.Count;
                if (total > 0)
                {
                    int checkedCount = clbPresets.CheckedItems.Count;
                    if (checkedCount == 0)
                    {
                        chkPresets.CheckState = CheckState.Unchecked;
                    }
                    else if (checkedCount == total)
                    {
                        chkPresets.CheckState = CheckState.Checked;
                    }
                    else
                    {
                        chkPresets.CheckState = CheckState.Indeterminate;
                    }
                }
                clbPresets.Enabled = chkPresets.CheckState != CheckState.Unchecked && hasPresets;
            }
            finally
            {
                updatingPresetState = false;
            }
        }

        private void SetAllPluginItems(bool isChecked)
        {
            for (int i = 0; i < clbPlugins.Items.Count; i++)
            {
                clbPlugins.SetItemChecked(i, isChecked);
            }
        }

        private void UpdatePluginParentState()
        {
            if (updatingPluginState) return;
            updatingPluginState = true;
            try
            {
                int total = clbPlugins.Items.Count;
                if (total > 0)
                {
                    int checkedCount = clbPlugins.CheckedItems.Count;
                    if (checkedCount == 0)
                    {
                        chkPlugins.CheckState = CheckState.Unchecked;
                    }
                    else if (checkedCount == total)
                    {
                        chkPlugins.CheckState = CheckState.Checked;
                    }
                    else
                    {
                        chkPlugins.CheckState = CheckState.Indeterminate;
                    }
                }
                if (chkPlugins.CheckState != CheckState.Unchecked)
                {
                    chkRuntime.Checked = true;
                }
                clbPlugins.Enabled = chkPlugins.CheckState != CheckState.Unchecked && hasPlugins;
            }
            finally
            {
                updatingPluginState = false;
            }
        }

        private void SetAllSkillItems(bool isChecked)
        {
            for (int i = 0; i < clbSkills.Items.Count; i++)
            {
                clbSkills.SetItemChecked(i, isChecked);
            }
        }

        private void UpdateSkillParentState()
        {
            if (updatingSkillState) return;
            updatingSkillState = true;
            try
            {
                int total = clbSkills.Items.Count;
                if (total > 0)
                {
                    int checkedCount = clbSkills.CheckedItems.Count;
                    if (checkedCount == 0)
                    {
                        chkSkills.CheckState = CheckState.Unchecked;
                    }
                    else if (checkedCount == total)
                    {
                        chkSkills.CheckState = CheckState.Checked;
                    }
                    else
                    {
                        chkSkills.CheckState = CheckState.Indeterminate;
                    }
                }
                clbSkills.Enabled = chkSkills.CheckState != CheckState.Unchecked && hasSkills;
            }
            finally
            {
                updatingSkillState = false;
            }
        }

        private void DrawCheckedListBoxItem(object sender, DrawItemEventArgs e, CheckedListBox list)
        {
            if (e.Index < 0) return;

            bool enabled = list.Enabled;
            bool isChecked = list.GetItemChecked(e.Index);

            using (SolidBrush bg = new SolidBrush(enabled ? SystemColors.Window : SystemColors.Control))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            Rectangle checkRect = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + (e.Bounds.Height - 13) / 2, 13, 13);
            ButtonState state;
            if (!enabled)
            {
                state = isChecked ? (ButtonState.Checked | ButtonState.Inactive) : ButtonState.Inactive;
            }
            else
            {
                state = isChecked ? ButtonState.Checked : ButtonState.Normal;
            }
            ControlPaint.DrawCheckBox(e.Graphics, checkRect, state);

            Rectangle textRect = new Rectangle(e.Bounds.X + 20, e.Bounds.Y, e.Bounds.Width - 22, e.Bounds.Height);
            Color textColor = enabled ? SystemColors.WindowText : SystemColors.GrayText;
            TextRenderer.DrawText(e.Graphics, list.Items[e.Index].ToString(), e.Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        public void SetRetentionOptions(bool presets, bool runtime, bool chatData, bool appSettings, bool modelConfig, bool otherUserData, bool plugins, bool skills, List<string> presetNames, List<string> pluginNames, List<string> skillNames)
        {
            chkRuntime.Checked = runtime;
            chkChatData.Checked = chatData;
            chkAppSettings.Checked = appSettings;
            chkModelConfig.Checked = modelConfig;
            chkOtherUserData.Checked = otherUserData;

            updatingPresetState = true;
            try
            {
                if (presets)
                {
                    if (presetNames == null || presetNames.Count == 0)
                    {
                        SetAllPresetItems(true);
                        chkPresets.CheckState = CheckState.Checked;
                    }
                    else
                    {
                        SetAllPresetItems(false);
                        HashSet<string> folderNames = new HashSet<string>(presetNames, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < clbPresets.Items.Count; i++)
                        {
                            PresetListItem item = clbPresets.Items[i] as PresetListItem;
                            if (item != null &&
                            (folderNames.Contains(item.Folder) ||
                             (!string.IsNullOrEmpty(item.Display) && folderNames.Contains(item.Display))))
                            {
                                clbPresets.SetItemChecked(i, true);
                            }
                        }
                    }
                }
                else
                {
                    SetAllPresetItems(false);
                    chkPresets.CheckState = CheckState.Unchecked;
                }
            }
            finally
            {
                updatingPresetState = false;
            }

            updatingPluginState = true;
            try
            {
                if (plugins)
                {
                    if (pluginNames == null || pluginNames.Count == 0)
                    {
                        SetAllPluginItems(true);
                        chkPlugins.CheckState = CheckState.Checked;
                        chkRuntime.Checked = true;
                    }
                    else
                    {
                        SetAllPluginItems(false);
                        HashSet<string> packageNames = new HashSet<string>(pluginNames, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < clbPlugins.Items.Count; i++)
                        {
                            PluginListItem item = clbPlugins.Items[i] as PluginListItem;
                            if (item != null && packageNames.Contains(item.Package))
                            {
                                clbPlugins.SetItemChecked(i, true);
                            }
                        }
                    }
                }
                else
                {
                    SetAllPluginItems(false);
                    chkPlugins.CheckState = CheckState.Unchecked;
                }
            }
            finally
            {
                updatingPluginState = false;
            }

            updatingSkillState = true;
            try
            {
                if (skills)
                {
                    if (skillNames == null || skillNames.Count == 0)
                    {
                        SetAllSkillItems(true);
                        chkSkills.CheckState = CheckState.Checked;
                    }
                    else
                    {
                        SetAllSkillItems(false);
                        HashSet<string> skillSet = new HashSet<string>(skillNames, StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < clbSkills.Items.Count; i++)
                        {
                            SkillListItem item = clbSkills.Items[i] as SkillListItem;
                            if (item != null && skillSet.Contains(item.Name))
                            {
                                clbSkills.SetItemChecked(i, true);
                            }
                        }
                    }
                }
                else
                {
                    SetAllSkillItems(false);
                    chkSkills.CheckState = CheckState.Unchecked;
                }
            }
            finally
            {
                updatingSkillState = false;
            }


            UpdatePresetParentState();
            UpdatePluginParentState();
            UpdateSkillParentState();
        }
        public RetentionForm()
        {
            Text = "DSH 桌面端卸载确认";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(520, 650);
            Font = new Font("Microsoft YaHei UI", 9F);
            // High-DPI: scale the fixed-pixel layout proportionally on 125%/150%
            // displays (Option A — lightweight; avoids absolute-position drift).
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Label lblCurrentDsh = new Label();
            lblCurrentDsh.Text = "当前DSH: " + DSHDesktopUninstaller.DetectedVariantLabel;
            lblCurrentDsh.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblCurrentDsh.AutoSize = false;
            lblCurrentDsh.AutoEllipsis = true;
            lblCurrentDsh.SetBounds(22, 10, 476, 22);

            Label lblTitle = new Label();
            lblTitle.Text = "确定要卸载 DSH / DeepSeek Harness 桌面端吗？";
            lblTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblTitle.AutoSize = false;
            lblTitle.SetBounds(22, 38, 476, 30);

            Label lblDesc = new Label();
            lblDesc.Text = "将删除程序、更新器、缓存、快捷方式、注册表和 DSH 用户数据。\r\n默认不保留用户数据，可在下方勾选需要保留的项目。";
            lblDesc.AutoSize = false;
            lblDesc.SetBounds(22, 74, 476, 48);

            GroupBox grpMode = new GroupBox();
            grpMode.Text = "卸载模式";
            grpMode.SetBounds(22, 128, 476, 72);

            rbDetectRunning = new RadioButton();
            string runningDir = DSHDesktopUninstaller.DetectedRunningDshDir;
            rbDetectRunning.Text = string.IsNullOrEmpty(runningDir)
                ? "程序识别卸载（未检测到运行中的 DSH，将回退默认定位）"
                : "程序识别卸载（当前运行：" + runningDir + "）";
            rbDetectRunning.SetBounds(14, 20, 448, 24);
            rbDetectRunning.Checked = !string.IsNullOrEmpty(runningDir);

            rbDefault = new RadioButton();
            rbDefault.Text = "默认卸载（按注册表/常见安装路径检测）";
            rbDefault.SetBounds(14, 46, 448, 22);
            rbDefault.Checked = !rbDetectRunning.Checked;

            grpMode.Controls.Add(rbDetectRunning);
            grpMode.Controls.Add(rbDefault);

            GroupBox grp = new GroupBox();
            grp.Text = "可选保留项";
            grp.SetBounds(22, 206, 476, 390);

            Panel pnlOptions = new Panel();
            pnlOptions.SetBounds(8, 20, 458, 358);
            pnlOptions.AutoScroll = true;

            chkPresets = new GrayableCheckBox();
            chkPresets.ThreeState = true;
            chkPresets.AutoCheck = false;
            chkPresets.Text = "保留预设（按名称保留）";
            chkPresets.SetBounds(18, 24, 440, 24);
            chkPresets.Click += delegate
            {
                chkPresets.CheckState = chkPresets.CheckState == CheckState.Checked
                    ? CheckState.Unchecked
                    : CheckState.Checked;
            };
            chkPresets.CheckStateChanged += delegate
            {
                if (updatingPresetState) return;
                updatingPresetState = true;
                try
                {
                    if (chkPresets.CheckState == CheckState.Checked)
                    {
                        SetAllPresetItems(true);
                    }
                    else if (chkPresets.CheckState == CheckState.Unchecked)
                    {
                        SetAllPresetItems(false);
                    }
                    clbPresets.Enabled = chkPresets.CheckState != CheckState.Unchecked && hasPresets;
                }
                finally
                {
                    updatingPresetState = false;
                }
            };

            clbPresets = new CheckedListBox();
            clbPresets.SetBounds(38, 50, 420, 70);
            clbPresets.CheckOnClick = true;
            clbPresets.IntegralHeight = false;
            clbPresets.DrawMode = DrawMode.OwnerDrawFixed;
            clbPresets.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                DrawCheckedListBoxItem(sender, e, clbPresets);
            };
            clbPresets.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (updatingPresetState) return;
                int total = clbPresets.Items.Count;
                if (total == 0) return;

                int checkedCount = clbPresets.CheckedItems.Count;
                if (e.NewValue == CheckState.Checked)
                {
                    checkedCount++;
                }
                else if (e.NewValue == CheckState.Unchecked && clbPresets.CheckedIndices.Contains(e.Index))
                {
                    checkedCount--;
                }

                CheckState state;
                if (checkedCount == 0)
                {
                    state = CheckState.Unchecked;
                }
                else if (checkedCount == total)
                {
                    state = CheckState.Checked;
                }
                else
                {
                    state = CheckState.Indeterminate;
                }

                if (chkPresets.CheckState != state)
                {
                    updatingPresetState = true;
                    try
                    {
                        chkPresets.CheckState = state;
                    }
                    finally
                    {
                        updatingPresetState = false;
                    }
                }
                clbPresets.Enabled = chkPresets.CheckState != CheckState.Unchecked && hasPresets;
            };

            List<PresetInfo> detected = DSHDesktopUninstaller.DetectAgentPresets();
            hasPresets = detected.Count > 0;
            if (detected.Count == 0)
            {
                clbPresets.Items.Add(new PresetListItem("", "", "（未检测到预设）"));
                clbPresets.Enabled = false;
                chkPresets.Enabled = false;
            }
            else
            {
                foreach (PresetInfo preset in detected)
                {
                    string label = string.Equals(preset.FolderName, preset.DisplayName, StringComparison.OrdinalIgnoreCase)
                        ? preset.DisplayName
                        : preset.DisplayName + " (" + preset.FolderName + ")";
                    clbPresets.Items.Add(new PresetListItem(preset.FolderName, preset.DisplayName, label));
                }
            }

            chkChatData = new CheckBox();
            chkChatData.Text = "保留聊天数据（.dsh\\sessions 对话记录）";
            chkChatData.SetBounds(18, 126, 440, 24);

            chkPlugins = new GrayableCheckBox();
            chkPlugins.ThreeState = true;
            chkPlugins.AutoCheck = false;
            chkPlugins.Text = "保留插件（按名称保留，自动保留运行时）";
            chkPlugins.SetBounds(18, 154, 440, 24);
            chkPlugins.Click += delegate
            {
                chkPlugins.CheckState = chkPlugins.CheckState == CheckState.Checked
                    ? CheckState.Unchecked
                    : CheckState.Checked;
            };
            chkPlugins.CheckStateChanged += delegate
            {
                if (updatingPluginState) return;
                updatingPluginState = true;
                try
                {
                    if (chkPlugins.CheckState == CheckState.Checked)
                    {
                        SetAllPluginItems(true);
                        chkRuntime.Checked = true;
                    }
                    else if (chkPlugins.CheckState == CheckState.Unchecked)
                    {
                        SetAllPluginItems(false);
                    }
                    clbPlugins.Enabled = chkPlugins.CheckState != CheckState.Unchecked && hasPlugins;
                }
                finally
                {
                    updatingPluginState = false;
                }
            };

            clbPlugins = new CheckedListBox();
            clbPlugins.SetBounds(38, 180, 420, 120);
            clbPlugins.CheckOnClick = true;
            clbPlugins.IntegralHeight = false;
            clbPlugins.HorizontalScrollbar = true;
            clbPlugins.DrawMode = DrawMode.OwnerDrawFixed;
            clbPlugins.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                DrawCheckedListBoxItem(sender, e, clbPlugins);
            };
            clbPlugins.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (updatingPluginState) return;
                int total = clbPlugins.Items.Count;
                if (total == 0) return;

                int checkedCount = clbPlugins.CheckedItems.Count;
                if (e.NewValue == CheckState.Checked)
                {
                    checkedCount++;
                }
                else if (e.NewValue == CheckState.Unchecked && clbPlugins.CheckedIndices.Contains(e.Index))
                {
                    checkedCount--;
                }

                CheckState state;
                if (checkedCount == 0)
                {
                    state = CheckState.Unchecked;
                }
                else if (checkedCount == total)
                {
                    state = CheckState.Checked;
                }
                else
                {
                    state = CheckState.Indeterminate;
                }

                if (chkPlugins.CheckState != state)
                {
                    updatingPluginState = true;
                    try
                    {
                        chkPlugins.CheckState = state;
                    }
                    finally
                    {
                        updatingPluginState = false;
                    }
                }
                if (chkPlugins.CheckState != CheckState.Unchecked)
                {
                    chkRuntime.Checked = true;
                }
                clbPlugins.Enabled = chkPlugins.CheckState != CheckState.Unchecked && hasPlugins;
            };

            List<PluginInfo> detectedPlugins = DSHDesktopUninstaller.DetectPlugins();
            hasPlugins = detectedPlugins.Count > 0;
            if (detectedPlugins.Count == 0)
            {
                clbPlugins.Items.Add(new PluginListItem("", "（未检测到插件）"));
                clbPlugins.Enabled = false;
                chkPlugins.Enabled = false;
            }
            else
            {
                foreach (PluginInfo plugin in detectedPlugins)
                {
                    clbPlugins.Items.Add(new PluginListItem(plugin.PackageName, plugin.DisplayName));
                }
            }

            chkSkills = new GrayableCheckBox();
            chkSkills.ThreeState = true;
            chkSkills.AutoCheck = false;
            chkSkills.Text = "保留 skills（按名称保留）";
            chkSkills.SetBounds(18, 310, 440, 24);
            chkSkills.Click += delegate
            {
                chkSkills.CheckState = chkSkills.CheckState == CheckState.Checked
                    ? CheckState.Unchecked
                    : CheckState.Checked;
            };
            chkSkills.CheckStateChanged += delegate
            {
                if (updatingSkillState) return;
                updatingSkillState = true;
                try
                {
                    if (chkSkills.CheckState == CheckState.Checked)
                    {
                        SetAllSkillItems(true);
                    }
                    else if (chkSkills.CheckState == CheckState.Unchecked)
                    {
                        SetAllSkillItems(false);
                    }
                    clbSkills.Enabled = chkSkills.CheckState != CheckState.Unchecked && hasSkills;
                }
                finally
                {
                    updatingSkillState = false;
                }
            };

            clbSkills = new CheckedListBox();
            clbSkills.SetBounds(38, 336, 420, 90);
            clbSkills.CheckOnClick = true;
            clbSkills.IntegralHeight = false;
            clbSkills.HorizontalScrollbar = true;
            clbSkills.DrawMode = DrawMode.OwnerDrawFixed;
            clbSkills.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                DrawCheckedListBoxItem(sender, e, clbSkills);
            };
            clbSkills.ItemCheck += delegate(object sender, ItemCheckEventArgs e)
            {
                if (updatingSkillState) return;
                int total = clbSkills.Items.Count;
                if (total == 0) return;

                int checkedCount = clbSkills.CheckedItems.Count;
                if (e.NewValue == CheckState.Checked)
                {
                    checkedCount++;
                }
                else if (e.NewValue == CheckState.Unchecked && clbSkills.CheckedIndices.Contains(e.Index))
                {
                    checkedCount--;
                }

                CheckState state;
                if (checkedCount == 0)
                {
                    state = CheckState.Unchecked;
                }
                else if (checkedCount == total)
                {
                    state = CheckState.Checked;
                }
                else
                {
                    state = CheckState.Indeterminate;
                }

                if (chkSkills.CheckState != state)
                {
                    updatingSkillState = true;
                    try
                    {
                        chkSkills.CheckState = state;
                    }
                    finally
                    {
                        updatingSkillState = false;
                    }
                }
                clbSkills.Enabled = chkSkills.CheckState != CheckState.Unchecked && hasSkills;
            };

            List<SkillInfo> detectedSkills = DSHDesktopUninstaller.DetectSkills();
            hasSkills = detectedSkills.Count > 0;
            if (detectedSkills.Count == 0)
            {
                clbSkills.Items.Add(new SkillListItem("", "（未检测到 skills）"));
                clbSkills.Enabled = false;
                chkSkills.Enabled = false;
            }
            else
            {
                foreach (SkillInfo skill in detectedSkills)
                {
                    clbSkills.Items.Add(new SkillListItem(skill.Name, skill.DisplayName));
                }
            }


            chkAppSettings = new CheckBox();
            chkAppSettings.Text = "保留应用设置（settings.yaml）";
            chkAppSettings.SetBounds(18, 432, 440, 24);

            chkModelConfig = new CheckBox();
            chkModelConfig.Text = "保留模型配置与凭据（.credentials.yaml + settings.yaml 模型部分）";
            chkModelConfig.SetBounds(18, 460, 440, 24);

            chkOtherUserData = new CheckBox();
            chkOtherUserData.Text = "保留其他 .dsh 数据（graph-memory/storages/super-injector 等）";
            chkOtherUserData.SetBounds(18, 488, 440, 24);

            chkRuntime = new CheckBox();
            chkRuntime.Text = "保留 .dsh-runtime（DSH CLI 运行时）";
            chkRuntime.SetBounds(18, 516, 440, 24);

            pnlOptions.Controls.Add(chkPresets);
            pnlOptions.Controls.Add(clbPresets);
            pnlOptions.Controls.Add(chkChatData);
            pnlOptions.Controls.Add(chkPlugins);
            pnlOptions.Controls.Add(clbPlugins);
            pnlOptions.Controls.Add(chkSkills);
            pnlOptions.Controls.Add(clbSkills);
            pnlOptions.Controls.Add(chkAppSettings);
            pnlOptions.Controls.Add(chkModelConfig);
            pnlOptions.Controls.Add(chkOtherUserData);
            pnlOptions.Controls.Add(chkRuntime);
            grp.Controls.Add(pnlOptions);

            Button btnOk = new Button();
            btnOk.Text = "卸载";
            btnOk.DialogResult = DialogResult.OK;
            btnOk.SetBounds(260, 596, 100, 30);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.SetBounds(370, 596, 100, 30);

            Controls.Add(lblCurrentDsh);
            Controls.Add(lblTitle);
            Controls.Add(lblDesc);
            Controls.Add(grpMode);
            Controls.Add(grp);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }
}
#endregion
