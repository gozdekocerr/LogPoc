namespace API.Logging;

public sealed class LoggingPolicyOptions
{
    public const string SectionName = "Logging:Policy";

    public IReadOnlyList<string> RequestPathIncludePrefixes  { get; init; } = ["/api"];
    public IReadOnlyList<string> RequestPathExcludeContains  { get; init; } = [];
    public bool                  EnableRequestBodyLogging    { get; init; }
    public bool                  EnableResponseBodyLogging   { get; init; }
    public IReadOnlyList<string> BodyAllowlistPathPrefixes   { get; init; } = [];
    public int                   MaxRequestBodyLogSizeBytes  { get; init; } = 16_384;
    public int                   MaxResponseBodyLogSizeBytes { get; init; } = 16_384;
    public int                   MaxLoggedBodyPreviewLength  { get; init; } = 4_096;
}
