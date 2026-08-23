using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Data-driven helpers shared by the DSHDesktopUninstaller partial class.
// These types are intentionally free of UI, registry and process logic so they
// can be unit-tested without a real installation.

/// <summary>One recognized DSH desktop variant and the names it publishes.</summary>
public class VariantProfile
{
    public readonly string Repo;
    public readonly string Label;
    public readonly string[] AppIds;
    public readonly string[] InstallDirNames;
    public readonly string[] ExeNames;
    public readonly string[] ProcessNames;
    public readonly string[] ShortcutNames;
    public readonly string[] UpdaterDirNames;
    public readonly string[] RoamingDirNames;
    public readonly string[] LocalAppDataDirNames;

    public VariantProfile(string repo, string label, string[] appIds, string[] installDirs, string[] exe, string[] proc, string[] shortcuts,
                          string[] updaters, string[] roaming, string[] local)
    {
        Repo = repo;
        Label = label;
        AppIds = appIds;
        InstallDirNames = installDirs;
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
    private static readonly List<VariantProfile> ProfileList = new List<VariantProfile>()
    {
        new VariantProfile("deepseek-ai", "官方 deepseek-ai/deepseek-harness",  new[]{"com.deepseek.dsh.desktop"}, new string[0], new[]{"DSH Desktop.exe","dsh-desktop.exe"}, new[]{"DSH Desktop","dsh-desktop"}, new[]{"DSH Desktop.lnk","dsh-desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater"}, new[]{"dsh-desktop","DSH Desktop"}, new[]{"DSH Desktop","dsh-desktop"}),
        new VariantProfile("myyangyunfan", "第三方 myYangyunfan/dsh_desktop",  new[]{"com.deepseek.dsh.desktop"}, new[]{"DSH Desktop","dsh-desktop"}, new[]{"DSH Desktop.exe","dsh-desktop.exe","dsh-tauri-app.exe"}, new[]{"DSH Desktop","dsh-desktop","dsh-tauri-app"}, new[]{"DSH Desktop.lnk","dsh-desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater"}, new[]{"dsh-desktop","DSH Desktop","com.deepseek.dsh.desktop"}, new[]{"DSH Desktop","dsh-desktop","com.deepseek.dsh.desktop"}),
        new VariantProfile("dataelement", "第三方 dataelement/dsh-desktop",  new[]{"io.dsh.desktop"}, new[]{"DSH Desktop","dsh-desktop"}, new[]{"DSH Desktop.exe","dsh-desktop.exe"}, new[]{"DSH Desktop","dsh-desktop"}, new[]{"DSH Desktop.lnk","dsh-desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater"}, new[]{"dsh-desktop","DSH Desktop"}, new[]{"DSH Desktop","dsh-desktop"}),
        new VariantProfile("zouyuxuan122", "第三方 zouyuxuan122/Deepseek-Harness-EAC",  new[]{"com.deepseek.dsh.desktop"}, new[]{"Deepseek Harness EAC"}, new[]{"Deepseek Harness EAC.exe"}, new[]{"Deepseek Harness EAC"}, new[]{"Deepseek Harness EAC.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater"}, new[]{"Deepseek Harness EAC"}, new[]{"Deepseek Harness EAC"}),
        new VariantProfile("amazingboycrazy", "第三方 AmazingBoyCrazy/dsh_desktop",  new[]{"io.github.amazingboycrazy.dsh-desktop"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop.exe"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater","dsh-updater"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop"}),
        new VariantProfile("easyhoov", "第三方 Easyhoov/deepseek-harness-desktop-windows",  new[]{"com.deepseek.harness.desktop"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop.exe"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop.lnk"}, new[]{"dsh-desktop-updater","dsh-launcher-updater","dsh-updater"}, new[]{"DeepSeek Harness Desktop"}, new[]{"DeepSeek Harness Desktop"}),
        new VariantProfile("steven-kid", "第三方 steven-kid/deepseek-harness-desktop",  new[]{"io.github.steven-kid.deepseek-harness-desktop"}, new[]{"DeepSeek Harness","deepseek-harness"}, new[]{"DeepSeek Harness.exe","deepseek-harness.exe"}, new[]{"DeepSeek Harness","deepseek-harness"}, new[]{"DeepSeek Harness.lnk","deepseek-harness.lnk"}, new[]{"dsh-updater","dsh-launcher-updater"}, new[]{"DeepSeek Harness","deepseek-harness"}, new[]{"DeepSeek Harness","deepseek-harness"}),
        new VariantProfile("lburny", "第三方 LBurny/deepseek-harness-desktop",  new[]{"com.dshdesktop.desktop"}, new[]{"DSHDesktop"}, new[]{"DSHDesktop.exe","dshdesktop.exe"}, new[]{"DSHDesktop","dshdesktop"}, new[]{"DSHDesktop.lnk","dshdesktop.lnk"}, new[]{"dsh-updater"}, new[]{"DSHDesktop","dshdesktop","com.dshdesktop.desktop"}, new[]{"DSHDesktop","dshdesktop","com.dshdesktop.desktop"}),
        new VariantProfile("ackow", "第三方 Ackow/dsh-desktop",  new string[0], new string[0], new[]{"DSHDesktop.exe","dshdesktop.exe"}, new[]{"DSHDesktop","dshdesktop"}, new[]{"DSHDesktop.lnk","dshdesktop.lnk"}, new string[0], new[]{"DSHDesktop","dshdesktop"}, new[]{"DSHDesktop","dshdesktop"}),
        new VariantProfile("2633352305", "第三方 2633352305/DeepSeekHarness-Desktop",  new string[0], new[]{"dsh-edge-app"}, new string[0], new string[0], new[]{"DeepSeek Harness.lnk"}, new string[0], new string[0], new[]{"dsh-edge-app"}),
        new VariantProfile("majiayu000", "第三方 majiayu000/dsh-desk",  new[]{"ai.deepseek.harness.desk"}, new[]{"DSH Desk","dsh-desk"}, new[]{"DSH Desk.exe","dsh-desk.exe"}, new[]{"DSH Desk","dsh-desk"}, new[]{"DSH Desk.lnk","dsh-desk.lnk"}, new[]{"dsh-updater"}, new[]{"DSH Desk","dsh-desk","ai.deepseek.harness.desk"}, new[]{"DSH Desk","dsh-desk","ai.deepseek.harness.desk"}),
        new VariantProfile("flashingchen", "第三方 FlashingChen/dsh-desktop-hub",  new[]{"com.dshdesktophub.app"}, new[]{"DSH Desktop Hub"}, new[]{"DSH Desktop Hub.exe"}, new[]{"DSH Desktop Hub"}, new[]{"DSH Desktop Hub.lnk"}, new[]{"dsh-updater"}, new[]{"DSH Desktop Hub"}, new[]{"DSH Desktop Hub"}),
        new VariantProfile("lxiayu", "第三方 Lxiayu/DshCockpit",  new[]{"com.dshcockpit.app"}, new string[0], new[]{"DshCockpit.exe"}, new[]{"DshCockpit"}, new[]{"DshCockpit.lnk"}, new string[0], new[]{"DshCockpit"}, new[]{"DshCockpit"}),
        new VariantProfile("ding7015869", "第三方 ding7015869-alt/dsh-web-desktop",  new string[0], new string[0], new[]{"DSH-Web.exe"}, new[]{"DSH-Web"}, new[]{"DSH-Web.lnk"}, new string[0], new[]{"DSH-Web"}, new[]{"DSH-Web"}),
        new VariantProfile("citrusli2026", "第三方 citrusli2026/dsh-electron-shell",  new[]{"io.github.citrusli2026.dsh-electron-shell"}, new[]{"dsh-desktop"}, new[]{"dsh-desktop.exe"}, new[]{"dsh-desktop"}, new[]{"dsh-desktop.lnk"}, new[]{"dsh-updater"}, new[]{"dsh-desktop"}, new[]{"dsh-desktop"}),
        new VariantProfile("hastings0714", "第三方 hastings0714/dsh-client",  new string[0], new string[0], new[]{"dsh-client.exe"}, new[]{"dsh-client"}, new[]{"dsh-client.lnk"}, new string[0], new[]{"dsh-client","dev.dsh.client"}, new[]{"dsh-client","dev.dsh.client"}),
          new VariantProfile("lai-133", "第三方 lai-133/dsh-integration",  new string[0], new[]{"dsh-desktop"}, new[]{"dsh-desktop.exe"}, new[]{"dsh-desktop"}, new[]{"dsh-desktop.lnk"}, new string[0], new[]{"dsh-desktop"}, new[]{"dsh-desktop"}),
    };

    /// <summary>Public read-only view of the catalog. The backing list is private so callers cannot cast the interface back to List and mutate it.</summary>
    public static readonly IReadOnlyList<VariantProfile> Profiles = ProfileList.AsReadOnly();


    /// <summary>Union of every AppId declared by any known profile.</summary>
    public static readonly string[] AllAppIds = BuildAllAppIds();
    static string[] BuildAllAppIds()
    {
        List<string> ids = new List<string>();
        foreach (VariantProfile p in Profiles)
        {
            if (p.AppIds == null) continue;
            foreach (string id in p.AppIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                bool seen = false;
                foreach (string existing in ids)
                {
                    if (existing.Equals(id, StringComparison.OrdinalIgnoreCase)) { seen = true; break; }
                }
                if (!seen) ids.Add(id);
            }
        }
        return ids.ToArray();
    }
    // ------------------------------------------------------------------
    // Generated all-variant name lists. Extra tokens cover historical or
    // not-yet-profiled names; a new variant is normally added as one
    // VariantProfile line above.
    // ------------------------------------------------------------------
    public static readonly string[] ExtraExeNames = new string[] {
        "deepseek-harness-desktop.exe", "DSH-Desktop.exe", "DeepSeek-harness-Desktop.exe",
        "dsh-studio.exe", "dsh-web-desktop.exe", "dsh-electron-shell.exe"
    };
    public static readonly string[] ExtraProcessNames = new string[] {
        "deepseek-harness-desktop", "DSH-Desktop", "DeepSeek-harness-Desktop",
        "dsh-studio", "dsh-web-desktop", "dsh-electron-shell"
    };
    public static readonly string[] ExtraShortcutNames = new string[] {
        "deepseek-harness-desktop.lnk", "DSH-Desktop.lnk", "DeepSeek-harness-Desktop.lnk",
        "dsh-studio.lnk", "dsh-web-desktop.lnk", "dsh-electron-shell.lnk"
    };
    public static readonly string[] ExtraRoamingDirNames = new string[] {
        "deepseek-harness-desktop", "DSH-Desktop", "DeepSeek-harness-Desktop",
        "dsh-studio", "dsh-web-desktop", "dsh-electron-shell", "dsh", ".dsh",
        "com.deepseek.dsh.desktop", "com.dshdesktop.desktop",
        "ai.deepseek.harness.desk", "dev.dsh.client"
    };
    public static readonly string[] ExtraLocalAppDataDirNames = new string[] {
        "deepseek-harness-desktop", "DSH-Desktop", "DeepSeek-harness-Desktop",
        "dsh-studio", "dsh-web-desktop", "dsh-electron-shell", "dsh", ".dsh",
        "com.deepseek.dsh.desktop", "com.dshdesktop.desktop",
        "ai.deepseek.harness.desk", "dev.dsh.client"
    };

    public static readonly string[] AllExeNames = BuildNameUnion(p => p.ExeNames, ExtraExeNames);
    public static readonly string[] AllProcessNames = BuildNameUnion(p => p.ProcessNames, ExtraProcessNames);
    public static readonly string[] AllShortcutNames = BuildNameUnion(p => p.ShortcutNames, ExtraShortcutNames);
    public static readonly string[] AllRoamingDirNames = BuildNameUnion(p => p.RoamingDirNames, ExtraRoamingDirNames);
    public static readonly string[] AllLocalAppDataDirNames = BuildNameUnion(p => p.LocalAppDataDirNames, ExtraLocalAppDataDirNames);

    static string[] BuildNameUnion(Func<VariantProfile, string[]> selector, string[] extra)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> names = new List<string>();
        foreach (VariantProfile p in Profiles)
        {
            string[] values = selector(p);
            if (values == null) continue;
            foreach (string v in values)
            {
                if (string.IsNullOrEmpty(v)) continue;
                if (seen.Add(v)) names.Add(v);
            }
        }
        if (extra != null)
        {
            foreach (string v in extra)
            {
                if (string.IsNullOrEmpty(v)) continue;
                if (seen.Add(v)) names.Add(v);
            }
        }
        return names.ToArray();
    }

    // ------------------------------------------------------------------
    // Path hints -> label. Detection.ResolveLabelFromPath consults this map
    // instead of keeping a second copy of repo/label mappings.
    // ------------------------------------------------------------------
    static readonly Dictionary<string, string> PathHintLabels = BuildPathHintLabels();
    static readonly KeyValuePair<string, string>[] SortedPathHints = PathHintLabels.OrderByDescending(kvp => kvp.Key.Length).ToArray();

    // Generated from VariantCatalog.Profiles so repo/label mappings live in
    // exactly one place. Aliases and negative hints that cannot be derived
    // from the profile table are added after the loop.
    static Dictionary<string, string> BuildPathHintLabels()
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (VariantProfile p in Profiles)
        {
            if (string.IsNullOrEmpty(p.Repo) || string.IsNullOrEmpty(p.Label)) continue;
            AddHint(map, p.Repo, p.Label);
            string last = p.Repo;
            int slash = last.LastIndexOf('/');
            if (slash >= 0) last = last.Substring(slash + 1);
            AddHint(map, last, p.Label);
            // deepseek-ai shares its generic exe/dir names with myyangyunfan.
            // Do not let the official profile own "DSH Desktop" via a path hint;
            // registry appId logic already handles the official variant.
            if (p.Repo.Equals("deepseek-ai", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.InstallDirNames != null) foreach (string n in p.InstallDirNames) AddHint(map, n, p.Label);
            if (p.ExeNames != null) foreach (string e in p.ExeNames) { string n = e.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? e.Substring(0, e.Length - 4) : e; AddHint(map, n, p.Label); }
        }
        map["deepseek_ai"] = "官方 deepseek-ai/deepseek-harness";
        map["deepseek harness desktop"] = "第三方 Easyhoov/deepseek-harness-desktop-windows";
        map["deepseek harness eac"] = "第三方 zouyuxuan122/Deepseek-Harness-EAC";
        map["dsh desktop hub"] = "第三方 FlashingChen/dsh-desktop-hub";
        map["dsh cockpit"] = "第三方 Lxiayu/DshCockpit";
        map["dsh web"] = "第三方 ding7015869-alt/dsh-web-desktop";
        map["dsh desktop"] = "第三方 myYangyunfan/dsh_desktop";
        map["dsh desk"] = "第三方 majiayu000/dsh-desk";
        map["dsh-desktop-client"] = string.Empty; // npm plugin, not a desktop app
        return map;
    }

    static void AddHint(Dictionary<string, string> map, string key, string label)
    {
        if (string.IsNullOrEmpty(key) || map.ContainsKey(key)) return;
        map[key] = label;
    }
    // Dictionary insertion order is an implementation detail. FindLabelByPath
    // sorts by key length so more specific keys ("dsh-desktop-hub") always
    // win over shorter, contained keys ("dsh-desktop").
    public static string FindLabelByPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        foreach (KeyValuePair<string, string> pair in SortedPathHints)
        {
            if (path.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return pair.Value;
            }
        }
        return string.Empty;
    }

    /// <summary>Returns the profile whose repo token appears in the given repo string. Chooses the longest matching token so shorter tokens like "dsh-desk" never shadow "dsh-desktop" purely because of profile order.</summary>
    public static VariantProfile Find(string repo)
    {
        if (string.IsNullOrEmpty(repo)) return null;
        VariantProfile best = null;
        int bestLen = 0;
        foreach (VariantProfile p in Profiles)
        {
            if (p.Repo.Length > bestLen && repo.IndexOf(p.Repo, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bestLen = p.Repo.Length;
                best = p;
            }
        }
        return best;
    }

    /// <summary>Explicit owner for app ids shared by several repositories. The
    /// registry key name is authoritative but not 1:1 with a repo; this table
    /// records which repo actually writes each key. KEY ASSUMPTION:
    /// com.deepseek.dsh.desktop is owned by myyangyunfan (the real desktop
    /// installer). The official deepseek-ai profile also declares that appId
    /// but the official monorepo ships no desktop installer and writes no
    /// uninstall registry key; if a future official desktop starts writing
    /// it, the DisplayName disambiguation path must be extended here. EAC
    /// still shares the key and is disambiguated by DisplayName in
    /// ResolveVariantLabelFromRegistryEntry before FindByAppId is consulted.</summary>
    public static readonly Dictionary<string, string> AppIdOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "com.deepseek.dsh.desktop", "myyangyunfan" },
        { "io.dsh.desktop", "dataelement" },
        { "io.github.amazingboycrazy.dsh-desktop", "amazingboycrazy" },
        { "com.deepseek.harness.desktop", "easyhoov" },
        { "io.github.steven-kid.deepseek-harness-desktop", "steven-kid" },
        { "com.dshdesktop.desktop", "lburny" },
        { "ai.deepseek.harness.desk", "majiayu000" },
        { "com.dshdesktophub.app", "flashingchen" },
        { "com.dshcockpit.app", "lxiayu" },
        { "io.github.citrusli2026.dsh-electron-shell", "citrusli2026" },
        { "dev.dsh.client", "hastings0714" }
    };

    /// <summary>Returns the profile that owns the given app id. Ownership is
    /// declared explicitly in AppIdOwner because some app ids are shared by
    /// several repositories.</summary>
    public static VariantProfile FindByAppId(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return null;
        string owner;
        if (AppIdOwner.TryGetValue(appId, out owner))
        {
            VariantProfile owned = Find(owner);
            if (owned != null) return owned;
        }
        // Fallback: first profile declaring the id (covers future profiles
        // not yet listed in AppIdOwner).
        foreach (VariantProfile p in Profiles)
        {
            foreach (string id in p.AppIds)
            {
                if (appId.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
        }
        return null;
    }

    /// <summary>Returns the profile whose process names best match a display name, skipping the official repo.</summary>
    public static VariantProfile FindByDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        string dn = displayName.Trim();
        foreach (VariantProfile p in Profiles)
        {
            if (p.Repo == "deepseek-ai") continue;
            foreach (string n in p.ProcessNames)
            {
                if (dn.Equals(n, StringComparison.OrdinalIgnoreCase)) return p;
            }
        }
        VariantProfile best = null;
        int bestLen = 0;
        foreach (VariantProfile p in Profiles)
        {
            if (p.Repo == "deepseek-ai") continue;
            foreach (string n in p.ProcessNames)
            {
                if (n.Length > bestLen && dn.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bestLen = n.Length;
                    best = p;
                }
            }
        }
        return best;
    }

    /// <summary>Returns the profile whose repo token appears in a URL/path string. Delegates to Find so both entry points use the same longest-token strategy.</summary>
    public static VariantProfile FindByRepoToken(string text)
    {
        return Find(text);
    }
}

/// <summary>Token-based name matching shared by detection and cleanup code.</summary>
public static class NameMatcher
{
    public static readonly string[] RelatedTokens = BuildRelatedTokens();
    public static readonly string[] PathTokens = BuildPathTokens();

    static string[] BuildRelatedTokens()
    {
        List<string> tokens = new List<string>();
        string[][] sources = new string[][] { VariantCatalog.AllProcessNames, VariantCatalog.AllRoamingDirNames, VariantCatalog.AllLocalAppDataDirNames, new string[] { "DSH桌面", "DeepSeek", "deepseek-ai" } };
        foreach (string[] source in sources)
        {
            foreach (string token in source)
            {
                if (string.IsNullOrEmpty(token)) continue;
                if (IsBareDshToken(token)) continue; // handled by ContainsPathSegment/EqualsToken below
                bool seen = false;
                foreach (string existing in tokens) { if (existing.Equals(token, StringComparison.OrdinalIgnoreCase)) { seen = true; break; } }
                if (!seen) tokens.Add(token);
            }
        }
        return tokens.ToArray();
    }

    static string[] BuildPathTokens()
    {
        List<string> tokens = new List<string>();
        string[][] sources = new string[][] { VariantCatalog.AllExeNames, VariantCatalog.AllShortcutNames, VariantCatalog.AllProcessNames, VariantCatalog.AllRoamingDirNames, VariantCatalog.AllLocalAppDataDirNames, new string[] { "dsh-runtime" } };
        foreach (string[] source in sources)
        {
            foreach (string token in source)
            {
                if (string.IsNullOrEmpty(token)) continue;
                if (IsBareDshToken(token)) continue; // handled by ContainsPathSegment/EqualsToken below
                bool seen = false;
                foreach (string existing in tokens) { if (existing.Equals(token, StringComparison.OrdinalIgnoreCase)) { seen = true; break; } }
                if (!seen) tokens.Add(token);
            }
        }
        return tokens.ToArray();
    }

    public static bool ContainsToken(string value, string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        // Whole-value match is always allowed (e.g. DisplayName "DSH Desktop").
        if (EqualsToken(value, tokens)) return true;

        // Otherwise the token must appear as a complete path segment
        // (delimiters: \ / | ; :). Spaces are deliberately NOT delimiters,
        // so token "DSH Desktop" does NOT match "DSH Desktop Manager" and
        // token "DSH Desk" does NOT match "DSH Desktop". This keeps prefix
        // / substring names like Dshield or DSH Desktop Tools from being
        // deleted by name heuristics.
        string[] parts = value.Split(new char[] { '\\', '/', '|', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (EqualsToken(part, tokens)) return true;
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

    static bool IsBareDshToken(string token)
    {
        return token.Length <= 4 &&
               (token.Equals("dsh", StringComparison.OrdinalIgnoreCase) ||
                token.Equals(".dsh", StringComparison.OrdinalIgnoreCase));
    }
    /// <summary>True when any path segment is exactly one of the given segments
    /// (case-insensitive). This lets bare dsh or .dsh folders count as DSH
    /// without making every path that merely contains dsh (e.g. dshield)
    /// match.</summary>
    public static bool ContainsPathSegment(string value, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] parts = value.Split(new char[] { '\\', '/', '|', ';', ':' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            foreach (string segment in segments)
            {
                if (part.Equals(segment, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
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
        RetentionOptions o = new RetentionOptions();
        o.Presets = Presets;
        o.Runtime = Runtime;
        o.ChatData = ChatData;
        o.AppSettings = AppSettings;
        o.ModelConfig = ModelConfig;
        o.OtherUserData = OtherUserData;
        o.Plugins = Plugins;
        o.Skills = Skills;
        o.PresetNames.AddRange(PresetNames);
        o.PluginNames.AddRange(PluginNames);
        o.SkillNames.AddRange(SkillNames);
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
    static readonly object writeLock = new object();

    public static string MainPath { get { return mainPath; } }
    public static string CopyPath { get { return copyPath; } }
    public static bool Available { get { return available; } }

    public static void Initialize(string path)
    {
        lock (writeLock)
        {
            available = false;
            mainPath = string.Empty;
            copyPath = string.Empty;
            string[] candidates = new string[] { path, GetExeDirLogPath(), GetCurrentDirLogPath() };
            foreach (string candidate in candidates)
            {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
            string dir = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
            Directory.CreateDirectory(dir);
            }
            if (File.Exists(candidate))
            {
            try { File.Copy(candidate, candidate + ".old", true); } catch { /* keep going; previous log preservation is best-effort */ }
            }
            File.WriteAllText(candidate, "===== DSH Desktop Uninstaller Log " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====" + Environment.NewLine);
            mainPath = candidate;
            available = true;
            break;
            }
            catch
            {
            // Intentionally empty: LogService cannot log its own failures (would recurse).
            }
            }
            }
        }

    static string GetExeDirLogPath()
    {
        try
        {
            string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            if (!string.IsNullOrEmpty(dir)) return Path.Combine(dir, "Log.log");
        }
        catch
            {
                // Intentionally empty: LogService cannot log its own failures (would recurse).
            }
        return string.Empty;
    }

    static string GetCurrentDirLogPath()
    {
        try { return Path.Combine(Directory.GetCurrentDirectory(), "Log.log"); }
        catch { return string.Empty; }
    }

    public static void SetCopyPath(string path)
    {
        lock (writeLock) { copyPath = path; }
    }


    /// <summary>Writes only the main log file under the same lock as Write,
    /// so UI timer and worker threads never interleave partial lines.</summary>
    public static void WriteToMainOnly(string message)
    {
        if (!available) return;
        lock (writeLock)
        {
            try
            {
                File.AppendAllText(mainPath, message + Environment.NewLine);
            }
            catch
            {
                // Intentionally empty: LogService cannot log its own failures (would recurse).
            }
        }
    }

    public static void Write(string message)
    {
        if (!available) return;
        lock (writeLock)
        {
            bool wroteMain = false;
            bool wroteCopy = false;
            try
            {
                File.AppendAllText(mainPath, message + Environment.NewLine);
                wroteMain = true;
            }
            catch
            {
                // Intentionally empty: LogService cannot log its own failures (would recurse).
            }
            if (!string.IsNullOrEmpty(copyPath) && !copyPath.Equals(mainPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.AppendAllText(copyPath, message + Environment.NewLine);
                    wroteCopy = true;
                }
                catch
                {
                    // Intentionally empty: LogService cannot log its own failures (would recurse).
                }
            }
            if (!wroteMain && !wroteCopy)
            {
                // Last resort: try the current directory if it differs from the failed paths.
                try { File.AppendAllText(Path.Combine(Directory.GetCurrentDirectory(), "Log.log"), message + Environment.NewLine); }
                catch
                {
                    // Intentionally empty: LogService cannot log its own failures (would recurse).
                }
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

    /// <summary>True when a partial deletion is caused ONLY by intentionally kept
    /// protected subtrees (no locked/access-denied files).</summary>
    public static bool IsExpectedPartialDeletion(bool keptAny, bool skippedAny)
    {
        return keptAny && !skippedAny;
    }

    public static bool TryDeserializeJson<T>(string json, out T result)
    {
        result = default(T);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            result = new JavaScriptSerializer().Deserialize<T>(json);
            return true;
        }
        catch
        {
            return false;
        }
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

/// <summary>Safe path normalization for deletion targets. Pure, no I/O beyond GetFullPath.</summary>
public static class PathSafety
{
    public static bool IsUnsafeRootPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        try
        {
            string t = path.Trim();
            // Device/extended-length path prefixes are never valid deletion targets here.
            if (t.StartsWith("\\\\?\\", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase)) return true;
            // Explicit drive-relative root forms: "C:", "C:\\" (and with trailing spaces).
            // Any X: form that is not followed by a backslash is a drive-relative
            // path (C:., C:foo, C:..); reject before GetFullPath resolves it
            // against the drive's current directory.
            if (Regex.IsMatch(t, @"^[A-Za-z]:(?!\\)")) return true;
            if (Regex.IsMatch(t, @"^[A-Za-z]:\\?$")) return true;
            string full = Path.GetFullPath(t);
            string root = Path.GetPathRoot(full);
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
            // Broad refuse list: profile roots and known-shell folders. Any
            // directory under these must never be deleted as a whole.
            List<string> refuse = new List<string>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string usersRoot = Path.GetDirectoryName(userProfile);
            if (!string.IsNullOrEmpty(usersRoot)) refuse.Add(usersRoot);
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            refuse.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            refuse.Add(userProfile);
            refuse.Add(Path.Combine(userProfile, "Documents"));
            refuse.Add(Path.Combine(userProfile, "Desktop"));
            refuse.Add(Path.Combine(userProfile, "Downloads"));
            refuse.Add(Path.Combine(userProfile, "Pictures"));
            refuse.Add(Path.Combine(userProfile, "Videos"));
            refuse.Add(Path.Combine(userProfile, "Music"));
            refuse.Add(Path.Combine(userProfile, "OneDrive"));
            refuse.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu"));
            string trimmed = full.TrimEnd('\\');
            foreach (string s in refuse)
            {
                if (!string.IsNullOrEmpty(s) && trimmed.Equals(s.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static string NormalizeDirForDelete(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return string.Empty;
        string t = dir.Trim().Trim('"');
        if (IsUnsafeRootPath(t)) return string.Empty;
        string full;
        try { full = Path.GetFullPath(t); }
        catch { return string.Empty; }
        if (IsUnsafeRootPath(full)) return string.Empty;
        return full.TrimEnd('\\');
    }
}
