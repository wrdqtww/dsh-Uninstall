using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

partial class DSHDesktopUninstaller
{

#region Process & File Cleanup
    static void KillDSHProcesses()
    {
        Log("[1/9] Stopping DSH Desktop processes...");

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
            Thread.Sleep(300);
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

        // Final pass: use taskkill /F /T so Electron child-process trees are
        // removed as a whole (main process alone may leave renderer/gpu children).
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
                    Log("  Retry " + (i + 1) + "/8 for directory: " + dir + " -> " + ex.Message);
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

    static void DeleteFileIfExists(string file, bool logMissing = false)
    {
        if (string.IsNullOrEmpty(file)) return;
        if (!File.Exists(file))
        {
            if (logMissing) Log("  Not found (skip): " + file);
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
                    Thread.Sleep(500);
                }
            }
        }
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
        catch
        {
            return value;
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
        return variantExeNames != null || variantAppIds != null;
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
            if (!string.IsNullOrEmpty(DshInstallDir) &&
                pathForHeuristic.IndexOf(DshInstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) >= 0) return true;
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
            if (!string.IsNullOrEmpty(DshInstallDir) &&
                value.IndexOf(DshInstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) >= 0) return true;
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
                            if (run == null) continue;
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
            catch
            {
                kind = RegistryValueKind.String;
            }
            key.SetValue("Path", string.Join(";", kept.ToArray()), kind);
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
        if (!string.IsNullOrEmpty(DshInstallDir) &&
            (expanded.StartsWith(DshInstallDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith(DshInstallDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) return true;
        // Broader heuristic for variants not installed in the detected dir.
        return IsDshRelatedPath(trimmed) || IsDshRelatedPath(expanded);
    }
#endregion

}
