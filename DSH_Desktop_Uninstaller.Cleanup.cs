using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using System.Management;

partial class DSHDesktopUninstaller
{

#region Process & File Cleanup
    static void KillDSHProcesses()
    {
        AddStep("[1/9] Stopping DSH Desktop processes...");

        // First pass: try graceful close when a main window exists,
        // otherwise terminate the process directly. Each attempt is logged.
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (IsDshProcess(p))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        bool accepted = p.CloseMainWindow();
                        Log("  Graceful close sent (accepted=" + accepted + "): " + p.ProcessName + " (PID " + p.Id + ")");
                    }
                    else
                    {
                        p.Kill();
                        Log("  Killed (no main window): " + p.ProcessName + " (PID " + p.Id + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                Log("  Close/Kill attempt failed for process: " + ex.Message);
            }
        }

        // Wait up to 3 seconds for graceful shutdown, re-enumerating each time.
        bool allExited = true;
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
                catch (Exception ex)
                {
                    Log("  Process check failed during shutdown wait: " + ex.Message);
                }
            }
            if (!anyAlive) break;
            allExited = false;
            SleepWithUi(300);
        }
        if (allExited)
        {
            Log("  All DSH processes exited gracefully.");
        }
        else
        {
            Log("  Graceful shutdown wait expired; force-killing remaining processes...");
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
            catch (Exception ex)
            {
                Log("  Force kill failed for PID " + p.Id + ": " + ex.Message);
            }
        }

        // Final pass: taskkill /F /T only on actual remaining DSH PIDs so
        // Electron child-process trees are removed as a whole without 44 blind
        // /IM attempts that would each start a taskkill process.
        foreach (Process p in Process.GetProcesses())
        {
            try
            {
                if (IsDshProcess(p))
                {
                    RunTaskKill("/F /T /PID " + p.Id);
                }
            }
            catch (Exception)
            {
                Log("  Warning: non-fatal error ignored.");
            }
        }

        SleepWithUi(500);
    }

    static void RunTaskKill(string arguments)
    {
        RunCommandAndLog("taskkill.exe", arguments, 10000);
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
                catch (Exception)
                {
                Log("  Warning: non-fatal error ignored.");
                }

                foreach (string dir in DshInstallDirs)
                {
                    if (!string.IsNullOrEmpty(dir) &&
                        path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
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
        catch (Exception)
        {
                Log("  Warning: non-fatal error ignored.");
        }
        return false;
    }

    static void DeleteDirectoryWithRetry(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!Directory.Exists(dir)) { Log("  Not found (skip): " + dir); return; }

        for (int i = 0; i < 8; i++)
        {
            try
            {
                int fileCounter = 0;
                bool removed = DeleteDirectorySafe(dir, ref fileCounter);
                if (removed)
                {
                    Log("  Deleted directory: " + dir);
                }
                else
                {
                    Log("  Partially removed (access-denied files remain): " + dir);
                    failureCount++;
                }
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
                    Log("  Retry " + (i + 1) + "/8 for directory: " + dir + " -> " + ex.Message);
                    SleepWithUi(800);
                }
            }
        }
    }

    static bool DeleteDirectorySafe(string path, ref int fileCounter)
    {
        if (!Directory.Exists(path)) return true;

        FileAttributes attr = File.GetAttributes(path);
        if ((attr & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, false);
            return true;
        }

        bool skipped = false;

        foreach (string file in Directory.GetFiles(path))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                fileCounter++;
                if ((fileCounter % 200) == 0) PumpUi();
            }
            catch (UnauthorizedAccessException ex)
            {
                skipped = true;
                Log("  Access denied; skipping file: " + file + " -> " + ex.Message);
            }
        }

        foreach (string sub in Directory.GetDirectories(path))
        {
            if (!DeleteDirectorySafe(sub, ref fileCounter))
            {
                skipped = true;
            }
        }

        try
        {
            Directory.Delete(path, false);
        }
        catch (Exception)
        {
            if (skipped)
            {
                Log("  Directory remains because access-denied files were skipped: " + path);
                return false;
            }
            throw;
        }
        return true;
    }

    static void DeleteFileIfExists(string file, bool logMissing = false)
    {
        if (string.IsNullOrEmpty(file)) return;
        if (!File.Exists(file))
        {
            Log("  Not found (skip): " + file);
            return;
        }

        for (int i = 0; i < 3; i++)
        {
            try
            {
                File.Delete(file);
                Log("  Deleted file: " + file);
                return;
            }
            catch (Exception ex)
            {
                if (i == 2)
                {
                    Log("  Failed to delete file: " + file + " -> " + ex.Message);
                    failureCount++;
                }
                else
                {
                    Log("  Retry " + (i + 1) + "/3 for file: " + file + " -> " + ex.Message);
                    SleepWithUi(500);
                }
            }
        }
    }

    // Collect every cleanup item that is still present after Run() has
    // finished. The result feeds both the human-readable residual table
    // and the optional /JsonReport file.
    static List<string> CollectCleanupResiduals()
    {
        List<string> residual = new List<string>();
        CollectRegistryResiduals(residual);
        CollectPathResiduals(residual);
        CollectStartupResiduals(residual);
        CollectServiceResiduals(residual);
        CollectScheduledTaskResiduals(residual);
        return residual;
    }

    static void CollectRegistryResiduals(List<string> residual)
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
                    using (RegistryKey root = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (root == null) continue;
                        foreach (string name in root.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey sub = root.OpenSubKey(name))
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
                                    bool matched = IsDshUninstallEntry(displayName, displayIcon, uninstallString, quietUninstallString, installLocation, bundleCachePath, publisher, urlInfoAbout) ||
                                                   MatchesKnownAppId(name);
                                    if (matched)
                                    {
                                        residual.Add("Uninstall key (" + hive + ", " + view + "): " + name + " (" + (displayName ?? "") + ")");
                                    }
                                }
                            }
                            catch (Exception)
                            {
                Log("  Warning: non-fatal error ignored.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    residual.Add("Uninstall scan error (" + hive + ", " + view + "): " + ex.Message);
                }
            }
        }
    }

    static void CollectPathResiduals(List<string> residual)
    {
        CheckPathKeyResidual(Registry.LocalMachine, MachineEnvKey, "HKLM PATH", residual);
        CheckPathKeyResidual(Registry.CurrentUser, "Environment", "HKCU PATH", residual);
    }

    static void CheckPathKeyResidual(RegistryKey root, string subKeyName, string scope, List<string> residual)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey(subKeyName, false))
            {
                if (key == null) return;
                string path = (key.GetValue("Path", "") ?? "").ToString();
                foreach (string part in path.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = part.Trim().TrimEnd('\\');
                    if (IsDshPathEntry(trimmed))
                    {
                        residual.Add(scope + " entry: " + part);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            residual.Add(scope + " scan error: " + ex.Message);
        }
    }

    static void CollectStartupResiduals(List<string> residual)
    {
        RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
        string[] subKeys = new string[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" };
        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                foreach (string subKey in subKeys)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (RegistryKey run = baseKey.OpenSubKey(subKey, false))
                        {
                            if (run == null) continue;
                            foreach (string valueName in run.GetValueNames())
                            {
                                string value = (run.GetValue(valueName, "") ?? "").ToString();
                                if (IsTargetedRunEntry(valueName, value))
                                {
                                    residual.Add("Run entry (" + subKey + ", " + hive + ", " + view + "): " + valueName + " = " + value);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        residual.Add("Run scan error (" + subKey + "): " + ex.Message);
                    }
                }
            }
        }
    }

    static void CollectServiceResiduals(List<string> residual)
    {
        try
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, DisplayName FROM Win32_Service"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        string name = (mo["Name"] ?? "").ToString();
                        string display = (mo["DisplayName"] ?? "").ToString();
                        if (IsDshRelatedName(name) || IsDshRelatedName(display))
                        {
                            residual.Add("Service: " + name + " (" + display + ")");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            residual.Add("Service scan error: " + ex.Message);
            failureCount++;
        }
    }

    static void CollectScheduledTaskResiduals(List<string> residual)
    {
        string file = Path.Combine(Path.GetTempPath(), "dsh-uninstaller-schtasks-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv");
        int code = RunCommandAndLog("cmd.exe", "/C schtasks /query /fo csv /nh > \"" + file + "\"", 30000);
        try
        {
            if (code == 0 && File.Exists(file))
            {
                foreach (string line in File.ReadAllLines(file))
                {
                    if (line.IndexOf("DSH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        line.IndexOf("DeepSeek", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        residual.Add("Scheduled task: " + line);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            residual.Add("Scheduled task scan error: " + ex.Message);
            failureCount++;
        }
        try { if (File.Exists(file)) File.Delete(file); } catch (Exception ex) { Log("  Warning: could not delete scheduled-task CSV: " + ex.Message); }
    }
#endregion


#region Registry & PATH Cleanup
    static void DeleteRegistryKeys()
    {
        Log("  Cleaning registry...");

        DeleteMatchingUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry64, "HKLM 64-bit");
        DeleteMatchingUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry32, "HKLM 32-bit");
        DeleteMatchingUninstallKeys(RegistryHive.CurrentUser, RegistryView.Registry64, "HKCU 64-bit");
        DeleteMatchingUninstallKeys(RegistryHive.CurrentUser, RegistryView.Registry32, "HKCU 32-bit");

        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
            {
                string legacyGuid = Path.GetFileName(LegacyUninstallRegKey);
                if (key != null && !string.IsNullOrEmpty(legacyGuid) && key.OpenSubKey(legacyGuid) != null)
                {
                    key.DeleteSubKeyTree(legacyGuid, false);
                    Log("  Deleted legacy HKLM uninstall key: " + LegacyUninstallRegKey);
                }
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to delete legacy HKLM uninstall key: " + ex.Message);
        }

        foreach (string appId in TargetAppIds)
        {
            DeleteRegSubKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + appId, "HKCU notification settings");
            DeleteRegSubKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\PushNotifications\Backup\" + appId, "HKCU push backup");
        }

        // Legacy variable used by some old DSH builds; clean it too.
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

        TryRemoveEnvVar(Registry.CurrentUser, "DSH_HOME");
        TryRemoveEnvVar(Registry.LocalMachine, "DSH_HOME");
    }

    static string NormalizeEnvPath(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string v = value.Trim().Trim('"').TrimEnd('\\');
            return Path.GetFullPath(v);
        }
        catch (Exception)
        {
                Log("  Warning: non-fatal error ignored.");
            return (value ?? string.Empty).Trim().Trim('\u0022').TrimEnd('\u005C');
        }
    }
        static void TryRemoveEnvVar(RegistryKey root, string name)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey("Environment", true))
            {
                if (key == null) return;
                object raw = key.GetValue(name, null);
                if (raw == null)
                {
                    Log("  Environment variable " + name + " not present in " + root.Name + ".");
                    return;
                }
                string val = raw.ToString().Trim().Trim('"');
                string normalizedVal = NormalizeEnvPath(val);
                string normalizedHome = NormalizeEnvPath(DshHome);
                if (!string.IsNullOrEmpty(normalizedHome) && normalizedVal.Equals(normalizedHome, StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue(name, false);
                    Log("  Deleted " + name + " (matched DSH user data path): " + val);
                }
                else
                {
                    Log("  Kept " + name + " (value does not match DSH user data path): " + val);
                }
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to inspect/delete environment variable " + name + " in " + root.Name + ": " + ex.Message);
            failureCount++;
        }
    }

    static bool IsKnownAppId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (string known in VariantCatalog.AllAppIds)
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
        foreach (string known in VariantCatalog.AllAppIds)
        {
            if (id.StartsWith(known + "_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(known + "-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    static bool MatchesTargetAppId(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        foreach (string known in TargetAppIds)
        {
            if (known.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(known + "_", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(known + "-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    static bool HasVariantProfile()
    {
        return (variantExeNames != null && variantExeNames.Length > 0) ||
               (variantProcessNames != null && variantProcessNames.Length > 0) ||
               (variantAppIds != null && variantAppIds.Length > 0) ||
               (variantShortcutNames != null && variantShortcutNames.Length > 0);
    }

    static bool IsTargetedUninstallEntry(string keyName, string displayName, string pathForHeuristic)
    {
        if (!HasVariantProfile()) return true;
        if (MatchesTargetAppId(keyName)) return true;

        bool hasNames = (KnownExeNames != null && KnownExeNames.Length > 0) ||
                        (KnownProcessNames != null && KnownProcessNames.Length > 0);
        if (hasNames)
        {
            if (NameMatcher.ContainsToken(displayName, KnownProcessNames) ||
                NameMatcher.ContainsToken(displayName, KnownExeNames)) return true;
            if (NameMatcher.ContainsToken(pathForHeuristic, KnownProcessNames) ||
                NameMatcher.ContainsToken(pathForHeuristic, KnownExeNames)) return true;
            foreach (string dir in DshInstallDirs)
            {
                if (!string.IsNullOrEmpty(dir) &&
                    pathForHeuristic.IndexOf(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        return IsDshRelatedName(displayName) || IsDshRelatedPath(pathForHeuristic);
    }

    static bool IsTargetedRunEntry(string valueName, string value)
    {
        if (!HasVariantProfile())
        {
            return IsDshRelatedName(valueName) || IsDshRelatedName(value) || IsDshRelatedPath(value);
        }

        bool hasNames = (KnownExeNames != null && KnownExeNames.Length > 0) ||
                        (KnownProcessNames != null && KnownProcessNames.Length > 0);
        if (hasNames)
        {
            if (NameMatcher.ContainsToken(valueName, KnownProcessNames) ||
                NameMatcher.ContainsToken(valueName, KnownExeNames) ||
                NameMatcher.ContainsToken(value, KnownProcessNames) ||
                NameMatcher.ContainsToken(value, KnownExeNames)) return true;
            foreach (string dir in DshInstallDirs)
            {
                if (!string.IsNullOrEmpty(dir) &&
                    value.IndexOf(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        return IsDshRelatedName(valueName) || IsDshRelatedName(value) || IsDshRelatedPath(value);
    }

    static int DeleteMatchingUninstallKeys(RegistryHive hive, RegistryView view, string label)
    {
        int deleted = 0;
        try
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey uninstallRoot = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
            {
                if (uninstallRoot == null) return 0;
                string[] names = uninstallRoot.GetSubKeyNames();
                foreach (string name in names)
                {
                    bool matched = false;
                    string displayName = string.Empty;
                    string displayIcon = string.Empty;
                    string uninstallString = string.Empty;
                    string quietUninstallString = string.Empty;
                    string installLocation = string.Empty;
                    string bundleCachePath = string.Empty;
                    string publisher = string.Empty;
                    string urlInfoAbout = string.Empty;
                    try
                    {
                        using (RegistryKey sub = uninstallRoot.OpenSubKey(name))
                        {
                            if (sub == null) continue;
                            displayName = sub.GetValue("DisplayName") as string;
                            displayIcon = sub.GetValue("DisplayIcon") as string;
                            uninstallString = sub.GetValue("UninstallString") as string;
                            quietUninstallString = sub.GetValue("QuietUninstallString") as string;
                            installLocation = sub.GetValue("InstallLocation") as string;
                            bundleCachePath = sub.GetValue("BundleCachePath") as string;
                            publisher = sub.GetValue("Publisher") as string;
                            urlInfoAbout = sub.GetValue("URLInfoAbout") as string;
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
                        string pathForHeuristic = (installLocation + "|" + displayIcon + "|" + uninstallString + "|" + quietUninstallString + "|" + bundleCachePath);
                        if (!IsTargetedUninstallEntry(name, displayName, pathForHeuristic))
                        {
                            Log("  Skipping other DSH uninstall key (not the detected variant): " + name + " (" + displayName + ")");
                            matched = false;
                        }
                    }

                    if (matched)
                    {
                        try
                        {
                            uninstallRoot.DeleteSubKeyTree(name, false);
                            Log("  Deleted " + label + " uninstall key: " + Path.Combine(uninstallRoot.Name, name));
                            deleted++;
                        }
                        catch (Exception ex)
                        {
                            Log("  Failed to delete " + label + " uninstall key " + name + ": " + ex.Message);
                            failureCount++;
                        }
                    }
                }
                Log("  Registry scan complete (" + label + "): scanned " + names.Length + " keys, deleted " + deleted + ".");
            }
        }
        catch (Exception ex)
        {
            Log("  Failed to scan " + label + " uninstall keys: " + ex.Message);
        }
        return deleted;
    }

    static void DeleteRegSubKey(RegistryKey root, string subKey, string label)
    {
        try
        {
            using (RegistryKey key = root.OpenSubKey(subKey))
            {
                if (key == null) { Log("  Not present (skip): " + label + " -> " + subKey); return; }
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
        Log("  Cleaning machine PATH...");
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
        Log("  Cleaning user PATH...");
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
        Log("  Cleaning Run/RunOnce startup entries...");
        int removedCount = 0;
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
                            if (run == null) { Log("  Startup key not present: " + subKey + " (" + hive + ", " + view + ")"); continue; }
                            foreach (string valueName in run.GetValueNames())
                            {
                                try
                                {
                                    string value = run.GetValue(valueName, "").ToString();
                                    if (IsTargetedRunEntry(valueName, value))
                                    {
                                        run.DeleteValue(valueName, false);
                                        Log("  Deleted startup entry (" + subKey + "): " + valueName + " = " + value);
                                        removedCount++;
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
        Log("  Startup entry cleanup complete: deleted " + removedCount + " entries.");
    }

    static void CleanPathRegistryKey(RegistryKey key, string scope)
    {
        if (key == null) return;
        string path = (key.GetValue("Path", "") ?? "").ToString();
        string shown = path.Length > 500 ? path.Substring(0, 500) + "..." : path;
        Log("  Reading " + scope + " PATH (length=" + path.Length + "): " + shown);
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
            RegistryValueKind kind = RegistryValueKind.String;
            try
            {
                kind = key.GetValueKind("Path");
                if (kind == RegistryValueKind.None || kind == RegistryValueKind.Unknown)
                {
                    kind = RegistryValueKind.String;
                }
            }
            catch (Exception)
            {
                Log("  Warning: non-fatal error ignored.");
                kind = RegistryValueKind.String;
            }
            try
            {
                key.SetValue("Path", string.Join(";", kept.ToArray()), kind);
                Log("  Updated " + scope + " PATH: removed " + (parts.Length - kept.Count) + " DSH entries, kept " + kept.Count + " entries.");
            }
            catch (Exception ex)
            {
                Log("  Failed to write " + scope + " PATH: " + ex.Message);
                failureCount++;
            }
        }
        else
        {
            Log("  No DSH entries found in " + scope + " PATH.");
        }
    }
    static bool IsDshPathEntry(string trimmed)
    {
        if (string.IsNullOrEmpty(trimmed)) return false;

        // PATH entries may contain %USERPROFILE% etc.; expand before
        // comparing so REG_EXPAND_SZ entries are matched too.
        string expanded = trimmed;
        try { expanded = Environment.ExpandEnvironmentVariables(trimmed); } catch { }

        if (!string.IsNullOrEmpty(DshRuntime))
        {
            if (expanded.Equals(Path.Combine(DshRuntime, "node"), StringComparison.OrdinalIgnoreCase)) return true;
        }
        if (!string.IsNullOrEmpty(DshHome))
        {
            if (expanded.Equals(Path.Combine(DshHome, "bin"), StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (string dir in DshInstallDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            if (expanded.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        // With a narrowed variant profile, broad name heuristics are safe.
        if (HasVariantProfile())
        {
            return IsDshRelatedPath(trimmed) || IsDshRelatedPath(expanded);
        }

        // No profile (generic detection): a bare "dsh" path segment is only
        // logged as a hint, never used as the sole reason to delete a PATH
        // entry. Only entries tied to the detected install/runtime/home are
        // removed; everything else is kept.
        if (NameMatcher.ContainsPathSegment(trimmed, "dsh", ".dsh")
            || NameMatcher.ContainsPathSegment(expanded, "dsh", ".dsh"))
        {
            Log("  PATH entry matches a dsh segment but is not tied to the detected install; keeping: " + trimmed);
        }
        return false;
    }
#endregion

}
