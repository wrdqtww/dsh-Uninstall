using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Management;
using System.Runtime.InteropServices;
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
        catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }

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
        string full = PathSafety.NormalizeDirForDelete(dir);
        if (string.IsNullOrEmpty(full))
        {
            Log("  Refusing unsafe install dir: " + dir);
            return;
        }
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
        try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")); } catch (Exception ex) { Log("  Warning in FindDshInstallDirsInVariantLocations: " + ex.Message); }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)); } catch (Exception ex) { Log("  Warning in FindDshInstallDirsInVariantLocations: " + ex.Message); }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch (Exception ex) { Log("  Warning in FindDshInstallDirsInVariantLocations: " + ex.Message); }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch (Exception ex) { Log("  Warning in FindDshInstallDirsInVariantLocations: " + ex.Message); }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); } catch (Exception ex) { Log("  Warning in FindDshInstallDirsInVariantLocations: " + ex.Message); }

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
        List<string> knownExeCandidates = new List<string>();
        ForEachDshUninstallEntry(false, (info, root) =>
        {
            if (!MatchesKnownAppId(info.KeyName) &&
                !IsDshUninstallEntry(info.DisplayName, info.DisplayIcon, info.UninstallString, info.QuietUninstallString, info.InstallLocation, info.BundleCachePath, info.Publisher, info.URLInfoAbout))
            {
                return;
            }
            string dir = ResolveInstallDirFromRegistryEntry(info.DisplayIcon, info.UninstallString, info.QuietUninstallString, info.InstallLocation, info.BundleCachePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            if (HasDshExecutable(dir) || HasDshSignature(dir))
            {
                AddInstallDir(knownExeCandidates, dir);
            }
            else
            {
                Log("  Skipping registry install dir without DSH signature: " + dir);
            }
        });
        List<string> result = new List<string>();
        foreach (string dir in knownExeCandidates) AddInstallDir(result, dir);
        return result;
    }

    static string ParseExePathFromCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        string cmd = commandLine.Trim();
        if (cmd.StartsWith("\""))
        {
            int end = cmd.IndexOf('\"', 1);
            if (end > 0) return cmd.Substring(1, end - 1);
        }
        int space = cmd.IndexOf(' ');
        return space > 0 ? cmd.Substring(0, space) : cmd;
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
        string path = value.Trim().Trim('\"').Trim();
        try
        {
            path = Environment.ExpandEnvironmentVariables(path);
            path = Path.GetFullPath(path);
        }
        catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }
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
        try { roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")); } catch (Exception ex) { Log("  Warning in FindDshInstallDirsInKnownLocations: " + ex.Message); }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch (Exception ex) { Log("  Warning in FindDshInstallDirsInKnownLocations: " + ex.Message); }
        try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch (Exception ex) { Log("  Warning in FindDshInstallDirsInKnownLocations: " + ex.Message); }

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
            catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }
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
        RefreshProcessCache();
        List<string> dirs = new List<string>();
        int total = 0;
        int unreadable = 0;
        Process[] procs = Process.GetProcesses();
        total = procs.Length;
        foreach (Process p in procs)
        {
            try
            {
                if (!MightBeDshProcess(p)) continue;
                string path = GetProcessExecutablePath(p);
                if (string.IsNullOrEmpty(path))
                {
                unreadable++;
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
                    string cmd = GetProcessCommandLine(p);
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
                catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && (HasDshExecutable(dir) || HasDshSignature(dir)))
                {
                    AddInstallDir(dirs, dir);
                }
            }
            catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }
        }
        if (unreadable > 0)
        {
            Log("  Running process scan: " + total + " processes, " + unreadable + " unreadable (system processes are skipped).");
        }
        foreach (Process pd in procs) { try { pd.Dispose(); } catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); } }
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
    class UninstallEntryInfo
    {
        public RegistryHive Hive;
        public RegistryView View;
        public string KeyName;
        public string DisplayName;
        public string DisplayIcon;
        public string UninstallString;
        public string QuietUninstallString;
        public string InstallLocation;
        public string BundleCachePath;
        public string Publisher;
        public string URLInfoAbout;
        public string PathForHeuristic;
    }

    // Shared enumerator for the uninstall-key scans used by variant label
    // resolution, residual collection and key deletion. All three call sites
    // read exactly the same values from the same registry roots, so this is
    // the single place where a new value (e.g. SystemComponent) gets added.
    static void ForEachDshUninstallEntry(bool writable, Action<UninstallEntryInfo, RegistryKey> action)
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
                    using (RegistryKey root = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable))
                    {
                        if (root == null) continue;
                        foreach (string name in root.GetSubKeyNames())
                        {
                            UninstallEntryInfo info = new UninstallEntryInfo();
                            info.Hive = hive;
                            info.View = view;
                            info.KeyName = name;
                            try
                            {
                                using (RegistryKey sub = root.OpenSubKey(name))
                                {
                                    if (sub == null) continue;
                                    info.DisplayName = sub.GetValue("DisplayName") as string ?? string.Empty;
                                    info.DisplayIcon = sub.GetValue("DisplayIcon") as string ?? string.Empty;
                                    info.UninstallString = sub.GetValue("UninstallString") as string ?? string.Empty;
                                    info.QuietUninstallString = sub.GetValue("QuietUninstallString") as string ?? string.Empty;
                                    info.InstallLocation = sub.GetValue("InstallLocation") as string ?? string.Empty;
                                    info.BundleCachePath = sub.GetValue("BundleCachePath") as string ?? string.Empty;
                                    info.Publisher = sub.GetValue("Publisher") as string ?? string.Empty;
                                    info.URLInfoAbout = sub.GetValue("URLInfoAbout") as string ?? string.Empty;
                                }
                            }
                            catch (Exception ex)
                            {
                                if (writable)
                                    LogAndCountFail("  Failed to read uninstall key " + name + ": " + ex.Message);
                                else
                                    Log("  Warning (ignored): failed to read uninstall key " + name + ": " + ex.Message);
                                continue;
                            }
                            try
                            {
                                info.PathForHeuristic = (info.InstallLocation + "|" + info.DisplayIcon + "|" + info.UninstallString + "|" + info.QuietUninstallString + "|" + info.BundleCachePath);
                                action(info, root);
                            }
                            catch (Exception ex)
                            {
                                if (writable)
                                    LogAndCountFail("  Failed to process uninstall key " + info.KeyName + ": " + ex.Message);
                                else
                                    Log("  Warning (ignored): failed to process uninstall key " + info.KeyName + ": " + ex.Message);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (writable)
                        LogAndCountFail("  Failed to scan uninstall keys (" + hive + ", " + view + "): " + ex.Message);
                    else
                        Log("  Warning (ignored): failed to scan uninstall keys (" + hive + ", " + view + "): " + ex.Message);
                }
            }
        }
    }


    static List<string> ResolveVariantLabelsFromRegistry()
    {
        List<string> labels = new List<string>();
        try
        {
            ForEachDshUninstallEntry(false, (info, root) =>
            {
                // Same two predicates as FindDshInstallDirsFromRegistry so a
                // key that can be deleted is also recognized as a variant label.
                if (!IsDshUninstallEntry(info.DisplayName, info.DisplayIcon, info.UninstallString, info.QuietUninstallString, info.InstallLocation, info.BundleCachePath, info.Publisher, info.URLInfoAbout)
                    && !MatchesKnownAppId(info.KeyName)) return;
                string label = ResolveVariantLabelFromRegistryEntry(info.KeyName, info.DisplayName, info.Publisher, info.URLInfoAbout, info.PathForHeuristic);
                if (!string.IsNullOrEmpty(label) && !labels.Contains(label, StringComparer.OrdinalIgnoreCase)) labels.Add(label);
            });
        }
        catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }
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

    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustData { public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice; public uint fdwRevocationChecks; public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference; public uint dwProvFlags; public uint dwUIContext; }
    [StructLayout(LayoutKind.Sequential)]
    struct WinTrustFileInfo { public uint cbStruct; public IntPtr pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, ref WinTrustData pWVTData);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetFileVersionInfoSize(string lptstrFilename, out uint lpdwHandle);
    static readonly Guid WinTrustActionGenericVerifyV2 = new Guid("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

    // Strong evidence for /InstallDir: at least one executable in the directory
    // must carry a valid Authenticode signature (verified via WinVerifyTrust),
    // or the directory must already be bound to a known DSH uninstall registry
    // entry pointing at this exact path. File-name heuristics alone are NOT
    // enough to authorize recursive deletion.
    static bool IsStrongInstallDirEvidence(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
        try
        {
            foreach (string exe in Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
            {
                if (HasAuthenticodeSignature(exe)) return true;
            }
        }
        catch (Exception ex) { Log("  Warning in IsStrongInstallDirEvidence (scan exe): " + ex.Message); }
        try
        {
            string full = Path.GetFullPath(dir).TrimEnd('\\');
            foreach (string reg in FindDshInstallDirsFromRegistry())
            {
                if (!string.IsNullOrEmpty(reg) && Path.GetFullPath(reg).TrimEnd('\\').Equals(full, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception ex) { Log("  Warning in IsStrongInstallDirEvidence (registry bind): " + ex.Message); }
        return false;
    }

    static bool HasAuthenticodeSignature(string file)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return false;
        try
        {
            WinTrustFileInfo fi = new WinTrustFileInfo();
            fi.cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo));
            IntPtr pPath = Marshal.StringToCoTaskMemUni(file);
            fi.pcwszFilePath = pPath;
            WinTrustData data = new WinTrustData();
            data.cbStruct = (uint)Marshal.SizeOf(typeof(WinTrustData));
            data.dwUIChoice = 2; // WTD_UI_NONE
            data.fdwRevocationChecks = 0;
            data.dwUnionChoice = 1; // WTD_CHOICE_FILE
            data.pFile = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            Marshal.StructureToPtr(fi, data.pFile, false);
            data.dwStateAction = 1; // WTD_STATEACTION_VERIFY
            data.dwProvFlags = 0x10; // WTD_CACHE_ONLY_URL_RETRIEVAL
            uint result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);
            uint closing = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, ref data);
            Marshal.FreeHGlobal(data.pFile);
            Marshal.FreeCoTaskMem(pPath);
            return result == 0;
        }
        catch (Exception ex)
        {
            Log("  Warning in HasAuthenticodeSignature (" + file + "): " + ex.Message);
            return false;
        }
    }

    static bool HasDshExecutable(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        string[] names = VariantCatalog.AllExeNames;
        foreach (string name in names)
        {
            if (File.Exists(Path.Combine(dir, name))) return true;
        }
        return false;
    }

static bool HasDshSignature(string dir)
{
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

    bool asarPresent = File.Exists(Path.Combine(dir, "resources", "app.asar"))
        || Directory.Exists(Path.Combine(dir, "resources", "app"))
        || Directory.Exists(Path.Combine(dir, "resources", "app.asar.unpacked"));
    bool dirNameDsh = IsDshRelatedName(Path.GetFileName(dir.TrimEnd('\\')));
    if (asarPresent && dirNameDsh) return true;

    string packageJson = Path.Combine(dir, "package.json");
    if (asarPresent && File.Exists(packageJson) && PackageJsonLooksDsh(packageJson)) return true;

    string appPackage = Path.Combine(dir, "resources", "app", "package.json");
    if (asarPresent && File.Exists(appPackage) && PackageJsonLooksDsh(appPackage)) return true;


    return false;
}

static bool PackageJsonLooksDsh(string file)
{
    try
    {
        string text = File.ReadAllText(file);
        System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
        object obj = ser.DeserializeObject(text);
        Dictionary<string, object> dict = obj as Dictionary<string, object>;
        if (dict != null)
        {
            if (dict.ContainsKey("dsh")) return true;
            string name = dict.ContainsKey("name") ? (dict["name"] as string ?? string.Empty) : string.Empty;
            string product = dict.ContainsKey("productName") ? (dict["productName"] as string ?? string.Empty) : string.Empty;
            if (ContainsDshWord(name)) return true;
            if (ContainsDshWord(product)) return true;
            return false;
        }
        return false;
    }
    catch (Exception ex)
    {
        Log("  PackageJsonLooksDsh failed for " + file + ": " + ex.Message);
        return false;
    }
}

    static bool ContainsDshWord(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] parts = value.Split(new char[] { '-', '_', ' ', '.', '/', '@' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (part.Equals("dsh", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("deepseek", StringComparison.OrdinalIgnoreCase))
            {
                return true;
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

    // Fast process-name prefilter shared by running-process detection
    // and process cleanup. Keeps MainModule/WMI probes off unrelated
    // One bulk WMI query per process-set snapshot instead of one query per
    // candidate process. Fills the PID caches (thread-safe dictionaries).
    static void RefreshProcessCache()
    {
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath, CommandLine FROM Win32_Process"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    try
                    {
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        string path = mo["ExecutablePath"] as string;
                        string cmd = mo["CommandLine"] as string;
                        KeyValuePair<DateTime, string> pv = new KeyValuePair<DateTime, string>(DateTime.MinValue, string.IsNullOrEmpty(path) ? string.Empty : path);
                        KeyValuePair<DateTime, string> cv = new KeyValuePair<DateTime, string>(DateTime.MinValue, string.IsNullOrEmpty(cmd) ? string.Empty : cmd);
                        CachedProcessPaths[pid] = pv;
                        CachedProcessCommandLines[pid] = cv;
                    }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception ex)
        {
            Log("  Warning: bulk process WMI query failed: " + ex.Message);
        }
    }


    static bool MightBeDshProcess(Process p)
    {
        return NameMatcher.EqualsToken(p.ProcessName, VariantCatalog.AllProcessNames)
            || p.ProcessName.Equals("wscript", StringComparison.OrdinalIgnoreCase)
            || p.ProcessName.Equals("wscript.exe", StringComparison.OrdinalIgnoreCase);
    }

    static DateTime GetProcessStartTime(Process p)
    {
        try { return p.StartTime; }
        catch (Exception) { return DateTime.MinValue; }
    }

    static string GetProcessCommandLine(Process p)
    {
        // Prefer the bulk snapshot cache (key DateTime.MinValue means fresh snapshot).
        int pid = p.Id;
        KeyValuePair<DateTime, string> cachedCmd;
        if (CachedProcessCommandLines.TryGetValue(pid, out cachedCmd))
        {
            if (cachedCmd.Key == DateTime.MinValue) return cachedCmd.Value;
            DateTime started = GetProcessStartTime(p);
            if (started != DateTime.MinValue && cachedCmd.Key == started) return cachedCmd.Value;
        }
        return QuerySingleProcessCommandLine(pid);
    }

    static string QuerySingleProcessCommandLine(int pid)
    {
        string result = string.Empty;
        try
        {
            using (System.Management.ManagementObjectSearcher searcher =
                new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
            {
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    string cmd = obj["CommandLine"] as string;
                    result = string.IsNullOrEmpty(cmd) ? string.Empty : cmd;
                    break;
                }
            }
        }
        catch (Exception) { }
        CachedProcessCommandLines[pid] = new KeyValuePair<DateTime, string>(DateTime.MinValue, result);
        return result;
    }

    static string GetProcessExecutablePath(Process p)
    {
        int pid = p.Id;
        KeyValuePair<DateTime, string> cached;
        if (CachedProcessPaths.TryGetValue(pid, out cached))
        {
            if (cached.Key == DateTime.MinValue) return cached.Value;
            DateTime started = GetProcessStartTime(p);
            if (started != DateTime.MinValue && cached.Key == started) return cached.Value;
        }
        try
        {
            string path = p.MainModule.FileName;
            if (!string.IsNullOrEmpty(path)) { CachedProcessPaths[pid] = new KeyValuePair<DateTime, string>(DateTime.MinValue, path); return path; }
        }
        catch (Exception) { }
        return QuerySingleProcessExecutablePath(pid);
    }

    static string QuerySingleProcessExecutablePath(int pid)
    {
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = " + pid))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    string path = mo["ExecutablePath"] as string;
                    if (!string.IsNullOrEmpty(path)) { CachedProcessPaths[pid] = new KeyValuePair<DateTime, string>(DateTime.MinValue, path); return path; }
                }
        }
        }
        catch (Exception) { }
        CachedProcessPaths[pid] = new KeyValuePair<DateTime, string>(DateTime.MinValue, string.Empty);
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
        // A bare dsh/.dsh path segment is only a weak candidate hint;
        // deletion decisions must additionally bind to a known variant,
        // appId or detected install dir. Keep this predicate broad for
        // detection scans, but never use it alone to authorize deletion.
        if (NameMatcher.ContainsToken(path, NameMatcher.PathTokens)) return true;
        return NameMatcher.ContainsPathSegment(path, "dsh", ".dsh");
    }


    static string ResolveDshHome()
    {
        try
        {
            string env = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrWhiteSpace(env))
            {
                string candidate = env.Trim().TrimEnd('\\');
                if (PathSafety.IsUnsafeRootPath(candidate))
                {
                    Log("  WARNING: refusing unsafe DSH_HOME override: " + env);
                }
                else
                {
                    string full = Path.GetFullPath(candidate);
                    bool isSafe = !PathSafety.IsUnsafeRootPath(full) &&
                        (IsDshHomeName(Path.GetFileName(full.TrimEnd('\\'))) ||
                         (Directory.Exists(Path.Combine(full, ".agent-presets")) &&
                          Directory.Exists(Path.Combine(full, "sessions")) &&
                          Directory.Exists(Path.Combine(full, "skills"))));
                    if (isSafe) return full;
                    Log("  WARNING: refusing weak DSH_HOME override (must be .dsh/.dsh-* or contain .agent-presets + sessions + skills): " + env);
                }
            }
        }
        catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
    }
#endregion

}
