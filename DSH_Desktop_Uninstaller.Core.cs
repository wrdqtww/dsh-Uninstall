using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Data-driven helpers shared by the DSHDesktopUninstaller partial class.
// These types are intentionally free of UI, registry and process logic so they
// can be unit-tested without a real installation.

/// <summary>One recognized DSH desktop variant and the names it publishes.</summary>
public class VariantProfile
{
    public readonly string Repo;
    public readonly string[] ExeNames;
    public readonly string[] ProcessNames;
    public readonly string[] ShortcutNames;
    public readonly string[] UpdaterDirNames;
    public readonly string[] RoamingDirNames;
    public readonly string[] LocalAppDataDirNames;

    public VariantProfile(string repo, string[] exe, string[] proc, string[] shortcuts,
                          string[] updaters, string[] roaming, string[] local)
    {
        Repo = repo;
        ExeNames = exe;
        ProcessNames = proc;
        ShortcutNames = shortcuts;
        UpdaterDirNames = updaters;
        RoamingDirNames = roaming;
        LocalAppDataDirNames = local;
    }
}

/// <summary>Known repository -> cleanup profile. Keys are matched by substring, in order.</summary>
public static class VariantCatalog
{
    public static readonly List<VariantProfile> Profiles = new List<VariantProfile>()
    {
        new VariantProfile("deepseek-ai",          new[]{"DSH Desktop.exe","dsh-desktop.exe"}, new[]{"DSH Desktop","dsh-desktop"}, new[]{"DSH Desktop.lnk","dsh-desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater"}, new[]{"dsh-desktop","DSH Desktop"}, new[]{"DSH Desktop","dsh-desktop"}),
        new VariantProfile("myyangyunfan",         new[]{"DSH Desktop.exe","dsh-desktop.exe"}, new[]{"DSH Desktop","dsh-desktop"}, new[]{"DSH Desktop.lnk","dsh-desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater"}, new[]{"dsh-desktop","DSH Desktop"}, new[]{"DSH Desktop","dsh-desktop"}),
        new VariantProfile("dataelement",          new[]{"DSH Desktop.exe","dsh-desktop.exe"}, new[]{"DSH Desktop","dsh-desktop"}, new[]{"DSH Desktop.lnk","dsh-desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater"}, new[]{"dsh-desktop","DSH Desktop"}, new[]{"DSH Desktop","dsh-desktop"}),
        new VariantProfile("zouyuxuan122",         new[]{"Deepseek Harness EAC.exe"}, new[]{"Deepseek Harness EAC"}, new[]{"Deepseek Harness EAC.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater"}, new[]{"Deepseek Harness EAC"}, new[]{"Deepseek Harness EAC"}),
        new VariantProfile("amazingboycrazy",      new[]{"DeepSeek Harness Desktop.exe"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater","dsh-updater"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop"}),
        new VariantProfile("easyhoov",             new[]{"DeepSeek Harness Desktop.exe"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater","dsh-updater"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop"}),
        new VariantProfile("steven-kid",           new[]{"DeepSeek Harness.exe","deepseek-harness.exe"}, new[]{"DeepSeek Harness","deepseek-harness"}, new[]{"DeepSeek Harness.lnk","deepseek-harness.lnk"}, new[]{"dsh-updater","dsh-launcher-updater"}, new[]{"DeepSeek Harness","deepseek-harness"}, new[]{"DeepSeek Harness","deepseek-harness"}),
        new VariantProfile("lburny",               new[]{"DSHDesktop.exe","dshdesktop.exe"}, new[]{"DSHDesktop","dshdesktop"}, new[]{"DSHDesktop.lnk","dshdesktop.lnk"}, new[]{"dsh-updater"}, new[]{"DSHDesktop","dshdesktop"}, new[]{"DSHDesktop","dshdesktop"}),
        new VariantProfile("ackow",                new[]{"DSHDesktop.exe","dshdesktop.exe"}, new[]{"DSHDesktop","dshdesktop"}, new[]{"DSHDesktop.lnk","dshdesktop.lnk"}, new string[0], new[]{"DSHDesktop","dshdesktop"}, new[]{"DSHDesktop","dshdesktop"}),
        new VariantProfile("2633352305",           new string[0], new string[0], new[]{"DeepSeek Harness.lnk"}, new string[0], new string[0], new[]{"dsh-edge-app"}),
        new VariantProfile("majiayu000",           new[]{"DSH Desk.exe"}, new[]{"DSH Desk"}, new[]{"DSH Desk.lnk"}, new[]{"dsh-updater"}, new[]{"DSH Desk"}, new[]{"DSH Desk"}),
        new VariantProfile("flashingchen",         new[]{"DSH Desktop Hub.exe"}, new[]{"DSH Desktop Hub"}, new[]{"DSH Desktop Hub.lnk"}, new[]{"dsh-updater"}, new[]{"DSH Desktop Hub"}, new[]{"DSH Desktop Hub"}),
        new VariantProfile("lxiayu",               new[]{"DshCockpit.exe"}, new[]{"DshCockpit"}, new[]{"DshCockpit.lnk"}, new string[0], new[]{"DshCockpit"}, new[]{"DshCockpit"}),
        new VariantProfile("ding7015869",          new[]{"DSH-Web.exe"}, new[]{"DSH-Web"}, new[]{"DSH-Web.lnk"}, new string[0], new[]{"DSH-Web"}, new[]{"DSH-Web"}),
        new VariantProfile("citrusli2026",         new[]{"dsh-desktop.exe"}, new[]{"dsh-desktop"}, new[]{"dsh-desktop.lnk"}, new[]{"dsh-updater"}, new[]{"dsh-desktop"}, new[]{"dsh-desktop"}),
        new VariantProfile("hastings0714",         new[]{"dsh-client.exe"}, new[]{"dsh-client"}, new[]{"dsh-client.lnk"}, new string[0], new[]{"dsh-client"}, new[]{"dsh-client"}),
    };

    /// <summary>Returns the first profile whose repo token is contained in the given repo string, or null.</summary>
    public static VariantProfile Find(string repo)
    {
        if (string.IsNullOrEmpty(repo)) return null;
        foreach (VariantProfile p in Profiles)
        {
            if (repo.IndexOf(p.Repo, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return p;
            }
        }
        return null;
    }
}

/// <summary>Token-based name matching shared by detection and cleanup code.</summary>
public static class NameMatcher
{
    public static readonly string[] RelatedTokens = new string[]
    {
        "DSH Desktop", "DSH桌面", "dsh-desktop", "DeepSeek Harness Desktop", "DeepSeek Harness",
        "deepseek-harness", "DSHDesktop", "dshdesktop", "dsh-edge-app", "DSH-Desktop",
        "DeepSeek-runtime-Desktop", "dsh-desk", "dsh-studio", "dsh-desktop-hub",
        "dsh-cockpit", "dsh-client", "dsh-web-desktop", "dsh-electron-shell",
        "Deepseek Harness EAC", "DSH Desktop Hub", "DSH-Web", "DshCockpit", "DSH Desk", "dsh desk"
    };

    public static readonly string[] PathTokens = new string[]
    {
        "DSH Desktop", "dsh-desktop", "DeepSeek Harness Desktop", "DeepSeek Harness",
        "deepseek-harness", "DSHDesktop", "dshdesktop", "dsh-edge-app", "DSH-Desktop",
        "DeepSeek-runtime-Desktop", "dsh-desk", "dsh-studio", "dsh-desktop-hub",
        "dsh-cockpit", "dsh-client", "dsh-web-desktop", "dsh-electron-shell",
        "Deepseek Harness EAC", "DSH Desktop Hub", "DSH-Web", "DshCockpit", "DSH Desk", "dsh desk", "dsh-runtime"
    };

    public static bool ContainsToken(string value, string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (string token in tokens)
        {
            if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    public static bool EqualsToken(string value, string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (string token in tokens)
        {
            if (value.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>Retention choices selected via CLI or the GUI popup.</summary>
public class RetentionOptions
{
    public bool Presets;
    public bool Runtime;
    public bool ChatData;
    public bool AppSettings;
    public bool ModelConfig;
    public bool OtherUserData;
    public bool Plugins;
    public bool Skills;
    public readonly List<string> PresetNames = new List<string>();
    public readonly List<string> PluginNames = new List<string>();
    public readonly List<string> SkillNames = new List<string>();

    public RetentionOptions Copy()
    {
        RetentionOptions o = (RetentionOptions)MemberwiseClone();
        return o;
    }

    public string Summary()
    {
        List<string> kept = new List<string>();
        if (Presets)
        {
            kept.Add(PresetNames.Count > 0 ? ".agent-presets (" + string.Join(", ", PresetNames.ToArray()) + ")" : ".agent-presets (all)");
        }
        if (ChatData)
        {
            kept.Add("聊天数据 (sessions)");
        }
        if (AppSettings)
        {
            kept.Add("应用设置 (settings.yaml)");
        }
        if (ModelConfig)
        {
            kept.Add("模型配置 (credentials + settings.yaml 模型部分)");
        }
        if (OtherUserData)
        {
            kept.Add("其他 .dsh 数据 (graph-memory/storages/profiles 等)");
        }
        if (Runtime)
        {
            kept.Add(".dsh-runtime");
        }
        if (Plugins)
        {
            kept.Add(PluginNames.Count > 0 ? "插件 (" + string.Join(", ", PluginNames.ToArray()) + ")" : "插件 (all)");
        }
        if (Skills)
        {
            kept.Add(SkillNames.Count > 0 ? "skills (" + string.Join(", ", SkillNames.ToArray()) + ")" : "skills (all)");
        }
        return kept.Count == 0 ? "(none)" : string.Join(", ", kept.ToArray());
    }
}

/// <summary>One command-line option declaration.</summary>
public class ArgSpec
{
    public readonly string[] Names;
    public readonly Action<string> Apply;

    public ArgSpec(string[] names, Action<string> apply)
    {
        Names = names;
        Apply = apply;
    }

    public bool Matches(string arg)
    {
        foreach (string name in Names)
        {
            if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

/// <summary>File logging facade used by the uninstaller.</summary>
public static class LogService
{
    static bool available;
    static string mainPath;
    static string copyPath;

    public static string MainPath { get { return mainPath; } }
    public static string CopyPath { get { return copyPath; } }
    public static bool Available { get { return available; } }

    public static void Initialize(string path)
    {
        available = false;
        mainPath = path;
        copyPath = string.Empty;
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, "===== DSH Desktop Uninstaller Log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====" + Environment.NewLine);
            available = true;
        }
        catch
        {
        }
    }

    public static void SetCopyPath(string path)
    {
        copyPath = path;
    }

    public static void Write(string message)
    {
        if (!available) return;
        try
        {
            File.AppendAllText(mainPath, message + Environment.NewLine);
        }
        catch
        {
        }
        if (!string.IsNullOrEmpty(copyPath) && !copyPath.Equals(mainPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.AppendAllText(copyPath, message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}

/// <summary>Pure string/list helpers with no file, registry or process I/O.</summary>
public static class PureHelpers
{
    public static string BuildQuotedArguments(string[] args)
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
            quoted.Add("\"" + EscapeWindowsArg(a) + "\"");
        }
        return string.Join(" ", quoted.ToArray());
    }

    public static string EscapeWindowsArg(string a)
    {
        StringBuilder sb = new StringBuilder();
        int backslashes = 0;
        foreach (char c in a)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
                continue;
            }
            sb.Append('\\', backslashes);
            sb.Append(c);
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2);
        return sb.ToString();
    }

    public static List<string> ParsePresetNames(string value)
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
}
