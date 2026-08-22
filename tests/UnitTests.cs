using System;
using System.Collections.Generic;

/// <summary>
/// Minimal dependency-free unit tests for the pure helper types in
/// DSH_Desktop_Uninstaller.Core.cs. Compiled and run by
/// tests/RunUnitTests.ps1 (invoked from build-uninstaller.ps1).
/// </summary>
static class UnitTests
{
    static int failures = 0;

    static void Check(string name, bool condition)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
        if (!condition) failures++;
    }

    static void CheckEq(string name, string expected, string actual)
    {
        Check(name, string.Equals(expected, actual, StringComparison.Ordinal));
    }

    static int Main()
    {
        // PureHelpers.BuildQuotedArguments / EscapeWindowsArg
        CheckEq("quote empty", "\"\"", PureHelpers.BuildQuotedArguments(new[] { "" }));
        CheckEq("quote space", "\"a b\"", PureHelpers.BuildQuotedArguments(new[] { "a b" }));
        CheckEq("quote embedded quote", "\"a\\\"b\"", PureHelpers.BuildQuotedArguments(new[] { "a\"b" }));
        CheckEq("no quote simple", "abc", PureHelpers.BuildQuotedArguments(new[] { "abc" }));
        CheckEq("backslash before quote", "\"a\\\\\\\"b\"", PureHelpers.BuildQuotedArguments(new[] { "a\\\"b" }));
        CheckEq("parse list csv", "a|b", string.Join("|", PureHelpers.ParsePresetNames("a, b").ToArray()));
        CheckEq("parse list semicolon and fullwidth", "a|b|c", string.Join("|", PureHelpers.ParsePresetNames("a;b，c").ToArray()));
        CheckEq("parse excludes star", "", string.Join("|", PureHelpers.ParsePresetNames("*, all").ToArray()));

        // NameMatcher
        Check("contains token whole-value", NameMatcher.ContainsToken("Dsh Desktop", new[] { "dsh desktop" }));
        Check("contains token path segment", NameMatcher.ContainsToken(@"C:\DSH Desktop\bin", new[] { "dsh desktop" }));
        Check("contains token rejects prefix name", !NameMatcher.ContainsToken("Dsh Desktop Manager", new[] { "dsh desktop" }));
        Check("contains token rejects substring", !NameMatcher.ContainsToken("dshield", new[] { "dsh" }));
        Check("equals token case-insensitive", NameMatcher.EqualsToken("DSH Desktop.exe", new[] { "dsh desktop.exe" }));
        Check("path segment bare dsh", NameMatcher.ContainsPathSegment(@"C:\dsh\bin", "dsh", ".dsh"));
        Check("path segment rejects dshield", !NameMatcher.ContainsPathSegment(@"C:\dshield", "dsh", ".dsh"));

        // RetentionOptions.Copy deep copy
        RetentionOptions o = new RetentionOptions();
        o.Presets = true;
        o.PresetNames.Add("agent-sc");
        RetentionOptions c = o.Copy();
        c.PresetNames.Clear();
        Check("copy deep copies lists", o.PresetNames.Count == 1 && c.PresetNames.Count == 0);
        Check("copy copies booleans", c.Presets == true);

        // ArgSpec.Matches
        ArgSpec spec = new ArgSpec(new[] { "/S", "-S" }, v => { });
        Check("argspec case-insensitive", spec.Matches("/s") && spec.Matches("-S"));

        // VariantCatalog: shared com.deepseek.dsh.desktop must resolve to
        // myyangyunfan (third party) first; official only when no third party
        // claims the appId.
        VariantProfile shared = VariantCatalog.FindByAppId("com.deepseek.dsh.desktop");
        Check("shared appId resolves to third party", shared != null && shared.Repo == "myyangyunfan");
        Check("find by repo substring", VariantCatalog.Find("steven-kid") != null);
        Check("official find", VariantCatalog.Find("deepseek-ai") != null);

        // PathSafety: H1 regression — drive roots and drive-relative forms
        // must never become deletion targets.
        Check("unsafe C backslash", PathSafety.IsUnsafeRootPath("C:\\"));
        Check("unsafe C colon", PathSafety.IsUnsafeRootPath("C:"));
        Check("unsafe C colon dot", PathSafety.IsUnsafeRootPath("C:."));
        Check("unsafe C colon foo", PathSafety.IsUnsafeRootPath("C:foo"));
        Check("unsafe C colon dotdot", PathSafety.IsUnsafeRootPath("C:.."));
        Check("unsafe C colon dot slash foo", PathSafety.IsUnsafeRootPath("C:.\\foo"));
        Check("unsafe user profile", PathSafety.IsUnsafeRootPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        // L2 three-state deletion helper
        Check("expected partial kept only", PureHelpers.IsExpectedPartialDeletion(true, false));
        Check("not expected when locked files skipped", !PureHelpers.IsExpectedPartialDeletion(false, true));
        Check("not expected mixed kept+skipped", !PureHelpers.IsExpectedPartialDeletion(true, true));
        Check("not expected deleted", !PureHelpers.IsExpectedPartialDeletion(false, false));

        Console.WriteLine(failures == 0 ? "ALL TESTS PASSED" : (failures + " TEST(S) FAILED"));
        return failures == 0 ? 0 : 1;
    }
}
