using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

partial class DSHDesktopUninstaller
{

#region Fields, Constants & Paths
    static bool silent = false;
    static bool dryRun = false;
    static bool helpRequested = false;
    static string manualInstallDir = string.Empty;
    static ConfirmForm progressForm = null;
    static RetentionOptions retentionOptions = new RetentionOptions();

    // Keep-* flags are aliases for retentionOptions so the cleanup code keeps
    // its current shape while the CLI/GUI fill a single object.
    static bool keepAgentPresets { get { return retentionOptions.Presets; } set { retentionOptions.Presets = value; } }
    static bool keepRuntime { get { return retentionOptions.Runtime; } set { retentionOptions.Runtime = value; } }
    static bool keepAppSettings { get { return retentionOptions.AppSettings; } set { retentionOptions.AppSettings = value; } }
    static bool keepModelConfig { get { return retentionOptions.ModelConfig; } set { retentionOptions.ModelConfig = value; } }
    static bool keepOtherUserData { get { return retentionOptions.OtherUserData; } set { retentionOptions.OtherUserData = value; } }
    static bool keepChatData { get { return retentionOptions.ChatData; } set { retentionOptions.ChatData = value; } }
    static bool keepPlugins { get { return retentionOptions.Plugins; } set { retentionOptions.Plugins = value; } }
    static bool keepSkills { get { return retentionOptions.Skills; } set { retentionOptions.Skills = value; } }
    static List<string> keepPresetNames { get { return retentionOptions.PresetNames; } set { retentionOptions.PresetNames.Clear(); retentionOptions.PresetNames.AddRange(value); } }
    static List<string> keepPluginNames { get { return retentionOptions.PluginNames; } set { retentionOptions.PluginNames.Clear(); retentionOptions.PluginNames.AddRange(value); } }
    static List<string> keepSkillNames { get { return retentionOptions.SkillNames; } set { retentionOptions.SkillNames.Clear(); retentionOptions.SkillNames.AddRange(value); } }

    // Multi-variant support: official DSH Desktop, collection/integrated
    // builds (DeepSeek Harness Desktop, dsh-desktop), and lite/simple
    // variants (deepseek-harness, dsh-edge-app, DSHDesktop, dshdesktop).
    // All-variant name lists are generated from VariantCatalog.Profiles so a
    // new variant is added in exactly one place (DSH_Desktop_Uninstaller.Core.cs).
    static string[] AllExeNames { get { return VariantCatalog.AllExeNames; } }
    static string[] AllProcessNames { get { return VariantCatalog.AllProcessNames; } }
    static string[] AllShortcutNames { get { return VariantCatalog.AllShortcutNames; } }
    static string[] AllRoamingDirNames { get { return VariantCatalog.AllRoamingDirNames; } }
    static string[] AllLocalAppDataDirNames { get { return VariantCatalog.AllLocalAppDataDirNames; } }
    static readonly string[] AllUpdaterDirNames = new string[]
    {
        "dsh-desktop-updater",
        "dsh-launcher-updater",
        "dsh-updater"
    };

    // When a specific variant repo is recognized, these override the broad
    // "all variants" lists so cleanup targets that variant's known names only.
    static string[] variantExeNames = null;
    static string[] variantProcessNames = null;
    static string[] variantShortcutNames = null;
    static string[] variantUpdaterDirNames = null;
    static string[] variantRoamingDirNames = null;
    static string[] variantLocalAppDataDirNames = null;
    static string[] variantAppIds = null;
    static string[] variantInstallDirNames = null;

    static string[] KnownExeNames { get { return variantExeNames ?? AllExeNames; } }
    static string[] KnownProcessNames { get { return variantProcessNames ?? AllProcessNames; } }
    static string[] KnownShortcutNames { get { return variantShortcutNames ?? AllShortcutNames; } }
    static string[] KnownUpdaterDirNames { get { return variantUpdaterDirNames ?? AllUpdaterDirNames; } }
    static string[] KnownRoamingDirNames { get { return variantRoamingDirNames ?? AllRoamingDirNames; } }
    static string[] KnownLocalAppDataDirNames { get { return variantLocalAppDataDirNames ?? AllLocalAppDataDirNames; } }
    static string[] TargetAppIds { get { return variantAppIds ?? VariantCatalog.AllAppIds; } }
    static string[] KnownInstallDirNames { get { return variantInstallDirNames ?? new string[0]; } }


        // KnownAppIds was merged into VariantCatalog.AllAppIds (see DSH_Desktop_Uninstaller.Core.cs).

    // AppId -> label mapping now lives in VariantCatalog.Profiles (see
    // DSH_Desktop_Uninstaller.Core.cs) and is queried via VariantCatalog.FindByAppId.

    static string DshInstallDir = string.Empty;
    static List<string> DshInstallDirs = new List<string>();
    const string LegacyUninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\62276e9d-c5f3-5091-b4ee-c7144d6db450";
    static string MachineEnvKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    static string DshHome = string.Empty;
    static string DshRuntime = string.Empty;

    static string ResolveDshRuntime()
    {
        try
        {
            // Authoritative override first: a future DSH build may export the
            // runtime location explicitly via DSH_RUNTIME.
            string envRuntime = Environment.GetEnvironmentVariable("DSH_RUNTIME");
            if (!string.IsNullOrWhiteSpace(envRuntime))
            {
                string t = envRuntime.Trim().Trim('"').TrimEnd('\\');
                if (PathSafety.IsUnsafeRootPath(t))
                {
                    Log("  WARNING: refusing unsafe DSH_RUNTIME override: " + envRuntime);
                }
                else
                {
                    string full = Path.GetFullPath(t);
                    string name = Path.GetFileName(full.TrimEnd('\\'));
                    bool namedRuntime = IsDshRuntimeName(name);
                    bool hasMarker = Directory.Exists(Path.Combine(full, "dsh", "node_modules"));
                    if (!PathSafety.IsUnsafeRootPath(full) && (namedRuntime || hasMarker) && Directory.Exists(full))
                    {
                        return full;
                    }
                    Log("  WARNING: refusing non-runtime DSH_RUNTIME override: " + envRuntime);
                }
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string defaultRuntime = Path.Combine(userProfile, ".dsh-runtime");
            if (Directory.Exists(defaultRuntime)) return defaultRuntime;

            // No registry value records the runtime location; resolve by
            // convention next to a custom DSH_HOME, with the install-dir
            // .dsh-runtime checked later in InitializeRuntime once install
            // dirs are known.
            string envHome = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrWhiteSpace(envHome))
            {
                string fullHome = Path.GetFullPath(envHome.Trim().TrimEnd('\\'));
                string parent = Path.GetDirectoryName(fullHome);
                if (!string.IsNullOrEmpty(parent) && parent != fullHome)
                {
                    string envHomeRuntime = Path.Combine(parent, ".dsh-runtime");
                    if (Directory.Exists(envHomeRuntime)) return envHomeRuntime;
                }
            }

            return defaultRuntime;
        }
        catch (Exception ex)
        {
            Log("  Warning in ResolveDshRuntime: " + ex.Message);
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh-runtime");
        }
    }
    static bool IsDshRuntimeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Equals(".dsh-runtime", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".dsh_runtime", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dsh-runtime", StringComparison.OrdinalIgnoreCase);
    }

    static bool useDetectedRunningDsh = false;
    static string selfTempDir = string.Empty;
    static string selfTempExe = string.Empty;
    static string logOverridePath = string.Empty;
    static string DetectedRunningDshDir = string.Empty;
    static string DetectedVariantLabel = string.Empty;
    static List<string> DetectedVariantLabels = new List<string>();
    static string LogFilePath = string.Empty;
    static bool VariantProfileApplied = false;

    static string ResolveLogFilePath()
    {
        try
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (!string.IsNullOrEmpty(exeDir))
            {
                return Path.Combine(exeDir, "Log.log");
            }
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        try
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "Log.log");
        }
        catch (Exception ex)
        {
            Log("  Warning (ignored): " + ex.Message);
            return "Log.log";
        }
    }

    // A single run removes every DETECTED DSH / DeepSeek Harness desktop
    // variant. Each detected variant's profile contributes its exe/process/
    // shortcut/directory/app-id names to a union list, so the cleanup is
    // targeted at exactly the variants that were found — not just one of
    // them and not every known repo. If none of the detected labels maps to
    // a known profile, all known lists are kept as a generic fallback.
    static bool ApplyVariantProfile()
    {
        try
        {
            CachedExtraDirectories = null; // recompute extra dirs on next use
            variantExeNames = null;
            variantProcessNames = null;
            variantShortcutNames = null;
            variantUpdaterDirNames = null;
            variantRoamingDirNames = null;
            variantLocalAppDataDirNames = null;
            variantAppIds = null;
            variantInstallDirNames = null;

            if (DetectedVariantLabels == null || DetectedVariantLabels.Count == 0)
            {
                Log("No DSH variant detected; using all known DSH lists as generic fallback.");
                return false;
            }

            List<string> repos = new List<string>();
            foreach (string label in DetectedVariantLabels)
            {
                string repo = ExtractRepoFromLabel(label);
                if (!string.IsNullOrEmpty(repo) && !repos.Contains(repo)) repos.Add(repo);
            }

            List<string> exes = new List<string>();
            List<string> procs = new List<string>();
            List<string> shortcuts = new List<string>();
            List<string> updaters = new List<string>();
            List<string> roaming = new List<string>();
            List<string> local = new List<string>();
            List<string> appIds = new List<string>();
            List<string> installDirs = new List<string>();
            bool anyProfile = false;

            foreach (string repo in repos)
            {
                VariantProfile prof = VariantCatalog.Find(repo);
                if (prof == null) continue;
                if (IsEmptyProfile(prof))
                {
                    Log("Empty profile (label-only) for " + repo + "; skipping its target lists.");
                    continue;
                }
                anyProfile = true;
                AddUnique(exes, prof.ExeNames);
                AddUnique(procs, prof.ProcessNames);
                AddUnique(shortcuts, prof.ShortcutNames);
                AddUnique(updaters, prof.UpdaterDirNames);
                AddUnique(roaming, prof.RoamingDirNames);
                AddUnique(local, prof.LocalAppDataDirNames);
                AddUnique(appIds, prof.AppIds);
                AddUnique(installDirs, prof.InstallDirNames);
            }

            if (!anyProfile)
            {
                Log("No known profile matched the detected labels; using all known DSH lists as generic fallback.");
                return false;
            }

            variantExeNames = exes.ToArray();
            variantProcessNames = procs.ToArray();
            variantShortcutNames = shortcuts.ToArray();
            variantUpdaterDirNames = updaters.ToArray();
            variantRoamingDirNames = roaming.ToArray();
            variantLocalAppDataDirNames = local.ToArray();
            variantAppIds = appIds.ToArray();
            variantInstallDirNames = installDirs.ToArray();

            Log("Cleanup scope: " + repos.Count + " detected variant(s) [" + string.Join(", ", repos.ToArray()) + "]; targeted lists: exe=" + variantExeNames.Length + ", proc=" + variantProcessNames.Length + ", shortcut=" + variantShortcutNames.Length + ", appId=" + variantAppIds.Length + ".");
            return true;
        }
        catch (Exception ex)
        {
            Log("ApplyVariantProfile failed: " + ex.Message);
            return false;
        }
    }

    static void AddUnique(List<string> target, string[] source)
    {
        if (source == null) return;
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string existing in target) seen.Add(existing);
        foreach (string item in source)
        {
            if (string.IsNullOrEmpty(item)) continue;
            if (seen.Add(item)) target.Add(item);
        }
    }
    static bool IsEmptyProfile(VariantProfile p)
    {
        return (p.ExeNames == null || p.ExeNames.Length == 0)
            && (p.ProcessNames == null || p.ProcessNames.Length == 0)
            && (p.ShortcutNames == null || p.ShortcutNames.Length == 0)
            && (p.UpdaterDirNames == null || p.UpdaterDirNames.Length == 0)
            && (p.RoamingDirNames == null || p.RoamingDirNames.Length == 0)
            && (p.LocalAppDataDirNames == null || p.LocalAppDataDirNames.Length == 0)
            && (p.AppIds == null || p.AppIds.Length == 0)
            && (p.InstallDirNames == null || p.InstallDirNames.Length == 0);
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
        catch (Exception ex) { Log("SafeResolveDshInstallDir failed: " + ex.Message); return string.Empty; }
    }

    static List<string> SafeResolveDshInstallDirs()
    {
        try { return ResolveDshInstallDirs(); }
        catch (Exception ex) { Log("SafeResolveDshInstallDirs failed: " + ex.Message); return new List<string>(); }
    }

    static string SafeFindRunningDshInstallDir()
    {
        try { return FindRunningDshInstallDir(); }
        catch (Exception ex) { Log("SafeFindRunningDshInstallDir failed: " + ex.Message); return string.Empty; }
    }

    static string SafeResolveVariantLabel()
    {
        try { return ResolveVariantLabel(); }
        catch (Exception ex) { Log("SafeResolveVariantLabel failed: " + ex.Message); return "未知"; }
    }

    static List<string> SafeResolveAllVariantLabels()
    {
        try { return ResolveAllVariantLabels(); }
        catch (Exception ex) { Log("SafeResolveAllVariantLabels failed: " + ex.Message); return new List<string>(); }
    }

    // Counts cleanup failures so /S (silent) mode can return a non-zero exit
    // code that scripts can check.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, int dwFlags);
    const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;
    static int failureCount = 0;
    static bool consoleAvailable = true;

    // Optional structured report for /S automation and audits.
    static string jsonReportPath = string.Empty;
static List<string> UnknownArgs = new List<string>();
    static bool installDirValueMissing = false;
    static List<string> CachedExtraDirectories;
    static List<string> reportSteps = new List<string>();
    static List<string> residualItems = new List<string>();

    // Version shown in the log so a support report can reproduce behavior.
    static readonly string UninstallerVersion = "1.6";

    // Prefetched in ConfirmAndSelectRetention so the GUI constructor performs
    // no synchronous disk I/O and the progress window opens instantly.
    static List<PresetInfo> CachedPresets;
    static List<PluginInfo> CachedPlugins;
    static List<SkillInfo> CachedSkills;

    // WMI results are cached per process id so repeated scans (process kill,
    // residual scan) do not issue the same slow Win32_Process query twice.
    static System.Collections.Concurrent.ConcurrentDictionary<int, KeyValuePair<DateTime, string>> CachedProcessPaths = new System.Collections.Concurrent.ConcurrentDictionary<int, KeyValuePair<DateTime, string>>();
    static System.Collections.Concurrent.ConcurrentDictionary<int, KeyValuePair<DateTime, string>> CachedProcessCommandLines = new System.Collections.Concurrent.ConcurrentDictionary<int, KeyValuePair<DateTime, string>>();


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

#region Entry Point & CLI Parsing
    static string BuildDeletionTargetsSummary()
    {
        List<string> targets = new List<string>();
        if (DshInstallDirs.Count > 0)
        {
            targets.Add("安装目录：" + string.Join(" | ", DshInstallDirs.ToArray()));
        }
        List<string> extraDirs = new List<string>();
        foreach (string dir in GetKnownExtraDirectories())
        {
            if (string.IsNullOrEmpty(dir)) continue;
            bool isInstall = false;
            foreach (string idir in DshInstallDirs)
            {
                if (!string.IsNullOrEmpty(idir) && dir.Equals(idir, StringComparison.OrdinalIgnoreCase)) { isInstall = true; break; }
            }
            if (isInstall) continue;
            extraDirs.Add(dir);
        }
        if (extraDirs.Count > 0)
        {
            targets.Add("附加目录：\r\n  " + string.Join("\r\n  ", extraDirs.ToArray()));
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

        // Prefetch preset/plugin/skill lists off the UI construction path.
        try { CachedPresets = DetectAgentPresets(); } catch (Exception ex) { Log("  DetectAgentPresets failed: " + ex.Message); CachedPresets = new List<PresetInfo>(); }
        try { CachedPlugins = DetectPlugins(); } catch (Exception ex) { Log("  DetectPlugins failed: " + ex.Message); CachedPlugins = new List<PluginInfo>(); }
        try { CachedSkills = DetectSkills(); } catch (Exception ex) { Log("  DetectSkills failed: " + ex.Message); CachedSkills = new List<SkillInfo>(); }

        Application.EnableVisualStyles();
        using (RetentionForm form = new RetentionForm())
        {
            RetentionOptions guiOptions = new RetentionOptions();
            guiOptions.Presets = keepAgentPresets;
            guiOptions.Runtime = keepRuntime;
            guiOptions.ChatData = keepChatData;
            guiOptions.AppSettings = keepAppSettings;
            guiOptions.ModelConfig = keepModelConfig;
            guiOptions.OtherUserData = keepOtherUserData;
            guiOptions.Plugins = keepPlugins;
            guiOptions.Skills = keepSkills;
            guiOptions.PresetNames.AddRange(keepPresetNames);
            guiOptions.PluginNames.AddRange(keepPluginNames);
            guiOptions.SkillNames.AddRange(keepSkillNames);
            form.SetRetentionOptions(guiOptions);

            if (form.ShowDialog() != DialogResult.OK)
            {
                return false;
            }

            retentionOptions.Presets = form.KeepAgentPresets;
            retentionOptions.Runtime = form.KeepRuntime;
            retentionOptions.ChatData = form.KeepChatData;
            retentionOptions.AppSettings = form.KeepAppSettings;
            retentionOptions.ModelConfig = form.KeepModelConfig;
            retentionOptions.OtherUserData = form.KeepOtherUserData;
            retentionOptions.Plugins = form.KeepPlugins;
            retentionOptions.Skills = form.KeepSkills;
            retentionOptions.PresetNames.Clear(); retentionOptions.PresetNames.AddRange(form.KeepPresetNames);
            retentionOptions.PluginNames.Clear(); retentionOptions.PluginNames.AddRange(form.KeepPluginNames);
            retentionOptions.SkillNames.Clear(); retentionOptions.SkillNames.AddRange(form.KeepSkillNames);
            useDetectedRunningDsh = form.UseDetectedRunningDsh;

            // Second confirmation: show exactly what will be retained and what
            // will be deleted before starting.
            string summary = RetentionSummary();
            string message = summary == "(none)"
            ? "确定卸载 DSH / DeepSeek Harness 桌面端并删除所有用户数据吗？"
            : "确定卸载 DSH / DeepSeek Harness 桌面端并保留以下内容吗？\r\n\r\n保留：\r\n" + summary;
                message += "\r\n\r\n将删除：\r\n" + BuildDeletionTargetsSummary();
            if (DshInstallDirs.Count == 0)
            {
                message += "\r\n\r\n⚠️ 未检测到 DSH 安装目录，将仅清理用户数据与已知额外目录。";
            }
            ConfirmForm confirmForm = new ConfirmForm(message);
            if (confirmForm.ShowDialog() != DialogResult.OK)
            {
            confirmForm.Dispose();
            return false;
            }

            // The second confirmation window stays on screen and morphs into
            // the progress window: current operation above the progress bar,
            // live log console below it.
            confirmForm.SwitchToProgress();
            confirmForm.Show();
            progressForm = confirmForm;
            return true;
        }
    }

    // When the uninstaller relaunches itself from a temp copy, the temp copy
    // cannot delete its own running exe. Schedule a delayed cmd to remove the
    // whole temp folder after the process has exited.
    // When this uninstaller relaunched a child from %TEMP%\\dsh-uninstaller-xxxx,
    // the child writes the authoritative uninstall log inside that temp dir.
    // ScheduleSelfTempDeletion removes the whole dir, so copy the log out first.
    static void PreserveChildLogFromTemp(string tempDir)
    {
        if (string.IsNullOrEmpty(tempDir)) return;
        try
        {
            string childLog = Path.Combine(tempDir, "Log.log");
            if (!File.Exists(childLog)) return;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string safeLog = Path.Combine(desktop, "DSH_Uninstaller_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            File.Copy(childLog, safeLog, true);
            Log("Child uninstaller log preserved at: " + safeLog);
        }
        catch (Exception ex)
        {
            Log("  Warning: could not preserve child uninstaller log from " + tempDir + ": " + ex.Message);
        }
    }

    static void StartDetachedCommand(string fileName, string arguments)
    {
        // Fire-and-forget helper for commands that must outlive the uninstaller
        // (for example delayed self-temp cleanup). Never wait here.
        try
        {
            Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex)
        {
            Log("  Warning: failed to start " + fileName + ": " + ex.Message);
        }
    }

    static void ScheduleSelfTempDeletion()
    {
        if (string.IsNullOrEmpty(selfTempDir)) return;
        try
        {
            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            string selfFull = Path.GetFullPath(selfTempDir);
            if (!selfFull.StartsWith(tempRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                Log("  WARNING: refusing to schedule deletion outside temp: " + selfTempDir);
                return;
            }
        }
        catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); return; }
        try
        {
            // Best effort: mark the running temp exe for deletion on next
            // reboot, then let a detached cmd retry rmdir after a short delay.
            string tempExe = !string.IsNullOrEmpty(selfTempExe) ? selfTempExe : Path.Combine(selfTempDir, "Uninstall_DSH_Desktop.exe");
            Log("Self-temp exe path: " + tempExe);
            if (File.Exists(tempExe))
            {
                try
                {
                    bool marked = MoveFileEx(tempExe, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                    Log("Marked temp exe for reboot deletion (success=" + marked + "): " + tempExe);
                }
                catch (Exception mfEx) { Log("  Warning: MoveFileEx failed: " + mfEx.Message); }
            }
            string cmd = "/C ping 127.0.0.1 -n 6 >nul & rmdir /s /q \"" + selfTempDir + "\"";
            Log("Scheduling temp cleanup command (best effort; may remain until reboot if the uninstaller is still running): cmd.exe " + cmd);
            StartDetachedCommand("cmd.exe", cmd);
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
    }

    // Explicit startup order: CLI first, then environment-derived values,
    // then detection/label/profile, and finally the log. This replaces the
    // old static field initialization order that caused subtle bugs.
    static void InitializeRuntime()
    {
        DshHome = ResolveDshHome();
        DshRuntime = ResolveDshRuntime();
        DetectedRunningDshDir = SafeFindRunningDshInstallDir();

        // Stage 1: registry + running process identify the variant. DshInstallDir
        // is still empty here; ResolveAllVariantLabels only needs the running
        // process fallback at this point.
        DetectedVariantLabels = SafeResolveAllVariantLabels();
        DetectedVariantLabel = DetectedVariantLabels.Count == 0 ? "\u672a\u77e5" : string.Join("\n", DetectedVariantLabels.ToArray());
        VariantProfileApplied = ApplyVariantProfile();

        // Stage 2: with the variant profile applied (or generic lists when no
        // profile matched), resolve the install directory.
        DshInstallDirs = SafeResolveDshInstallDirs();
        DshInstallDir = DshInstallDirs.Count > 0 ? DshInstallDirs[0] : string.Empty;

        // Stage 2b: if the profile .dsh-runtime does not exist yet, prefer an
        // install dir that carries its own .dsh-runtime subdirectory.
        if (!Directory.Exists(DshRuntime))
        {
            foreach (string dir in DshInstallDirs)
            {
                string local = Path.Combine(dir, ".dsh-runtime");
                if (Directory.Exists(local)) { DshRuntime = local; break; }
            }
        }

        // Stage 3: if stage 1 could not identify a variant but an install dir
        // was found, derive the label from that dir and re-apply once.
        bool unknown = DetectedVariantLabels.Count == 1 && DetectedVariantLabels[0] == "\u672a\u77e5";
        if (unknown && !string.IsNullOrEmpty(DshInstallDir))
        {
            string labelFromPath = ResolveLabelFromPath(DshInstallDir);
            if (!string.IsNullOrEmpty(labelFromPath))
            {
                DetectedVariantLabels.Clear();
                DetectedVariantLabels.Add(labelFromPath);
                DetectedVariantLabel = labelFromPath;
                VariantProfileApplied = ApplyVariantProfile();
            }
        }

          if (string.IsNullOrEmpty(logOverridePath)) LogFilePath = ResolveLogFilePath();
    }

    [STAThread]
    static int Main(string[] args)
    {
        // Request administrator rights immediately, before any detection or log
        // file creation, so permission checks cannot intercept later steps.
        // Pure read-only modes (/help, /DryRun, /Preview) run without elevation.
        bool pureInfoMode = false;
        foreach (string a in args)
        {
            string t = a;
            int eq = t.IndexOf('=');
            if (eq >= 0) t = t.Substring(0, eq);
            if (t.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("-help", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("/?", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("-?", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("/DryRun", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("-DryRun", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("/Preview", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("-Preview", StringComparison.OrdinalIgnoreCase))
            {
                pureInfoMode = true;
                break;
            }
        }
        // Pre-scan for /S before elevation so a UAC cancel or runas failure in
        // silent mode can be handled without any GUI popup.
        foreach (string t in args)
        {
            if (t.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("-S", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("-silent", StringComparison.OrdinalIgnoreCase))
            {
                silent = true;
                break;
            }
        }
        if (!pureInfoMode && !IsAdministrator())
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Assembly.GetEntryAssembly().Location;
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                psi.Arguments = PureHelpers.BuildQuotedArguments(args);
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                if (silent)
                {
                    Log("ERROR: UAC elevation failed in silent mode: " + ex.Message);
                    return 1;
                }
                ShowMessage("\u9700\u8981\u7ba1\u7406\u5458\u6743\u9650\uff0c\u8bf7\u53f3\u952e\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\u3002" + Environment.NewLine + Environment.NewLine + ex.Message,
                    "DSH \u684c\u9762\u7aef\u5378\u8f7d\u5668", MessageBoxIcon.Warning);
                return 1;
            }
        }
        ParseArgs(args);
        if (!string.IsNullOrEmpty(logOverridePath))
        {
            LogFilePath = logOverridePath;
        }
        // Initialize the log BEFORE InitializeRuntime() so the variant
        // detection / profile-selection diagnostics inside it are recorded.
        if (string.IsNullOrEmpty(LogFilePath)) LogFilePath = ResolveLogFilePath();
        InitializeLog();
        if (!string.IsNullOrEmpty(LogService.MainPath)) LogFilePath = LogService.MainPath;
        if (helpRequested)
        {
            PrintUsage();
            return 0;
        }
        if (UnknownArgs.Count > 0)
        {
            string joined = string.Join(" ", UnknownArgs.ToArray());
            Log("ERROR: unrecognized argument(s): " + joined);
            if (silent)
            {
                Log("ERROR: aborting in silent mode because of unrecognized arguments.");
                return 2;
            }
            DialogResult argConfirm = ShowMessage("存在无法识别的参数：\r\n" + joined + "\r\n\r\n是否仍然继续卸载？（可用 /help 查看全部参数）", "参数确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (argConfirm != DialogResult.Yes)
            {
                Log("Uninstall cancelled by user (unrecognized arguments).");
                return 0;
            }
        }
        if (installDirValueMissing)
        {
            Log("ERROR: /InstallDir requires a path value.");
            if (silent) return 2;
            ShowMessage("\u6307\u5b9a\u4e86 /InstallDir \u4f46\u672a\u63d0\u4f9b\u8def\u5f84\uff0c\u8bf7\u91cd\u65b0\u8f93\u5165\u3002", "\u53c2\u6570\u9519\u8bef", MessageBoxIcon.Warning);
            return 2;
        }
        InitializeRuntime();

        // /InstallDir must take effect BEFORE the self-relocation decision
        // below, and it must fail hard when the path is invalid in silent
        // mode. Interactive mode gets a yes/no fallback-to-auto choice.
        if (!string.IsNullOrEmpty(manualInstallDir))
        {
            string norm = PathSafety.NormalizeDirForDelete(manualInstallDir);
            bool valid = !string.IsNullOrEmpty(norm) && Directory.Exists(norm) && IsSafeInstallDir(norm) && IsStrongInstallDirEvidence(norm);
            if (!valid)
            {
                Log("ERROR: /InstallDir path is invalid or does not look like a DSH desktop: " + manualInstallDir);
                if (silent) return 2;
                DialogResult d = ShowMessage("\u6307\u5b9a\u7684\u5b89\u88c5\u76ee\u5f55\u65e0\u6548\u6216\u4e0d\u662f DSH \u684c\u9762\u7aef\uff1a\r\n" + manualInstallDir + "\r\n\r\n\u662f\u5426\u7ee7\u7eed\u81ea\u52a8\u68c0\u6d4b\u5e76\u5378\u8f7d\uff1f", "\u5b89\u88c5\u76ee\u5f55\u786e\u8ba4", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (d != DialogResult.Yes) { Log("Uninstall cancelled by user (invalid /InstallDir)."); return 0; }
            }
            else
            {
                manualInstallDir = norm;
                DshInstallDirs.Clear();
                DshInstallDirs.Add(norm);
                DshInstallDir = norm;
                string lbl = ResolveLabelFromPath(norm);
                if (string.IsNullOrEmpty(lbl)) lbl = "\u672a\u77e5";
                DetectedVariantLabel = lbl;
                DetectedVariantLabels.Clear();
                DetectedVariantLabels.Add(lbl);
                VariantProfileApplied = ApplyVariantProfile();
                Log("Uninstall mode: manual install dir -> " + norm);
            }
        }
        MergePreviousRelocatedLog();
        Log("Detected DSH: " + DetectedVariantLabel);
        if (VariantProfileApplied)
        {
            string repo = ExtractRepoFromLabel(DetectedVariantLabel);
            if (!string.IsNullOrEmpty(repo)) Log("Variant profile applied for: " + repo);
        }
        Log("Command line: " + Environment.CommandLine);
        Log("Log file: " + LogFilePath);
        Log("Uninstaller version: " + UninstallerVersion);
        Log("Administrator: " + IsAdministrator());
        Log("Variant config fingerprint: exe=" + KnownExeNames.Length + ", proc=" + KnownProcessNames.Length + ", shortcut=" + KnownShortcutNames.Length + ", appId=" + TargetAppIds.Length);

        if (dryRun)
        {
            RunDryRun();
            return 0;
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
                  selfTempExe = tempExe;
                File.Copy(srcExe, tempExe, true);
                Log("Uninstaller runs from install dir; relaunching from temp: " + tempExe);
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = tempExe;
                psi.UseShellExecute = false;
                psi.Arguments = PureHelpers.BuildQuotedArguments(args);
                Log("Child command: " + tempExe + " " + psi.Arguments);
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit();
                    // The child uninstaller writes its Log.log inside the temp
                    // directory that ScheduleSelfTempDeletion will remove. Copy
                    // it to a safe location first so the real uninstall log is
                    // not lost (the child merged our old log, but nothing merges
                    // the child log back to a safe place).
                    PreserveChildLogFromTemp(tempDir);
                    ScheduleSelfTempDeletion();
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to relocate uninstaller for self-deletion: " + ex.Message);
                ScheduleSelfTempDeletion();
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

            if (silent)
            {
                // Silent mode has no progress window to pump; run directly on
                // this thread to keep the shutdown path simple and deterministic.
                Run();
            }
            else
            {
                // For interactive mode the confirmation window has already
                // switched to the progress view and is stored in progressForm.
                // Run() executes on a worker thread while the main thread pumps
                // window messages, so the progress UI stays responsive and no
                // destructive step is ever interrupted by DoEvents reentrancy.
                Exception runError = null;
                Thread worker = new Thread(delegate()
                {
                    try { Run(); }
                    catch (Exception ex) { runError = ex; }
                });
                worker.SetApartmentState(ApartmentState.STA);
                worker.Start();
                while (!worker.Join(100))
                {
                    Application.DoEvents();
                }

                try
                {
                    if (progressForm != null)
                    {
                        progressForm.AllowClose = true;
                        progressForm.Close();
                        progressForm.Dispose();
                    }
                }
                catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
                progressForm = null;

                if (runError != null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(runError).Throw();
                }
            }

            Log("===== Uninstaller exit =====");
            WriteJsonReport((failureCount > 0) ? 1 : 0, string.Empty);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex);
            WriteJsonReport(1, ex.Message);
            ShowMessage("卸载过程发生错误：\r\n" + ex.Message + "\r\n\r\n日志：\r\n" + LogService.MainPath, "DSH 桌面端卸载器", MessageBoxIcon.Error);
            ScheduleSelfTempDeletion();
            Pause();
            return 1;
        }

        ScheduleSelfTempDeletion();
        if (!silent) ShowCompletionPopup();
        Pause();
        // Both silent and interactive modes report cleanup failures via a non-zero exit code.
        // Interactive mode additionally shows the failure count in the completion popup.
        return (failureCount > 0) ? 1 : 0;
    }

    // All user-facing message boxes go through this helper so silent mode
    // never blocks on a modal dialog: it only logs and relies on exit codes.
    static void ShowMessage(string text, string caption, System.Windows.Forms.MessageBoxIcon icon)
    {
        ShowMessage(text, caption, MessageBoxButtons.OK, icon);
    }

    static DialogResult ShowMessage(string text, string caption, MessageBoxButtons buttons, System.Windows.Forms.MessageBoxIcon icon)
    {
        if (silent)
        {
            Log("UI message suppressed in silent mode [" + caption + "]: " + text);
            return DialogResult.OK;
        }
        return MessageBox.Show(text, caption, buttons, icon);
    }
static void ParseArgs(string[] args)
{
    UnknownArgs.Clear();
    List<ArgSpec> specs = BuildArgSpecs();
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

        bool matched = false;
        foreach (ArgSpec spec in specs)
        {
            if (spec.Matches(arg))
            {
                spec.Apply(value);
                matched = true;
                break;
            }
        }
        if (!matched) UnknownArgs.Add(raw);
    }
}


    static List<ArgSpec> BuildArgSpecs()
    {
        return new List<ArgSpec>()
        {
            new ArgSpec(new[] { "/S", "-S", "/silent", "-silent" }, v => { silent = true; }),
            new ArgSpec(new[] { "/KeepPresets", "-KeepPresets" }, v => { keepAgentPresets = true; if (!string.IsNullOrWhiteSpace(v)) keepPresetNames = PureHelpers.ParsePresetNames(v); }),
            new ArgSpec(new[] { "/KeepRuntime", "-KeepRuntime" }, v => { keepRuntime = true; }),
            new ArgSpec(new[] { "/KeepPlugins", "-KeepPlugins" }, v => { keepPlugins = true; keepRuntime = true; if (!string.IsNullOrWhiteSpace(v)) keepPluginNames = PureHelpers.ParsePresetNames(v); }),
            new ArgSpec(new[] { "/KeepVision", "-KeepVision" }, v => { keepPlugins = true; keepRuntime = true; if (!keepPluginNames.Contains("@dsh-external/dsh-vision", StringComparer.OrdinalIgnoreCase)) keepPluginNames.Add("@dsh-external/dsh-vision"); }),
            new ArgSpec(new[] { "/KeepSkills", "-KeepSkills" }, v => { keepSkills = true; if (!string.IsNullOrWhiteSpace(v)) keepSkillNames = PureHelpers.ParsePresetNames(v); }),
            new ArgSpec(new[] { "/KeepAppSettings", "-KeepAppSettings" }, v => { keepAppSettings = true; }),
            new ArgSpec(new[] { "/KeepModelConfig", "-KeepModelConfig" }, v => { keepModelConfig = true; }),
            new ArgSpec(new[] { "/KeepOtherUserData", "-KeepOtherUserData", "/KeepOtherData", "-KeepOtherData" }, v => { keepOtherUserData = true; }),
            new ArgSpec(new[] { "/KeepChatData", "-KeepChatData", "/KeepChat", "-KeepChat" }, v => { keepChatData = true; }),
            new ArgSpec(new[] { "/KeepAll", "-KeepAll" }, v => { keepAgentPresets = true; keepRuntime = true; keepPlugins = true; keepChatData = true; keepAppSettings = true; keepModelConfig = true; keepOtherUserData = true; keepSkills = true; }),
            new ArgSpec(new[] { "/DetectRunning", "-DetectRunning", "/DetectDSH", "-DetectDSH" }, v => { useDetectedRunningDsh = true; }),
            new ArgSpec(new[] { "/Default", "-Default" }, v => { useDetectedRunningDsh = false; }),
            new ArgSpec(new[] { "/InstallDir", "-InstallDir", "/Dir", "-Dir" }, v => { if (string.IsNullOrWhiteSpace(v)) installDirValueMissing = true; else manualInstallDir = v.Trim().Trim('"'); }),
            new ArgSpec(new[] { "/Log", "-Log", "/LogFile", "-LogFile" }, v => { if (!string.IsNullOrWhiteSpace(v)) logOverridePath = v.Trim().Trim('"'); }),
            new ArgSpec(new[] { "/JsonReport", "-JsonReport" }, v => { if (!string.IsNullOrWhiteSpace(v)) jsonReportPath = v.Trim().Trim('"'); }),
            new ArgSpec(new[] { "/DryRun", "-DryRun", "/Preview", "-Preview" }, v => { dryRun = true; }),
            new ArgSpec(new[] { "/help", "-help", "/?", "-?", "-h" }, v => { helpRequested = true; }),
        };
    }

    // BuildQuotedArguments / EscapeWindowsArg / ParsePresetNames moved to PureHelpers.

    static void PrintUsage()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("DSH / DeepSeek Harness \u684c\u9762\u7aef\u5378\u8f7d\u5668");
        sb.AppendLine();
        sb.AppendLine("\u7528\u6cd5: Uninstall_DSH_Desktop.exe [\u9009\u9879]");
        sb.AppendLine();
        sb.AppendLine("  \u5378\u8f7d\u6a21\u5f0f:");
        sb.AppendLine("    /S \u6216 /silent          \u9759\u9ed8\u5378\u8f7d\uff08\u4e0d\u663e\u793a\u754c\u9762\uff09");
        sb.AppendLine("    /DetectRunning         \u4f18\u5148\u68c0\u6d4b\u6b63\u5728\u8fd0\u884c\u7684 DSH \u5b89\u88c5\u76ee\u5f55");
        sb.AppendLine("    /Default               \u4f7f\u7528\u9ed8\u8ba4\u68c0\u6d4b\uff08\u6ce8\u518c\u8868/\u5e38\u89c1\u8def\u5f84\uff09");
        sb.AppendLine("    /InstallDir=<\u8def\u5f84>     \u624b\u52a8\u6307\u5b9a\u5b89\u88c5\u76ee\u5f55");
        sb.AppendLine("    /Log=<\u8def\u5f84>           \u6307\u5b9a\u65e5\u5fd7\u6587\u4ef6\u8def\u5f84\uff08\u9ed8\u8ba4 exe \u540c\u76ee\u5f55 Log.log\uff09");
        sb.AppendLine("    /JsonReport=<\u8def\u5f84>    \u8f93\u51fa\u7ed3\u6784\u5316 JSON \u62a5\u544a\uff08\u6b65\u9aa4/\u6b8b\u7559/\u5931\u8d25\u8ba1\u6570\uff09");
        sb.AppendLine("    /DryRun                \u53ea\u68c0\u6d4b\u5e76\u5217\u51fa\u5c06\u5220\u9664/\u4fdd\u7559\u7684\u5185\u5bb9\uff0c\u4e0d\u5b9e\u9645\u5220\u9664");
        sb.AppendLine("    /help \u6216 /?            \u663e\u793a\u672c\u5e2e\u52a9");
        sb.AppendLine();
        sb.AppendLine("  \u4fdd\u7559\u9009\u9879:");
        sb.AppendLine("    /KeepPresets[=\u540d\u79f0]    \u4fdd\u7559\u9884\u8bbe\uff08\u4e0d\u586b=\u5168\u90e8\uff1b\u591a\u4e2a\u7528\u9017\u53f7\u5206\u9694\uff09");
        sb.AppendLine("    /KeepSkills[=\u540d\u79f0]     \u4fdd\u7559 skills\uff08\u4e0d\u586b=\u5168\u90e8\uff1b\u591a\u4e2a\u7528\u9017\u53f7\u5206\u9694\uff09");
        sb.AppendLine("    /KeepChatData          \u4fdd\u7559\u804a\u5929\u6570\u636e (sessions)");
        sb.AppendLine("    /KeepAppSettings       \u4fdd\u7559\u5e94\u7528\u8bbe\u7f6e (settings.yaml)");
        sb.AppendLine("    /KeepModelConfig       \u4fdd\u7559\u6a21\u578b\u914d\u7f6e\u4e0e\u51ed\u636e");
        sb.AppendLine("    /KeepOtherUserData     \u4fdd\u7559\u5176\u4ed6 .dsh \u6570\u636e");
        sb.AppendLine("    /KeepPlugins[=\u540d\u79f0]    \u4fdd\u7559\u63d2\u4ef6\uff08\u4e0d\u586b=\u5168\u90e8\uff09");
        sb.AppendLine("    /KeepVision            \u4fdd\u7559 dsh-vision \u63d2\u4ef6\uff08\u7b49\u540c\u4e8e /KeepPlugins=@dsh-external/dsh-vision\uff09");
        sb.AppendLine("    /KeepRuntime           \u4fdd\u7559 .dsh-runtime");
        sb.AppendLine("    /KeepAll               \u4fdd\u7559\u4ee5\u4e0a\u5168\u90e8");
          sb.AppendLine();
          sb.AppendLine("  \u793a\u4f8b: Uninstall_DSH_Desktop.exe /S /KeepChatData /Log=C:\\tmp\\dsh-uninstall.log");
        string usage = sb.ToString();
        // Console output still works when stdout is redirected (e.g. > help.txt);
        // the message box makes /help visible under the winexe subsystem.
        try { Console.WriteLine(usage); } catch (Exception ex) { Log("  Warning (ignored): " + ex.Message); }
        ShowMessage(usage, "DSH \u684c\u9762\u7aef\u5378\u8f7d\u5668\u5e2e\u52a9", MessageBoxIcon.Information);
    }

    static void RunDryRun()
    {
        Log("===== DSH Desktop Uninstaller Dry-Run =====");
        Log("\u5b89\u88c5\u76ee\u5f55:   " + (DshInstallDirs.Count > 0 ? string.Join(" | ", DshInstallDirs.ToArray()) : "(\u672a\u68c0\u6d4b\u5230)"));
        Log("当前DSH:    " + DetectedVariantLabel);
        Log("用户数据:   " + DshHome);
        Log("运行时:     " + DshRuntime);
        Log("保留:       " + RetentionSummary());
        Log("");
        Log("将删除的主要内容:");
        Log("  - \u5b89\u88c5\u76ee\u5f55: " + (DshInstallDirs.Count > 0 ? string.Join(" | ", DshInstallDirs.ToArray()) : "(\u672a\u68c0\u6d4b\u5230\uff0c\u8df3\u8fc7)"));
        foreach (string dir in GetKnownExtraDirectories())
        {
            if (!string.IsNullOrEmpty(dir)) Log("  - 额外目录: " + dir);
        }
        Log("  - 快捷方式: 桌面/开始菜单中的 DSH 相关 .lnk");
        Log("  - 注册表:   卸载键 + 通知设置 + PATH 条目 + Run 启动项");
        Log("  - 用户数据: " + DshHome + (keepAgentPresets || keepChatData || keepSkills || keepAppSettings || keepModelConfig || keepOtherUserData ? "（按选项保留）" : "（全部删除）"));
        Log("  - 运行时:   " + (keepRuntime ? "保留" : DshRuntime));
        Log("===== Dry-run end =====");
        WriteJsonReport(0, string.Empty);
    }


#endregion

#region Uninstall Pipeline
    static bool IsAdministrator()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    static bool IsRunningFromDshInstallDir()
    {
        try
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (string.IsNullOrEmpty(exeDir)) return false;
            if (IsPathUnderAny(exeDir, DshInstallDirs)) return true;
            return false;
        }
        catch (Exception ex)
        {
            Log("  Warning (ignored): " + ex.Message);
            return false;
        }
    }


    static bool IsSafeInstallDir(string dir)
    {
        return !string.IsNullOrWhiteSpace(dir) && !PathSafety.IsUnsafeRootPath(dir);
    }

    // Shared by ResolveDshHome and IsSafeDshHome so detection and cleanup
    // always agree on what counts as a DSH home directory name.
    // Runtime-content evidence used before deleting a runtime directory.
    static bool IsLikelyDshRuntime(string dir)
    {
        try
        {
            string name = Path.GetFileName(Path.GetFullPath(dir).TrimEnd('\\'));
            bool namedRuntime = IsDshRuntimeName(name);
            bool hasNodeModules = Directory.Exists(Path.Combine(dir, "dsh", "node_modules"));
            bool hasNodeExe = File.Exists(Path.Combine(dir, "node", "node.exe")) || File.Exists(Path.Combine(dir, "node.exe"));
            // Require at least two independent runtime markers so a single
            // fabricated empty node directory or a matching folder name can
            // never authorize deletion of an arbitrary DSH_RUNTIME target.
            int evidence = (namedRuntime ? 1 : 0) + (hasNodeModules ? 1 : 0) + (hasNodeExe ? 1 : 0);
            return evidence >= 2;
        }
        catch (Exception ex)
        {
            Log("  Warning (ignored): " + ex.Message);
            return false;
        }
    }
    static bool IsDshHomeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Equals(".dsh", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".dsh-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(".dsh_", StringComparison.OrdinalIgnoreCase);
    }

    // If Log.log lives inside a directory that Run() will delete, create a
    // safe copy first and dual-write to it for the rest of the run. The
    // copy goes to the exe directory's parent when possible (desktop as a
    // last resort), so no fixed C-drive log path is introduced.
    static void PreserveLogCopyIfNeeded()
    {
        if (!LogService.Available || !string.IsNullOrEmpty(LogService.CopyPath)) return;
        try
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            if (string.IsNullOrEmpty(exeDir)) return;

            List<string> doomed = new List<string>();
            foreach (string dir in DshInstallDirs)
            {
                if (!string.IsNullOrEmpty(dir)) doomed.Add(dir);
            }
            if (!string.IsNullOrEmpty(DshHome)) doomed.Add(DshHome);
            if (!string.IsNullOrEmpty(DshRuntime)) doomed.Add(DshRuntime);
            if (!string.IsNullOrEmpty(selfTempDir)) doomed.Add(selfTempDir);
            foreach (string d in GetKnownExtraDirectories())
            {
                if (!string.IsNullOrEmpty(d)) doomed.Add(d);
            }

            // The active log may be inside a directory that Run() deletes even
            // when the exe itself is not (e.g. /Log=<install dir>\run.log).
            bool exeUnder = IsPathUnderAny(exeDir, doomed);
            bool logUnder = IsPathUnderAny(LogFilePath, doomed);
            if (!exeUnder && !logUnder) return;

            string baseDir = exeUnder ? exeDir : Path.GetDirectoryName(LogFilePath);
            if (string.IsNullOrEmpty(baseDir)) baseDir = exeDir;
            string safeDir = FindSafeDirOutside(baseDir, doomed);

            string copyPath = Path.Combine(safeDir, "DSH_Uninstaller_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            if (!copyPath.Equals(LogFilePath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(LogService.MainPath)) File.Copy(LogService.MainPath, copyPath, true);
                LogService.SetCopyPath(copyPath);
                Log("Log copy preserved at: " + copyPath);
            }
        }
        catch (Exception ex)
        {
                Log("  Warning (ignored): " + ex.Message);
            // Desktop fallback: keep the log even if the parent directory is
            // not writable (e.g. exe sits directly under a drive root).
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (!string.IsNullOrEmpty(desktop))
                {
                    string copyPath = Path.Combine(desktop, "DSH_Uninstaller_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                    if (!copyPath.Equals(LogFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(LogService.MainPath)) File.Copy(LogService.MainPath, copyPath, true);
                        LogService.SetCopyPath(copyPath);
                        Log("Log copy preserved on desktop: " + copyPath);
                    }
                }
            }
            catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        }
    }

    static string FindSafeDirOutside(string baseDir, IEnumerable<string> doomed)
    {
        try
        {
            string candidate = Path.GetFullPath(baseDir);
            for (int i = 0; i < 8; i++)
            {
                if (!IsPathUnderAny(candidate, doomed))
                {
                    return candidate;
                }
                string parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) || parent == candidate)
                {
                    break;
                }
                candidate = parent;
            }
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    static void MergePreviousRelocatedLog()
    {
        try
        {
            string exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string tempPrefix = Path.GetTempPath().TrimEnd('\\') + "\\dsh-uninstaller-";
            if (string.IsNullOrEmpty(exeDir) || !exeDir.StartsWith(tempPrefix, StringComparison.OrdinalIgnoreCase)) return;
            if (DshInstallDirs.Count == 0) return;
            foreach (string installDir in DshInstallDirs)
            {
                string prevLog = Path.Combine(installDir, "Log.log");
                if (!File.Exists(prevLog)) continue;
                if (prevLog.Equals(LogFilePath, StringComparison.OrdinalIgnoreCase)) continue;
                string text = File.ReadAllText(prevLog);
                File.AppendAllText(LogFilePath, Environment.NewLine + "----- Log from the process that ran inside the install directory -----" + Environment.NewLine + text);
                Log("Merged previous log from install directory: " + prevLog);
                return;
            }
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
    }

    static bool IsPathUnderAny(string path, IEnumerable<string> dirs)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
            foreach (string d in dirs)
            {
                if (string.IsNullOrEmpty(d)) continue;
                string dfull = Path.GetFullPath(d).TrimEnd('\\') + "\\";
                if (full.StartsWith(dfull, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        return false;
    }
    static void Run()
    {
        if (useDetectedRunningDsh)
        {
            List<string> runningDirs = FindRunningDshInstallDirs();
            if (runningDirs.Count > 0)
            {
                DshInstallDirs.Clear();
                DshInstallDirs.AddRange(runningDirs);
                DshInstallDir = DshInstallDirs[0];
                Log("Uninstall mode: detect running DSH -> " + string.Join(" | ", DshInstallDirs.ToArray()));
                string runLabel = ResolveLabelFromPath(DshInstallDir);
                if (string.IsNullOrEmpty(runLabel)) runLabel = "\u672a\u77e5";
                DetectedVariantLabel = runLabel;
                DetectedVariantLabels.Clear();
                DetectedVariantLabels.Add(runLabel);
                VariantProfileApplied = ApplyVariantProfile();
                Log("Variant label updated from running install dir: " + runLabel);
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
        if (DshInstallDirs.Count > 0)
        {
            Log("Install dirs (" + DshInstallDirs.Count + "): " + string.Join(" | ", DshInstallDirs.ToArray()));
        }
        else
        {
            Log("Install dir: (not detected)");
            Log("  WARNING: no DSH install directory detected; only user data and known extra directories will be cleaned.");
        }
        Log("");

        UpdateProgress(0);
        PreserveLogCopyIfNeeded();
        reportSteps.Clear();
        residualItems.Clear();

        RunStep("[1/9] Stopping DSH Desktop processes", delegate { KillDSHProcesses(); });
        UpdateProgress(11);

        RunStep("[2/9] Deleting install directories", delegate
        {
            List<string> skipUnderInstall = new List<string>();
            if (IsPathUnderAny(DshHome, DshInstallDirs))
            {
                Log("  WARNING: DSH user data resides inside the install directory; install-directory deletion will skip it and later steps handle retention.");
                skipUnderInstall.Add(Path.GetFullPath(DshHome));
            }
            if (IsPathUnderAny(DshRuntime, DshInstallDirs))
            {
                Log("  WARNING: DSH runtime resides inside the install directory; install-directory deletion will skip it and the runtime step handles it.");
                skipUnderInstall.Add(Path.GetFullPath(DshRuntime));
            }
            foreach (string dir in DshInstallDirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                List<string> dirSkips = new List<string>();
                foreach (string sp in skipUnderInstall)
                {
                    if (IsPathUnderAny(sp, new string[] { dir })) dirSkips.Add(sp);
                }
                DeleteDirectoryWithRetry(dir, dirSkips.ToArray());
            }
        });
        UpdateProgress(22);

        RunStep("[3/9] Deleting known extra directories", delegate
        {
            foreach (string dir in GetKnownExtraDirectories())
            {
                if (string.IsNullOrEmpty(dir)) continue;
                bool isInstall = false;
                foreach (string idir in DshInstallDirs)
                {
                    if (!string.IsNullOrEmpty(idir) && dir.Equals(idir, StringComparison.OrdinalIgnoreCase)) { isInstall = true; break; }
                }
                if (isInstall) continue;
                if (Directory.Exists(dir)) DeleteDirectoryWithRetry(dir);
            }
        });
        UpdateProgress(33);

        RunStep("[4/9] Deleting DSH shortcuts and registry entries", delegate
        {
            DeleteKnownDshShortcuts();
            DeleteRegistryKeys();
            CleanupRunKeys();
        });
        UpdateProgress(44);

        RunStep("[5/9] Cleaning PATH entries", delegate
        {
            CleanupMachinePath();
            CleanupUserPath();
            BroadcastEnvironmentChange();
        });
        UpdateProgress(55);

        RunStep("[6/9] Preserving selected plugins", delegate { PreserveSelectedPlugins(); });
        UpdateProgress(66);

        RunStep("[7/9] Cleaning DSH user data", delegate { CleanDshHome(); });
        UpdateProgress(77);

        RunStep("[8/9] Cleaning runtime", delegate
        {
            if (!keepRuntime)
            {
                string safeRuntime = PathSafety.NormalizeDirForDelete(DshRuntime);
                if (!IsLikelyDshRuntime(safeRuntime))
                {
                    LogAndCountFail("  Refusing to delete runtime path without runtime evidence: " + safeRuntime);
                }
                else
                {
                    DeleteDirectoryWithRetry(safeRuntime);
                }
            }
            foreach (string dir in DshInstallDirs)
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                try
                {
                    if (Directory.GetDirectories(dir).Length == 0 && Directory.GetFiles(dir).Length == 0)
                    {
                        Directory.Delete(dir, false);
                        Log("  Removed empty install-directory shell: " + dir);
                    }
                }
                catch (Exception ex) { Log("  Warning: could not remove empty install-directory shell " + dir + ": " + ex.Message); }
            }
        });
        UpdateProgress(88);

        RunStep("[9/9] Cleaning temp files and scanning residuals", delegate
        {
            CleanupTemp();
            residualItems = RunResidualScan();
            foreach (string r in residualItems)
            {
                if (r.StartsWith("[RESIDUAL]", StringComparison.OrdinalIgnoreCase))
                {
                    LogAndCountFail("  Residual found: " + r);
                }
            }
        });
        UpdateProgress(100);

        Log("");
        Log("===== Uninstall finished =====");
        Log("Removed DSH Desktop / DeepSeek Harness app, updaters, caches, shortcuts, uninstall registry key and DSH user data.");
        Log("Kept: " + RetentionSummary());
        Log("Advice: reboot or log off/on to fully refresh the PATH environment.");
    }

    static void RunStep(string label, Action body)
    {
        AddStep(label);
        try
        {
            body();
        }
        catch (Exception ex)
        {
            LogAndCountFail("  Step failed (" + label + "): " + ex.Message);
        }
    }


    static bool SafeDirHasEntries(string dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            return Directory.GetDirectories(dir).Length > 0 || Directory.GetFiles(dir).Length > 0;
        }
        catch (Exception ex)
        {
            Log("  Warning: cannot enumerate directory (treating as non-empty): " + dir + " -> " + ex.Message);
            return true;
        }
    }

    static List<string> RunResidualScan()
    {
        List<string> residual = new List<string>();
        Log("  Residual scan:");

        if (DshInstallDirs.Count == 0)
        {
            Log("    [INFO] Install dir: not applicable (not detected)");
        }
        foreach (string dir in DshInstallDirs)
        {
            if (Directory.Exists(dir))
            {
                residual.Add("Install dir: " + dir);
                Log("    [RESIDUAL] Install dir: " + dir);
            }
            else
            {
                Log("    [CLEAN] Install dir: confirmed removed -> " + dir);
            }
        }

        bool userDataKept = keepAgentPresets || keepChatData || keepSkills || keepAppSettings || keepModelConfig || keepOtherUserData;
        if (userDataKept)
        {
            if (Directory.Exists(DshHome))
            {
                Log("    [INFO] User data: Retained by choice -> " + DshHome + " (\u4fdd\u7559\u5185\u5bb9\u5df2\u4fdd\u7559\uff0c\u76ee\u5f55\u672a\u5220\u9664)");
            }
            else
            {
                Log("    [CLEAN] User data: " + DshHome + " (kept by choice but path absent)");
            }
        }
        else
        {
            if (Directory.Exists(DshHome))
            {
                if (IsDshHomeName(Path.GetFileName(DshHome.TrimEnd('\\'))) ||
                    SafeDirHasEntries(DshHome))
                {
                    residual.Add("User data: " + DshHome);
                    Log("    [RESIDUAL] User data: " + DshHome);
                }
                else
                {
                    Log("    [CLEAN] User data: custom-named root kept by design (empty) -> " + DshHome);
                }
                }
            }
        if (keepRuntime)
        {
            if (Directory.Exists(DshRuntime))
            {
                Log("    [INFO] Runtime: Retained by choice -> " + DshRuntime + " (\u4fdd\u7559\u5185\u5bb9\u5df2\u4fdd\u7559\uff0c\u76ee\u5f55\u672a\u5220\u9664)");
            }
            else
            {
                Log("    [CLEAN] Runtime: " + DshRuntime + " (kept by choice but path absent)");
            }
        }
        else
        {
            if (Directory.Exists(DshRuntime))
            {
                residual.Add("Runtime: " + DshRuntime);
                Log("    [RESIDUAL] Runtime: " + DshRuntime);
            }
            else
            {
                Log("    [CLEAN] Runtime: confirmed removed -> " + DshRuntime);
            }
        }

        List<string> cleanupResiduals = CollectCleanupResiduals();

          CollectCustomShortcutResiduals(residual);
        if (cleanupResiduals.Count == 0)
        {
            Log("    [CLEAN] Registry/PATH/startup/services/tasks: no residuals found");
        }
        else
        {
            foreach (string item in cleanupResiduals)
            {
                residual.Add(item);
                Log("    [RESIDUAL] " + item);
            }
        }

        Log("  Residual count: " + residual.Count);
        return residual;
    }

    static void AddStep(string step)
    {
        reportSteps.Add(step);
        Log(step);
    }

    static void WriteJsonReport(int exitCode, string fatalError)
    {
        if (string.IsNullOrEmpty(jsonReportPath)) return;
        try
        {
            string fullPath = Path.GetFullPath(jsonReportPath);
            string dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            // Let the serializer handle all escaping; no hand-rolled JSON.
            var report = new Dictionary<string, object>();
            report["version"] = UninstallerVersion;
            report["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            report["exit_code"] = exitCode;
            report["failure_count"] = failureCount;
            report["retained"] = RetentionSummary();
            report["fatal_error"] = fatalError;
            report["steps"] = new List<string>(reportSteps);
            report["residuals"] = new List<string>(residualItems);
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            File.WriteAllText(fullPath, serializer.Serialize(report));
            Log("JSON report written: " + fullPath);
        }
        catch (Exception ex)
        {
            Log("Failed to write JSON report: " + ex.Message);
        }
    }
    static IEnumerable<string> GetKnownExtraDirectories()
    {
        if (CachedExtraDirectories != null) return CachedExtraDirectories;
        List<string> dirs = new List<string>();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Updater directories are DSH-specific names, but verify existing ones
        // before deleting so a user's own same-named folder is never removed.
        foreach (string name in KnownUpdaterDirNames)
        {
            AddVerifiedUpdaterDir(dirs, Path.Combine(localAppData, name));
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
        string npmDshDir = Path.Combine(appData, "npm", "node_modules", "@deepseek-ai", "dsh");
        if (!Directory.Exists(npmDshDir))
        {
            Log("  npm DSH package not present (skip): " + npmDshDir);
        }
        else if (IsLikelyDshNpmPackageDirectory(npmDshDir))
        {
            dirs.Add(npmDshDir);
            Log("  Verified DSH npm package: " + npmDshDir);
        }
        else
        {
            Log("  Skipping non-DSH npm package: " + npmDshDir);
        }
        CachedExtraDirectories = dirs;
        return dirs;
    }

    // Updater folders may legitimately contain only the updater exe and config,
    // so accept them when an exe is present and the name is DSH-specific.
    static void AddVerifiedUpdaterDir(List<string> dirs, string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!Directory.Exists(dir)) { Log("  Skipping non-existent updater directory: " + dir); return; }
        if (IsLikelyDshDirectory(dir) || HasDshExecutable(dir) || HasDshSignature(dir))
        {
            dirs.Add(dir); Log("  Verified DSH updater directory: " + dir); return;
        }
        string name = Path.GetFileName(dir.TrimEnd('\\'));
        bool hasExe = false;
        try { hasExe = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly).Length > 0; }
        catch (Exception ex) { Log("  Warning in AddVerifiedUpdaterDir (enumerate): " + ex.Message); }
        if (hasExe && (NameMatcher.ContainsToken(name, KnownUpdaterDirNames) || name.IndexOf("dsh", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            dirs.Add(dir); Log("  Verified DSH updater directory (exe present): " + dir);
        }
        else
        {
            Log("  Skipping non-DSH updater directory: " + dir);
        }
    }

    // Add a candidate directory only if it does not exist or is verified as a
    // DSH/Electron install; skip existing non-DSH directories to avoid deleting
    // a user's unrelated folder that happens to share the same name.
    static void AddVerifiedDshDir(List<string> dirs, string dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!Directory.Exists(dir))
        {
            Log("  Candidate directory does not exist (skip): " + dir);
            return;
        }
        if (IsLikelyDshDirectory(dir) || IsLikelyDshUserDataDirectory(dir) || IsLikelyDshEdgeAppDirectory(dir))
        {
            dirs.Add(dir);
            Log("  Verified DSH directory: " + dir);
        }
        else
        {
            Log("  Skipping non-DSH directory: " + dir + " (" + DiagnoseDshDirectory(dir) + ")");
        }
    }

    // Human-readable reason why a candidate directory was rejected, so a
    // user reviewing the log can see exactly which checks failed.
    static string DiagnoseDshDirectory(string dir)
    {
        try
        {
            bool exe = HasDshExecutable(dir);
            bool asar = File.Exists(Path.Combine(dir, "resources", "app.asar"));
            bool appDir = Directory.Exists(Path.Combine(dir, "resources", "app"));
            string pkgFile = Path.Combine(dir, "package.json");
            bool pkgJson = File.Exists(pkgFile) && PackageJsonLooksDsh(pkgFile);
            int markers = CountUserDataMarkers(dir);
            bool edge = IsLikelyDshEdgeAppDirectory(dir);
            return "exe=" + exe + ", asar=" + asar + ", appDir=" + appDir + ", packageJson=" + pkgJson + ", userDataMarkers=" + markers + ", edgeApp=" + edge;
        }
        catch (Exception ex)
        {
            return "diagnose error: " + ex.Message;
        }
    }
    static bool IsLikelyDshDirectory(string dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
            if (HasDshExecutable(dir)) return true;

            bool asarPresent = File.Exists(Path.Combine(dir, "resources", "app.asar"))
                || Directory.Exists(Path.Combine(dir, "resources", "app"))
                || Directory.Exists(Path.Combine(dir, "resources", "app.asar.unpacked"));
            bool dirNameDsh = IsDshRelatedName(Path.GetFileName(dir.TrimEnd('\\')));
            string pkgFile = Path.Combine(dir, "package.json");
            string appPkgFile = Path.Combine(dir, "resources", "app", "package.json");
            bool pkgDsh = File.Exists(pkgFile) && PackageJsonLooksDsh(pkgFile);
            bool appPkgDsh = File.Exists(appPkgFile) && PackageJsonLooksDsh(appPkgFile);

            // Tightened policy for generic-name folders: asar alone is not enough;
            // it must combine with a DSH-related directory name and a confirmed DSH
            // package.json (root or resources/app), aligned with HasDshSignature.
            if (asarPresent && dirNameDsh && (pkgDsh || appPkgDsh)) return true;
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        return false;
    }

    // Electron/Chromium userData directories (for example
    // %APPDATA%\DSH Desktop) do not contain app.asar or package.json, but
    // they carry the standard Chromium profile marker set. Require at least
    // two markers so an unrelated folder that merely has a single Preferences
    // file is not treated as DSH data.
    static bool IsLikelyDshUserDataDirectory(string dir)
    {
        try
        {
            if (IsLikelyDshDirectory(dir)) return true;
            return CountUserDataMarkers(dir) >= 2;
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        return false;
    }

    // Count the standard Electron/Chromium userData markers. Requiring at
    // least two markers avoids deleting an unrelated folder that merely has
    // a single Preferences or config.json file.
    static int CountUserDataMarkers(string dir)
    {
        int hits = 0;
        string[] markers = new string[]
        {
            "Local State", "Preferences", "Network", "Cache", "Code Cache",
            "GPUCache", "Local Storage", "Session Storage", "logs", "settings.json",
            "lockfile", "DIPS", "SharedStorage", "EBWebView", "config.json",
            "window-state.json", "single-instance.lock", "profile-patch-heal-cache.json"
        };
        foreach (string m in markers)
        {
            string full = Path.Combine(dir, m);
            if (File.Exists(full) || Directory.Exists(full))
            {
                hits += (m == "EBWebView") ? 2 : 1;
                if (hits >= 2) return hits;
            }
        }
        try
        {
            if (Directory.GetFiles(dir, "*.db").Length > 0) hits++;
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        return hits;
    }
    // The edge-shortcut variant (2633352305/DeepSeekHarness-Desktop) installs
    // only launcher.vbs + icon + install script into %LOCALAPPDATA%\dsh-edge-app.
    static bool IsLikelyDshEdgeAppDirectory(string dir)
    {
        try
        {
            string name = Path.GetFileName(dir);
            if (!name.Equals("dsh-edge-app", StringComparison.OrdinalIgnoreCase)) return false;
            return File.Exists(Path.Combine(dir, "launcher.vbs")) ||
                   File.Exists(Path.Combine(dir, "install.ps1")) ||
                   File.Exists(Path.Combine(dir, "deepseek.ico"));
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        return false;
    }

    // The edge-shortcut variant may install @deepseek-ai/dsh globally via npm.
    static bool IsLikelyDshNpmPackageDirectory(string dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir)) return false;
            string pkgFile = Path.Combine(dir, "package.json");
            if (!File.Exists(pkgFile)) return false;
            string json = File.ReadAllText(pkgFile);
            try
            {
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                var pkg = serializer.DeserializeObject(json) as Dictionary<string, object>;
                object nameObj; if (pkg != null && pkg.TryGetValue("name", out nameObj))
                {
                    string packageName = (nameObj ?? string.Empty).ToString();
                    if (packageName.IndexOf("@deepseek-ai/dsh", StringComparison.OrdinalIgnoreCase) >= 0
                        || packageName.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0
                        || packageName.StartsWith("dsh", StringComparison.OrdinalIgnoreCase)
                        || packageName.IndexOf("/dsh", StringComparison.OrdinalIgnoreCase) >= 0
                        || (pkg.ContainsKey("dsh") && packageName.Length > 0))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Fall back to the same substring signals used before.
                if (json.IndexOf("@deepseek-ai/dsh", StringComparison.OrdinalIgnoreCase) >= 0
                    || json.IndexOf("\"dsh\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
        return false;
    }

    static string ResolveShortcutTarget(string lnkPath)
    {
        try
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return string.Empty;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                try
                {
                    object target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                    object arguments = shortcut.GetType().InvokeMember("Arguments", BindingFlags.GetProperty, null, shortcut, null);
                    string targetPath = target as string ?? string.Empty;
                    string args = arguments as string ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(args)) return (targetPath + " " + args).Trim();
                    return targetPath;
                }
                finally { Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
        catch (Exception ex)
        {
            Log("  Could not resolve shortcut target: " + lnkPath + " -> " + ex.Message);
        }
        return string.Empty;
    }

    // Parses the executable path from a shortcut target string like
    // "\"C:\\Program Files\\App\\app.exe\" --flag". Only the first token is
    // used for ownership checks, so quoted paths and extra arguments never
    // break Path.GetFullPath.
    static string ExtractShortcutExePath(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        string t = target.Trim();
        if (t.StartsWith("\""))
        {
            int end = t.IndexOf('"', 1);
            return end > 1 ? t.Substring(1, end - 1) : string.Empty;
        }
        int space = t.IndexOf(' ');
        return space > 0 ? t.Substring(0, space) : t;
    }
    static bool IsDshShortcutTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        string t = ExtractShortcutExePath(target);
        if (string.IsNullOrEmpty(t)) return false;
        foreach (string dir in DshInstallDirs)
        {
            if (!string.IsNullOrEmpty(dir) && IsPathUnderAny(t, new string[] { dir })) return true;
        }
        if (!string.IsNullOrEmpty(DshRuntime) && IsPathUnderAny(t, new string[] { DshRuntime })) return true;
        if (!string.IsNullOrEmpty(DshHome) && IsPathUnderAny(t, new string[] { DshHome })) return true;
        if (t.IndexOf("dsh-edge-app", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (t.IndexOf("launcher.vbs", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static void DeleteKnownDshShortcuts()
    {
        Log("  Scanning shortcut roots...");
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
                    if (!isKnown) continue;

                    string target = ResolveShortcutTarget(file);
                    if (IsDshShortcutTarget(target))
                    {
                        Log("  Deleting known shortcut: " + file + " (target: " + target + ")");
                        DeleteFileIfExists(file);
                    }
                    else
                    {
                        Log("  Skipping same-name shortcut (target not DSH): " + file + " (target: " + (string.IsNullOrEmpty(target) ? "unknown" : target) + ", exePath: " + ExtractShortcutExePath(target) + ", installDirs: " + DshInstallDirs.Count + ")");
                    }
                }
            }
            catch (Exception ex)
            {
                LogAndCountFail("  Failed to scan shortcut root " + root + ": " + ex.Message);
            }
        }
    }

    // Report (but never delete) shortcuts whose filename is not a known DSH
    // shortcut name but whose target still points into a detected DSH install
    // dir, runtime dir or user-data dir. Users rename shortcuts; deletion is
    // deliberately left to the human.
    static void CollectCustomShortcutResiduals(List<string> residual)
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
                foreach (string file in Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(file);
                    bool isKnown = false;
                    foreach (string known in KnownShortcutNames)
                    {
                        if (known.Equals(fileName, StringComparison.OrdinalIgnoreCase)) { isKnown = true; break; }
                    }
                    if (isKnown) continue;
                    string target = ResolveShortcutTarget(file);
                    if (!IsDshShortcutTarget(target)) continue;
                    residual.Add("Custom shortcut: " + file + " -> " + target);
                    Log("    [RESIDUAL] Custom shortcut (report only, not deleted): " + file + " -> " + target);
                }
            }
            catch (Exception ex)
            {
                Log("  Warning (ignored): failed to scan shortcut root " + root + " -> " + ex.Message);
            }
        }
    }

    static string RetentionSummary()
    {
        return retentionOptions.Summary();
    }

#endregion

#region Logging & Helpers
    static void InitializeLog()
    {
        LogService.Initialize(LogFilePath);
        // winexe has no console. Probe once so Log() does not keep trying a
        // stdout write that will always throw.
        try { Console.WriteLine(string.Empty); consoleAvailable = true; }
        catch (Exception) { consoleAvailable = false; }
    }

    static void Log(string message)
    {
        LogService.Write(message);
        // winexe has no console; guarded by the one-time probe in InitializeLog.
        if (consoleAvailable)
        {
            try { Console.WriteLine(message); } catch (Exception) { consoleAvailable = false; }
        }

        try
        {
            if (progressForm != null && !progressForm.IsDisposed)
            {
                progressForm.Append(message);
            }
        }
        catch (Exception) { }
    }



      static void UpdateProgress(int percent)
      {
          try
          {
              if (progressForm != null && !progressForm.IsDisposed)
              {
                  progressForm.SetProgress(percent);
              }
          }
          catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
      }



  // Sleep in short slices while pumping UI messages. Use this instead of
  // Thread.Sleep everywhere in the uninstall pipeline.
    static void SleepWithUi(int milliseconds)
    {
        // Run() executes on a background STA thread. Application.DoEvents there
        // only pumps the worker queue and adds reentrancy risk; the main thread
        // keeps the progress UI responsive via its own DoEvents loop, so plain
        // Thread.Sleep slices are used on this side.
        while (milliseconds > 0)
        {
            int step = milliseconds > 100 ? 100 : milliseconds;
            Thread.Sleep(step);
            milliseconds -= step;
        }
    }

  // Wait for an external process/condition while keeping the UI alive.
  static bool WaitWithUi(Func<bool> done, int timeoutMs, int sliceMs = 100)
  {
      int waited = 0;
      while (waited < timeoutMs)
      {
          bool finished = false;
            try { finished = done(); } catch (Exception ex) { Log("  Warning in WaitWithUi (poll): " + ex.Message); finished = false; }
          if (finished) return true;
          SleepWithUi(sliceMs);
          waited += sliceMs;
      }
        try { return done(); } catch (Exception ex) { Log("  Warning in WaitWithUi (final poll): " + ex.Message); return false; }
  }

// Runs an external command and records both the command line and its
// exit code in the log, so every operation can be audited afterwards.
    static int RunCommandAndLog(string fileName, string arguments, int timeoutMs, bool logOutputLines = true)
    {
        try
        {
            Log("Command: " + fileName + " " + arguments);
            ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                List<string> outLines = new List<string>();
                List<string> errLines = new List<string>();
                p.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (outLines) { outLines.Add(e.Data); } } };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (errLines) { errLines.Add(e.Data); } } };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!WaitWithUi(() => p.HasExited, timeoutMs))
                {
                    try { p.Kill(); } catch (Exception ex) { Log("  Warning in RunCommandAndLog (Kill): " + ex.Message); }
                    try { p.WaitForExit(2000); } catch (Exception wex) { Log("  Warning in RunCommandAndLog (WaitForExit after Kill): " + wex.Message); }
                    Log("Command result: TIMEOUT after " + timeoutMs + " ms -> " + fileName + " " + arguments);
                    return -1;
                }
                p.WaitForExit();
                int code = p.ExitCode;
                Log("Command result: exit code " + code + " -> " + fileName + " " + arguments);
                if (logOutputLines)
                {
                    LogCommandOutput("stdout", outLines);
                    LogCommandOutput("stderr", errLines);
                }
                else
                {
                    Log("Command output: " + outLines.Count + " stdout line(s), " + errLines.Count + " stderr line(s) (debug lines suppressed).");
                }
                return code;
            }
        }
        catch (Exception ex)
        {
            Log("Command failed: " + fileName + " " + arguments + " -> " + ex.Message);
            return -1;
        }
    }

    // Logs redirected command output lines captured asynchronously while the
    // process was still running, avoiding the 4KB pipe deadlock risk.
    static void LogCommandOutput(string kind, List<string> lines)
    {
        try
        {
            if (lines == null || lines.Count == 0) return;
            string text = string.Join(Environment.NewLine, lines.ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (text.Length > 2000) text = text.Substring(text.Length - 2000) + " (truncated tail)";
            Log("Command " + kind + ": " + text);
        }
        catch (Exception ex) { Log("  Warning in LogCommandOutput(" + kind + "): " + ex.Message); }
    }

    static void ShowCompletionPopup()
    {
        try
        {
            string msg = "DSH / DeepSeek Harness \u684c\u9762\u7aef\u5378\u8f7d\u5df2\u5b8c\u6210\u3002\r\n\r\n\u4fdd\u7559\uff1a" + RetentionSummary();
            if (failureCount > 0)
            {
                msg += "\r\n\r\n\u6709 " + failureCount + " \u4e2a\u9879\u76ee\u5220\u9664\u5931\u8d25\uff0c\u8bf7\u67e5\u770b\u65e5\u5fd7\uff1a\r\n" + LogService.MainPath;
            }
            else
            {
                msg += "\r\n\r\n\u65e5\u5fd7\uff1a" + LogService.MainPath;
            }
            MessageBox.Show(msg, "\u5378\u8f7d\u5b8c\u6210", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
    }

    static void Pause()
    {
        if (silent) return;
        try
        {
            if (Console.IsInputRedirected || Console.IsOutputRedirected) return;
        }
        catch (Exception)
        {
            // winexe has no console; Pause must never log or wait here.
            return;
        }
        try
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }
        catch (Exception innerEx) { Log("  Warning (ignored): " + innerEx.Message); }
    }
#endregion

}
