using System.Linq;
using System.Reflection;

namespace MapExplorer.App;

public static class AppVersionInfo
{
    /// <summary>The release tag's version (e.g. "1.2.3"), injected at build time via -p:AppVersion.
    /// Falls back to "dev" for ordinary local/CI builds that don't set it.</summary>
    public static string Version { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "AppVersion")?.Value
        is { Length: > 0 } value ? value : "dev";
}
