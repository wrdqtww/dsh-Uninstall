using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

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
        "DeepSeek-harness-Desktop.exe",
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
        "DeepSeek-harness-Desktop",
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
        "DeepSeek-harness-Desktop.lnk",
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
        "DeepSeek-harness-Desktop",
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
        "DSH Desk",
        "dsh",
        ".dsh"
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
        "DeepSeek-harness-Desktop",
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
        "DSH Desk",
        "dsh",
        ".dsh"
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
        catch
        {
        }
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
            variantExeNames = null;
            variantProcessNames = null;
            variantShortcutNames = null;
            variantUpdaterDirNames = null;
            variantRoamingDirNames = null;
            variantLocalAppDataDirNames = null;
            variantAppIds = null;
            variantInstallDirNames = null;

            if (DetectedVariantLabels == null || DetectedVariantLabels.Count != 1)
            {
                Log("Multiple (or no) DSH variants detected; keeping generic cleanup lists.");
                return false;
            }
            string repo = ExtractRepoFromLabel(DetectedVariantLabels[0]);
            if (string.IsNullOrEmpty(repo)) return false;

            VariantProfile p = VariantCatalog.Find(repo);
            if (p != null)
            {
                variantExeNames = p.ExeNames;
                variantProcessNames = p.ProcessNames;
                variantShortcutNames = p.ShortcutNames;
                variantUpdaterDirNames = p.UpdaterDirNames;
                variantRoamingDirNames = p.RoamingDirNames;
                variantLocalAppDataDirNames = p.LocalAppDataDirNames;
                variantAppIds = p.AppIds;
                variantInstallDirNames = p.InstallDirNames;
                Log("Variant profile applied for: " + repo);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log("ApplyVariantProfile failed: " + ex.Message);
            return false;
        }
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
    static int failureCount = 0;

    // Version shown in the log so a support report can reproduce behavior.
    static readonly string UninstallerVersion = "1.2.2";


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
    static void ScheduleSelfTempDeletion()
    {
        if (string.IsNullOrEmpty(selfTempDir)) return;
        try
        {
            string cmd = "/C ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"" + selfTempDir + "\"";
            Log("Scheduling temp cleanup command: cmd.exe " + cmd);
            RunCommandAndLog("cmd.exe", cmd, 15000);
        }
        catch
        {
        }
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

        LogFilePath = ResolveLogFilePath();
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
                MessageBox.Show("\u9700\u8981\u7ba1\u7406\u5458\u6743\u9650\uff0c\u8bf7\u53f3\u952e\u4ee5\u7ba1\u7406\u5458\u8eab\u4efd\u8fd0\u884c\u3002" + Environment.NewLine + Environment.NewLine + ex.Message,
                    "DSH \u684c\u9762\u7aef\u5378\u8f7d\u5668", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        ParseArgs(args);
        InitializeRuntime();
        if (!string.IsNullOrEmpty(logOverridePath))
        {
            LogFilePath = logOverridePath;
        }
        InitializeLog();
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
                psi.Arguments = PureHelpers.BuildQuotedArguments(args);
                Log("Child command: " + tempExe + " " + psi.Arguments);
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

            // For interactive mode the confirmation window has already
            // switched to the progress view and is stored in progressForm.
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
            MessageBox.Show("\u5378\u8f7d\u8fc7\u7a0b\u53d1\u751f\u9519\u8bef\uff1a\r\n" + ex.Message + "\r\n\r\n\u65e5\u5fd7\uff1a\r\n" + LogService.MainPath, "DSH \u684c\u9762\u7aef\u5378\u8f7d\u5668", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ScheduleSelfTempDeletion();
            Pause();
            return 1;
        }

        ScheduleSelfTempDeletion();
        if (!silent) ShowCompletionPopup();
        Pause();
        // In silent mode report cleanup failures via a non-zero exit code so
        // scripts can detect a partial uninstall; interactive mode returns 0.
        return (silent && failureCount > 0) ? 1 : 0;
    }

    static void ParseArgs(string[] args)
    {
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

            foreach (ArgSpec spec in specs)
            {
                if (spec.Matches(arg))
                {
                    spec.Apply(value);
                    break;
                }
            }
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
            new ArgSpec(new[] { "/InstallDir", "-InstallDir", "/Dir", "-Dir" }, v => { manualInstallDir = (v ?? string.Empty).Trim().Trim('"').TrimEnd('\\'); }),
            new ArgSpec(new[] { "/Log", "-Log", "/LogFile", "-LogFile" }, v => { if (!string.IsNullOrWhiteSpace(v)) logOverridePath = v.Trim().Trim('"'); }),
            new ArgSpec(new[] { "/DryRun", "-DryRun", "/Preview", "-Preview" }, v => { dryRun = true; }),
            new ArgSpec(new[] { "/help", "-help", "/?", "-?", "-h" }, v => { helpRequested = true; }),
        };
    }

    // BuildQuotedArguments / EscapeWindowsArg / ParsePresetNames moved to PureHelpers.

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
        Console.WriteLine("    /Log=<路径>           指定日志文件路径（默认 exe 同目录 Log.log）");
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
        Console.WriteLine("    /KeepVision           \u4fdd\u7559 dsh-vision \u63d2\u4ef6\uff08\u7b49\u540c\u4e8e /KeepPlugins=@dsh-external/dsh-vision\uff09");
        Console.WriteLine("    /KeepRuntime           保留 .dsh-runtime");
        Console.WriteLine("    /KeepAll               保留以上全部");
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
            if (string.IsNullOrEmpty(exeDir)) return false;
            foreach (string dir in DshInstallDirs)
            {
                if (!string.IsNullOrEmpty(dir) && exeDir.Equals(dir, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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
        catch
        {
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
            catch
            {
            }
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
        catch
        {
        }
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
        catch
        {
        }
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
        catch
        {
        }
        return false;
    }

    static void Run()
    {
        if (!string.IsNullOrEmpty(manualInstallDir))
        {
            if (Directory.Exists(manualInstallDir) && IsSafeInstallDir(manualInstallDir) &&
                (HasDshExecutable(manualInstallDir) || HasDshSignature(manualInstallDir)))
            {
                DshInstallDirs.Clear();
                DshInstallDirs.Add(manualInstallDir);
                DshInstallDir = manualInstallDir;
                Log("Uninstall mode: manual install dir -> " + manualInstallDir);
                // Re-derive the variant label and targeted cleanup lists so the
                // GUI label and Known* arrays match the manually selected dir.
                string manualLabel = ResolveLabelFromPath(DshInstallDir);
                if (string.IsNullOrEmpty(manualLabel)) manualLabel = "未知";
                DetectedVariantLabel = manualLabel;
                DetectedVariantLabels.Clear();
                DetectedVariantLabels.Add(manualLabel);
                  VariantProfileApplied = ApplyVariantProfile();
                Log("Variant label updated from manual install dir: " + DetectedVariantLabel);
            }
            else
            {
                Log("WARNING: manual install dir does not look like a DSH desktop, ignored: " + manualInstallDir);
            }
        }

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

        PreserveLogCopyIfNeeded();
        KillDSHProcesses();
        Log("[2/9] Deleting install directories...");
        foreach (string dir in DshInstallDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            DeleteDirectoryWithRetry(dir);
        }
        Log("[3/9] Deleting known extra directories...");
        foreach (string dir in GetKnownExtraDirectories())
        {
            if (string.IsNullOrEmpty(dir)) continue;
            bool isInstall = false;
            foreach (string idir in DshInstallDirs)
            {
                if (!string.IsNullOrEmpty(idir) && dir.Equals(idir, StringComparison.OrdinalIgnoreCase)) { isInstall = true; break; }
            }
            if (isInstall) continue;
            if (Directory.Exists(dir))
            {
                DeleteDirectoryWithRetry(dir);
            }
        }
        Log("[4/9] Deleting DSH shortcuts...");
        DeleteKnownDshShortcuts();
        DeleteRegistryKeys();
        CleanupRunKeys();
        Log("[5/9] Cleaning PATH entries...");
        CleanupMachinePath();
        CleanupUserPath();
        BroadcastEnvironmentChange();
        Log("[6/9] Preserving selected plugins...");
        PreserveSelectedPlugins();
        Log("[7/9] Cleaning DSH user data...");
        CleanDshHome();
        Log("[8/9] Cleaning runtime...");
        if (!keepRuntime)
        {
            DeleteDirectoryWithRetry(DshRuntime);
        }
        Log("[9/9] Cleaning temp files and scanning residuals...");
        CleanupTemp();
        RunResidualScan();

        Log("");
        Log("===== Uninstall finished =====");
        Log("Removed DSH Desktop / DeepSeek Harness app, updaters, caches, shortcuts, uninstall registry key and DSH user data.");
        Log("Kept: " + RetentionSummary());
        Log("Advice: reboot or log off/on to fully refresh the PATH environment.");
    }



    static void RunResidualScan()
    {
        Log("  Residual scan:");
        if (DshInstallDirs.Count == 0)
        {
            Log("    Install dir: not applicable (not detected)");
        }
        foreach (string dir in DshInstallDirs)
        {
            Log("    Install dir: " + ResidualStatus(dir));
        }
        Log("    User data:   " + ResidualStatus(DshHome));
        Log("    Runtime:     " + ResidualStatus(DshRuntime));
    }

    static string ResidualStatus(string path)
    {
        if (string.IsNullOrEmpty(path)) return "not applicable (not detected)";
        return Directory.Exists(path) ? "RESIDUAL still exists -> " + path : "confirmed removed (" + path + ")";
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
        string npmDshDir = Path.Combine(appData, "npm", "node_modules", "@deepseek-ai", "dsh");
        if (!Directory.Exists(npmDshDir) || IsLikelyDshNpmPackageDirectory(npmDshDir))
        {
            dirs.Add(npmDshDir);
        }
        else
        {
            Log("  Skipping non-DSH npm package: " + npmDshDir);
        }
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
        if (IsLikelyDshDirectory(dir) || IsLikelyDshUserDataDirectory(dir) || IsLikelyDshEdgeAppDirectory(dir))
        {
            dirs.Add(dir);
            Log("  Verified DSH directory: " + dir);
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
            if (File.Exists(Path.Combine(dir, "resources", "app.asar"))) return true;
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
            string[] markers = new string[]
            {
                "Local State", "Preferences", "Network", "Cache", "Code Cache",
                "GPUCache", "Local Storage", "Session Storage", "logs", "settings.json",
                "lockfile", "DIPS", "SharedStorage", "EBWebView", "config.json"
            };
            int hits = 0;
            foreach (string m in markers)
            {
                string full = Path.Combine(dir, m);
                if (File.Exists(full) || Directory.Exists(full))
                {
                    hits++;
                    if (hits >= 2) return true;
                }
            }
            try
            {
                if (Directory.GetFiles(dir, "*.db").Length > 0) hits++;
            }
            catch
            {
            }
            return hits >= 2;
        }
        catch
        {
        }
        return false;
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
        catch
        {
        }
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
            return json.IndexOf("@deepseek-ai/dsh", StringComparison.OrdinalIgnoreCase) >= 0
                || (json.IndexOf("\"dsh\"", StringComparison.OrdinalIgnoreCase) >= 0
                    && json.IndexOf("\"name\"", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch
        {
        }
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
            catch (Exception ex)
            {
                Log("  Failed to scan shortcut root " + root + ": " + ex.Message);
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
    }

    static void Log(string message)
    {
        LogService.Write(message);
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


    // Runs an external command and records both the command line and its
    // exit code in the log, so every operation can be audited afterwards.
    static int RunCommandAndLog(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            Log("Command: " + fileName + " " + arguments);
            ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    Log("Command result: TIMEOUT after " + timeoutMs + " ms -> " + fileName + " " + arguments);
                    return -1;
                }
                int code = p.ExitCode;
                Log("Command result: exit code " + code + " -> " + fileName + " " + arguments);
                return code;
            }
        }
        catch (Exception ex)
        {
            Log("Command failed: " + fileName + " " + arguments + " -> " + ex.Message);
            return -1;
        }
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

}
