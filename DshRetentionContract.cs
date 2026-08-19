// DSH Desktop Uninstaller - retention extension contract.
//
// These types are intentionally small: they define the extension surface for
// future retention categories without forcing the current static implementation
// to be rewritten in one pass. New categories (e.g. "keep WSL distro data",
// "keep updater cache") can implement IRetentionCategory and be wired into the
// confirmation/Run pipeline later.

/// <summary>Common contract for an optional item that may be kept during uninstall.</summary>
public interface IRetentionCategory
{
    string DisplayName { get; }

    bool IsSelected { get; set; }

    string DescribeRetention();
}

/// <summary>
/// Simple abstract base for retention categories. New categories should derive
/// from this class and only implement DescribeRetention().
/// </summary>
public abstract class RetentionCategory : IRetentionCategory
{
    protected RetentionCategory(string displayName)
    {
        DisplayName = displayName;
    }

    public string DisplayName { get; protected set; }

    public bool IsSelected { get; set; }

    public abstract string DescribeRetention();
}