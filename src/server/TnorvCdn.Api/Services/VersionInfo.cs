namespace TnorvCdn.Api.Services;

/// <summary>主控版本号（程序集版本）。</summary>
public static class VersionInfo
{
    public static string Current => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
