namespace API.Logging;

/// <summary>appsettings → <c>Logging:Policy</c> bölümünden bağlanan ayar kümesi.</summary>
public sealed class LoggingPolicyOptions
{
    public const string SectionName = "Logging:Policy";

    public IReadOnlyList<string> RequestPathIncludePrefixes { get; init; } = new[] { "/api" };
    public IReadOnlyList<string> RequestPathExcludeContains { get; init; } = Array.Empty<string>();
    public bool EnableRequestBodyLogging { get; init; }
    public bool EnableResponseBodyLogging { get; init; }
    public IReadOnlyList<string> BodyAllowlistPathPrefixes { get; init; } = Array.Empty<string>();
    public int MaxRequestBodyLogSizeBytes { get; init; } = 16 * 1024;
    public int MaxResponseBodyLogSizeBytes { get; init; } = 16 * 1024;
    public int MaxLoggedBodyPreviewLength { get; init; } = 4096;
    public bool EnablePayloadMasking { get; init; } = true;
    public IReadOnlyList<string> SensitiveJsonKeys { get; init; } = Array.Empty<string>();
    public string MaskValue { get; init; } = "***";
}
