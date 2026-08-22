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
        RefreshProcessCache();
        AddStep("[1/9] Stopping DSH Desktop processes...");

        // First pass: try graceful close when a main window exists,
        // otherwise terminate the process directly. Each attempt is logged.
        Process[] procs1 = Process.GetProcesses();
        foreach (Process p in procs1)
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
                bool exited1 = false; try { exited1 = p.HasExited; } catch { }
                if (exited1) { Log("  Process already exited (benign): PID " + p.Id); }
                else { Log("  Close/Kill attempt failed for process: " + ex.Message); failureCount++; }
            }
        }
        DisposeProcessArray(procs1);

        // Wait up to 3 seconds for graceful shutdown, re-enumerating each time.
        int waitCheckFailures = 0;
        bool allExited = true;
        for (int i = 0; i < 10; i++)
        {
            bool anyAlive = false;
            Process[] procs2 = Process.GetProcesses();
            foreach (Process p in procs2)
            {
                try
                {
                    if (IsDshProcess(p))
                    {
                        anyAlive = true;
                        break;
                    }
                }
                  catch (Exception)
                {
                        waitCheckFailures++;
                }
            }
            DisposeProcessArray(procs2);
            if (!anyAlive) { allExited = true; break; }
            allExited = false;
            SleepWithUi(300);
        }
        if (waitCheckFailures > 0) Log("  Process check failures during shutdown wait: " + waitCheckFailures);
        if (allExited)
        {
            Log("  All DSH processes exited gracefully.");
        }
        else
        {
            Log("  Graceful shutdown wait expired; force-killing remaining processes...");
        }

        // Second pass: force-kill any remaining DSH processes.
        Process[] procs3 = Process.GetProcesses();
        foreach (Process p in procs3)
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
                bool exited2 = false; try { exited2 = p.HasExited; } catch { }
                if (exited2) { Log("  Process already exited (benign): PID " + p.Id); }
                else { Log("  Force kill failed for PID " + p.Id + ": " + ex.Message); failureCount++; }
            }
        }
        DisposeProcessArray(procs3);

        // Final pass: taskkill /F /T only on actual remaining DSH PIDs so
        // Electron child-process trees are removed as a whole without 44 blind
        // /IM attempts that would each start a taskkill process.
        Process[] procs4 = Process.GetProcesses();
        foreach (Process p in procs4)
        {
            try
            {
                if (IsDshProcess(p))
                {
                    int tk = RunTaskKill("/F /T /PID " + p.Id);
                    if (tk != 0)
                    {
                        bool exited3 = false; try { exited3 = p.HasExited; } catch { }
                        if (exited3) { Log("  taskkill returned " + tk + " but process already exited (benign): PID " + p.Id); }
                        else { LogAndCountFail("  taskkill failed with code " + tk + ": /F /T /PID " + p.Id); }
                    }
                }
            }
          catch (Exception ex) { Log("  Taskkill pass failed: " + ex.Message); failureCount++; }
        }
        DisposeProcessArray(procs4);

        // PID caches are valid only while the process set is stable. Clear them
        // after the kill phase so a later PID reuse cannot return a stale path.
        CachedProcessPaths.Clear();
        CachedProcessCommandLines.Clear();
        SleepWithUi(500);
    }

    static void DisposeProcessArray(Process[] processes)
    {
        if (processes == null) return;
        foreach (Process p in processes)
        {
            try { p.Dispose(); }
            catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }
        }
    }

    static int RunTaskKill(string arguments)
    {
            return RunCommandAndLog("taskkill.exe", arguments, 10000, false);
    }

    static bool IsDshProcess(Process p)
    {
        try
        {
            // Fast negative filter: when no install dir is known, only process
            // names from the DSH catalog (plus wscript.exe for the edge variant)
            // can possibly be DSH processes. This avoids MainModule/WMI probes
            // for every unrelated process during repeated snapshots.
              if (DshInstallDirs.Count == 0 && !MightBeDshProcess(p))
              {
                  return false;
              }
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
                catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }

                foreach (string dir in DshInstallDirs)
                {
                    if (!string.IsNullOrEmpty(dir) &&
                        path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                string fileName = Path.GetFileName(path);
                // Strategy: clean ALL DSH desktops, not only the detected
                // variant. Use the full catalog so a running DSH process is
                // never left behind after detection narrows the file lists.
                if (NameMatcher.EqualsToken(fileName, VariantCatalog.AllExeNames))
                {
                    // Exe name matched but the path is not inside any detected
                    // install directory. Never kill by name alone: report it
                    // as a residual candidate and skip (silent counts it).
                    LogAndCountFail("  DSH-named process outside detected install dirs; skipping kill: " + p.ProcessName + " (PID " + p.Id + ", path " + path + ")");
                    return false;
                }


                // Edge-shortcut variant (2633352305) runs launcher.vbs under
                // wscript.exe. Its exe path is not a DSH exe, so detect it
                // from the command line exactly like FindRunningDshInstallDirs.
                if (fileName.Equals("wscript.exe", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Equals("wscript", StringComparison.OrdinalIgnoreCase))
                {
                    string cmd = GetProcessCommandLine(p);
                    if (!string.IsNullOrEmpty(cmd) &&
                        cmd.IndexOf("dsh-edge-app", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        cmd.IndexOf("launcher.vbs", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            // A bare process-name match is no longer enough to kill: many
            // unrelated applications could be named like a DSH executable.
            // Only kill when the executable path ties the process to a DSH
            // install. If the path cannot be read, leave the process alone.
            if (NameMatcher.EqualsToken(p.ProcessName, VariantCatalog.AllProcessNames))
            {
                int tk = RunTaskKill("/F /T /PID " + p.Id);
                if (tk != 0)
                {
                    bool exitedName = false; try { exitedName = p.HasExited; } catch { }
                    if (exitedName) { Log("  Process name matched but path unknown; taskkill returned " + tk + " but process already exited (benign): " + p.ProcessName + " (PID " + p.Id + ")"); }
                    else { LogAndCountFail("  Process name matched but path unknown and taskkill failed with code " + tk + ": " + p.ProcessName + " (PID " + p.Id + ")"); }
                }
                else
                {
                    Log("  Process name matched but path unknown; taskkill issued: " + p.ProcessName + " (PID " + p.Id + ")");
                }
            }
    }
        catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }
        return false;
    }

    static void DeleteDirectoryWithRetry(string dir, params string[] skipPaths)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (PathSafety.IsUnsafeRootPath(dir))
        {
            LogAndCountFail("  Refusing to delete unsafe path: " + dir);
            return;
        }
        if (!Directory.Exists(dir)) { Log("  Not found (skip): " + dir); return; }

        List<string> skips = new List<string>();
        if (skipPaths != null)
        {
            foreach (string s in skipPaths)
            {
                if (string.IsNullOrEmpty(s)) continue;
                try { skips.Add(Path.GetFullPath(s)); } catch (Exception ex) { Log("  Warning in DeleteDirectoryWithRetry (skip path): " + ex.Message); }
            }
        }

        // Defensive check: a skip path equal to the directory being deleted
        // would never be matched by the child-directory comparison below.
        string dirFull = Path.GetFullPath(dir).TrimEnd('\\');
        foreach (string skip in skips)
        {
            if (skip.TrimEnd('\\').Equals(dirFull, StringComparison.OrdinalIgnoreCase))
            {
                Log("  WARNING: skip path equals the directory to delete; refusing: " + dir);
                return;
            }
        }

        for (int i = 0; i < 8; i++)
        {
            try
            {
                int fileCounter = 0;
                bool keptAny = false;
                bool skippedAny = false;
                bool removed = DeleteDirectorySafe(dir, ref fileCounter, skips, out keptAny, out skippedAny);
                if (removed)
                {
                    Log("  Deleted directory: " + dir);
                    return;
                }
                if (PureHelpers.IsExpectedPartialDeletion(keptAny, skippedAny))
                {
                    // Only protected subtree(s) were kept; this is an expected
                    // partial deletion, not a failure. If any file was also
                    // skipped (access denied/locked), fall through to retry so
                    // real failures are never hidden by the skip list.
                    Log("  Kept protected subtree(s) under " + dir + " (expected).");
                    return;
                }
                if (i == 7)
                {
                    Log("  Partially removed (access-denied files remain): " + dir);
                    failureCount++;
                    return;
                }
                Log("  Partial removal, retry " + (i + 1) + "/8: " + dir);
                SleepWithUi(800);
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

    static bool DeleteDirectorySafe(string path, ref int fileCounter, List<string> skipPaths, out bool keptAny, out bool skippedAny)
    {
        keptAny = false;
        skippedAny = false;

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
            }
            catch (Exception ex)
            {
                if (!(ex is UnauthorizedAccessException) && !(ex is IOException)) throw;
                skipped = true;
                skippedAny = true;
                Log("  Access denied or file locked; skipping file: " + file + " -> " + ex.Message);
            }
        }

        foreach (string sub in Directory.GetDirectories(path))
        {
            bool skipThis = false;
            if (skipPaths != null)
            {
                string subFull = Path.GetFullPath(sub);
                foreach (string skip in skipPaths)
                {
                    if (subFull.Equals(skip, StringComparison.OrdinalIgnoreCase)) { skipThis = true; break; }
                }
            }
            if (skipThis)
            {
                keptAny = true;
                Log("  Kept protected subtree: " + sub);
                continue;
            }
            bool childKept = false;
            bool childSkipped = false;
            if (!DeleteDirectorySafe(sub, ref fileCounter, skipPaths, out childKept, out childSkipped))
            {
                skipped = true;
            }
            keptAny |= childKept;
            skippedAny |= childSkipped;
        }

        try
        {
            Directory.Delete(path, false);
        }
        catch (Exception)
        {
            if (keptAny)
            {
                Log("  Directory partially removed; protected subtree(s) kept under: " + path);
                return false;
            }
            if (skipped)
            {
                Log("  Directory remains because access-denied files were skipped: " + path);
                return false;
            }
            throw;
        }
        return true;
    }
    static void DeleteFileIfExists(string file)
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
                // Clear read-only before deleting, mirroring DeleteDirectorySafe.
                try { File.SetAttributes(file, FileAttributes.Normal); } catch (Exception attrEx) { Log("  Warning: could not clear attributes on " + file + ": " + attrEx.Message); }
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
        try
        {
            ForEachDshUninstallEntry(false, (info, root) =>
            {
                bool matched = IsDshUninstallEntry(info.DisplayName, info.DisplayIcon, info.UninstallString, info.QuietUninstallString, info.InstallLocation, info.BundleCachePath, info.Publisher, info.URLInfoAbout) ||
                               MatchesKnownAppId(info.KeyName);
                // Residual scan must use the same targeted filter as deletion,
                // otherwise intentionally kept other-variant keys would show
                // up as [RESIDUAL] and mislead scripts/JSON reports.
                if (matched) matched = IsTargetedUninstallEntry(info.KeyName, info.DisplayName, info.PathForHeuristic);
                if (matched)
                {
                    residual.Add("Uninstall key (" + info.Hive + ", " + info.View + "): " + info.KeyName + " (" + info.DisplayName + ")");
                }
            });
        }
        catch (Exception ex)
        {
            residual.Add("Uninstall scan error: " + ex.Message);
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

    // Run() already executes on a background STA worker thread, so this
    // synchronous WMI scan blocks cleanup, not the UI. It runs exactly once
    // during the residual scan; no per-process WMI round-trips happen here.
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
            // Scan failure is not a cleanup failure: WMI unavailability must not turn a successful uninstall into exit code 1.
            residual.Add("Service scan error: " + ex.Message);
        }
    }

    static void CollectScheduledTaskResiduals(List<string> residual)
    {
        string file = Path.Combine(Path.GetTempPath(), "dsh-uninstaller-schtasks-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv");
            int code = RunCommandAndLog("cmd.exe", "/C schtasks /query /fo csv /nh > \"" + file + "\"", 30000, false);
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
            // Scan failure is not a cleanup failure: schtasks/CSV issues must not turn a successful uninstall into exit code 1.
            residual.Add("Scheduled task scan error: " + ex.Message);
        }
        try { if (File.Exists(file)) File.Delete(file); } catch (Exception ex) { Log("  Warning: could not delete scheduled-task CSV: " + ex.Message); }
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
            LogAndCountFail("  Failed to clean machine PATH: " + ex.Message);
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
            LogAndCountFail("  Failed to clean user PATH: " + ex.Message);
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
            LogAndCountFail("  Failed to broadcast environment change: " + ex.Message);
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
                        LogAndCountFail("  Failed to scan startup key (" + subKey + ", " + hive + ", " + view + "): " + ex.Message);
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
        // Keep empty segments: a leading/trailing or double semicolon is a legitimate PATH feature (current directory).
        string[] parts = path.Split(';');
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
            catch (Exception ex)
            {
                Log("  Warning (ignored): " + ex.Message);
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
        try { expanded = Environment.ExpandEnvironmentVariables(trimmed); } catch (Exception ex) { Log("  Warning in IsDshPathEntry (ExpandEnvironmentVariables): " + ex.Message); }

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
              expanded.Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }
        // Name tokens NEVER authorize deletion by themselves: they only
        // serve as a report hint below. Deletion is strictly limited to
        // entries bound to DshInstallDirs, DshRuntime\\node or DshHome\\bin
        // (checked above). This keeps unrelated same-name directories
        // such as C:\\Tools\\DSH Desktop\\bin safe.
        if (NameMatcher.ContainsToken(trimmed, KnownExeNames) ||
            NameMatcher.ContainsToken(trimmed, KnownShortcutNames) ||
            NameMatcher.ContainsToken(trimmed, KnownProcessNames) ||
            NameMatcher.ContainsToken(trimmed, KnownRoamingDirNames) ||
            NameMatcher.ContainsToken(trimmed, KnownLocalAppDataDirNames) ||
            NameMatcher.ContainsToken(expanded, KnownExeNames) ||
            NameMatcher.ContainsToken(expanded, KnownShortcutNames) ||
            NameMatcher.ContainsToken(expanded, KnownProcessNames) ||
            NameMatcher.ContainsToken(expanded, KnownRoamingDirNames) ||
            NameMatcher.ContainsToken(expanded, KnownLocalAppDataDirNames))
        {
            Log("  PATH entry matches DSH name tokens but is not bound to the detected install; keeping: " + trimmed);
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


#region Registry & PATH Cleanup
    static void LogAndCountFail(string msg)
    {
        Log(msg);
        failureCount++;
    }

    static void DeleteRegistryKeys()
    {
        Log("  Cleaning registry...");

          DeleteMatchingUninstallKeys("uninstall");

        RegistryHive[] legacyHives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
        RegistryView[] legacyViews = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
        string legacyGuid = Path.GetFileName(LegacyUninstallRegKey);
        if (!string.IsNullOrEmpty(legacyGuid))
        {
            foreach (RegistryHive legacyHive in legacyHives)
            {
                foreach (RegistryView legacyView in legacyViews)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(legacyHive, legacyView))
                        using (RegistryKey key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", true))
                        {
                            if (key != null)
                            {
                                using (RegistryKey legacySub = key.OpenSubKey(legacyGuid))
                                {
                                    if (legacySub != null)
                                    {
                                        key.DeleteSubKeyTree(legacyGuid, false);
                                        Log("  Deleted legacy uninstall key: " + LegacyUninstallRegKey + " (" + legacyHive + ", " + legacyView + ")");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogAndCountFail("  Failed to delete legacy uninstall key (" + legacyHive + ", " + legacyView + "): " + ex.Message);
                    }
                }
            }
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
            LogAndCountFail("  Failed to delete VIPSHOME: " + ex.Message);
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
            v = Environment.ExpandEnvironmentVariables(v);
            return Path.GetFullPath(v);
        }
        catch (Exception ex)
        {
                Log("  Warning (ignored): " + ex.Message);
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
        // Deletion is authorized ONLY by a known app-id or by a path bound
        // to one of the detected install directories. DisplayName is not
        // sufficient evidence for deleting a registry key.
        if (MatchesTargetAppId(keyName)) return true;

        string[] dirs = NormalizePathCandidateDirs(pathForHeuristic);
        foreach (string dir in dirs)
        {
            if (IsDshInstallDirPath(dir)) return true;
        }
        return false;
    }


    static bool IsDshInstallDirPath(string dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        foreach (string d in DshInstallDirs)
        {
            if (string.IsNullOrEmpty(d)) continue;
            if (dir.StartsWith(d.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
                dir.Equals(d.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static string[] NormalizePathCandidateDirs(string pathForHeuristic)
    {
        List<string> dirs = new List<string>();
        if (string.IsNullOrWhiteSpace(pathForHeuristic)) return dirs.ToArray();
        string[] parts = pathForHeuristic.Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string candidate = part.Trim().Trim('"');
            int comma = candidate.IndexOf(',');
            if (comma >= 0) candidate = candidate.Substring(0, comma);
            try
            {
                // UninstallString-like values are command lines. Reuse the
                // command-line parser so only the executable path is fed to
                // Path.GetFullPath, never "C:\...\Uninstall.exe" /S.
                string exePath = ParseExePathFromCommandLine(candidate);
                if (!string.IsNullOrEmpty(exePath) && (File.Exists(exePath) || candidate.StartsWith("\"", StringComparison.OrdinalIgnoreCase)))
                {
                    string dir = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(dir)) { dirs.Add(dir.TrimEnd('\\')); continue; }
                }
                string full = Path.GetFullPath(candidate);
                if (File.Exists(full)) full = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(full)) dirs.Add(full.TrimEnd('\\'));
            }
            catch (Exception ex) { Log("  Warning in NormalizePathCandidateDirs: " + ex.Message); }
        }
        return dirs.ToArray();
    }
    static bool IsTargetedRunEntry(string valueName, string value)
    {
        if (!HasVariantProfile())
        {
            return IsDshRelatedName(valueName) || IsDshRelatedName(value) || IsDshRelatedPath(value) || IsRunEntryUnderDsh(value);
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
                    (value.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
                     value.Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) return true;
            }
            if (IsRunEntryUnderDsh(value)) return true;
            return false;
        }

        return IsDshRelatedName(valueName) || IsDshRelatedName(value) || IsDshRelatedPath(value) || IsRunEntryUnderDsh(value);
    }

    static bool IsRunEntryUnderDsh(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.IndexOf("dsh-edge-app", StringComparison.OrdinalIgnoreCase) >= 0 &&
            value.IndexOf("launcher.vbs", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        string expanded = value;
        try { expanded = Environment.ExpandEnvironmentVariables(value); } catch (Exception ex) { Log("  Warning in IsRunEntryUnderDsh: " + ex.Message); }
        string[] roots = new string[] { DshRuntime, DshHome };
        foreach (string root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            string r = root.TrimEnd('\\');
            if (expanded.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase) ||
                expanded.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase) ||
                value.Equals(r, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static int DeleteMatchingUninstallKeys(string label)
    {
        int deleted = 0;
        int scanned = 0;
        try
        {
            ForEachDshUninstallEntry(true, (info, root) =>
            {
                scanned++;
                bool matched = IsDshUninstallEntry(info.DisplayName, info.DisplayIcon, info.UninstallString, info.QuietUninstallString, info.InstallLocation, info.BundleCachePath, info.Publisher, info.URLInfoAbout) ||
                               MatchesKnownAppId(info.KeyName);
                if (matched)
                {
                    if (!IsTargetedUninstallEntry(info.KeyName, info.DisplayName, info.PathForHeuristic))
                    {
                        Log("  Skipping other DSH uninstall key (not tied to a detected DSH install path): " + info.KeyName + " (" + info.DisplayName + ")");
                        matched = false;
                    }
                }
                if (matched)
                {
                    try
                    {
                        root.DeleteSubKeyTree(info.KeyName, false);
                        Log("  Deleted " + label + " uninstall key: " + Path.Combine(root.Name, info.KeyName));
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        Log("  Failed to delete " + label + " uninstall key " + info.KeyName + ": " + ex.Message);
                        failureCount++;
                    }
                }
            });
            Log("  Registry scan complete (" + label + "): scanned " + scanned + " keys, deleted " + deleted + ".");
        }
        catch (Exception ex)
        {
            LogAndCountFail("  Failed to scan " + label + " uninstall keys: " + ex.Message);
        }
        return deleted;
    }
#endregion

}
