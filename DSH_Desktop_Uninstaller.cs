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

#region Fields, Constants & Paths
    static bool silent = false;
    static bool dryRun = false;
    static bool helpRequested = false;
    static string manualInstallDir = string.Empty;
    static ProgressForm progressForm = null;
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
    static string[] variantAppIds = null;

    static string[] KnownExeNames { get { return variantExeNames ?? AllExeNames; } }
    static string[] KnownProcessNames { get { return variantProcessNames ?? AllProcessNames; } }
    static string[] KnownShortcutNames { get { return variantShortcutNames ?? AllShortcutNames; } }
    static string[] KnownUpdaterDirNames { get { return variantUpdaterDirNames ?? AllUpdaterDirNames; } }
    static string[] KnownRoamingDirNames { get { return variantRoamingDirNames ?? AllRoamingDirNames; } }
    static string[] KnownLocalAppDataDirNames { get { return variantLocalAppDataDirNames ?? AllLocalAppDataDirNames; } }
    static string[] TargetAppIds { get { return variantAppIds ?? KnownAppIds; } }


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

    static string DshInstallDir = string.Empty;
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
            string repo = ExtractRepoFromLabel(DetectedVariantLabel);
            if (string.IsNullOrEmpty(repo)) return true;

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

#region Entry Point & CLI Parsing
    static string BuildDeletionTargetsSummary()
    {
        List<string> targets = new List<string>();
        if (!string.IsNullOrEmpty(DshInstallDir))
        {
            targets.Add("安装目录：" + DshInstallDir);
        }
        List<string> extraDirs = new List<string>();
        foreach (string dir in GetKnownExtraDirectories())
        {
            if (string.IsNullOrEmpty(dir)) continue;
            if (!string.IsNullOrEmpty(DshInstallDir) && dir.Equals(DshInstallDir, StringComparison.OrdinalIgnoreCase)) continue;
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
            Log("Scheduling temp cleanup command: cmd.exe " + cmd);
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            Process.Start(psi);
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
        DshInstallDir = SafeResolveDshInstallDir();
        DetectedRunningDshDir = SafeFindRunningDshInstallDir();
        DetectedVariantLabel = SafeResolveVariantLabel();
        VariantProfileApplied = ApplyVariantProfile();
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
                Console.WriteLine("Administrator rights are required. Right-click and select Run as administrator.");
                Console.WriteLine(ex.Message);
                Pause();
                return 1;
            }
        }

        ParseArgs(args);
        if (!string.IsNullOrEmpty(logOverridePath))
        {
            LogFilePath = logOverridePath;
        }
        InitializeRuntime();
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
        Console.WriteLine("    /KeepRuntime           保留 .dsh-runtime");
        Console.WriteLine("    /KeepAll               保留以上全部");
    }

    static void RunDryRun()
    {
        Log("===== DSH Desktop Uninstaller Dry-Run =====");
        Log("安装目录:   " + (string.IsNullOrEmpty(DshInstallDir) ? "(未检测到)" : DshInstallDir));
        Log("当前DSH:    " + DetectedVariantLabel);
        Log("用户数据:   " + DshHome);
        Log("运行时:     " + DshRuntime);
        Log("保留:       " + RetentionSummary());
        Log("");
        Log("将删除的主要内容:");
        Log("  - 安装目录: " + (string.IsNullOrEmpty(DshInstallDir) ? "(未检测到，跳过)" : DshInstallDir));
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
            if (!string.IsNullOrEmpty(DshInstallDir)) doomed.Add(DshInstallDir);
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
                File.Copy(LogFilePath, copyPath, true);
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
                        File.Copy(LogFilePath, copyPath, true);
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
            if (string.IsNullOrEmpty(DshInstallDir)) return;
            string prevLog = Path.Combine(DshInstallDir, "Log.log");
            if (!File.Exists(prevLog)) return;
            if (prevLog.Equals(LogFilePath, StringComparison.OrdinalIgnoreCase)) return;
            string text = File.ReadAllText(prevLog);
            File.AppendAllText(LogFilePath, Environment.NewLine + "----- Log from the process that ran inside the install directory -----" + Environment.NewLine + text);
            Log("Merged previous log from install directory: " + prevLog);
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
                DshInstallDir = manualInstallDir;
                Log("Uninstall mode: manual install dir -> " + manualInstallDir);
                // Re-derive the variant label and targeted cleanup lists so the
                // GUI label and Known* arrays match the manually selected dir.
                string manualLabel = ResolveLabelFromPath(DshInstallDir);
                if (string.IsNullOrEmpty(manualLabel)) manualLabel = "未知";
                DetectedVariantLabel = manualLabel;
                ApplyVariantProfile();
                Log("Variant label updated from manual install dir: " + DetectedVariantLabel);
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

        PreserveLogCopyIfNeeded();
        KillDSHProcesses();
        DeleteDirectoryWithRetry(DshInstallDir);
        Log("[1b/9] Deleting known extra directories...");
        foreach (string dir in GetKnownExtraDirectories())
        {
            if (string.IsNullOrEmpty(dir)) continue;
            if (!string.IsNullOrEmpty(DshInstallDir) && dir.Equals(DshInstallDir, StringComparison.OrdinalIgnoreCase)) continue;
            if (Directory.Exists(dir))
            {
                DeleteDirectoryWithRetry(dir);
            }
        }
        Log("[1c/9] Deleting DSH shortcuts...");
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

    static void DeleteKnownDshShortcuts()
    {
        Log("[1c] Scanning shortcut roots...");
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
