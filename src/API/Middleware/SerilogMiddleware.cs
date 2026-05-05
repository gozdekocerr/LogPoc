using System.Diagnostics;
using System.Text;
using API.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace API.Middleware;

public sealed class SerilogMiddleware
{
    private readonly RequestDelegate                        _next;
    private readonly ILogger<SerilogMiddleware>             _logger;
    private readonly IOptionsMonitor<LoggingPolicyOptions>  _options;

    public SerilogMiddleware(
        RequestDelegate next,
        ILogger<SerilogMiddleware> logger,
        IOptionsMonitor<LoggingPolicyOptions> options)
    {
        _next    = next;
        _logger  = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var policy = _options.CurrentValue;
        var path   = GetPath(context);

        if (!ShouldLog(path, policy))
        {
            await _next(context);
            return;
        }

        var requestBody = await ReadRequestBodyAsync(context.Request, path, policy);

        var originalBody    = context.Response.Body;
        var bufferResponse  = ShouldCaptureBody(policy.EnableResponseBodyLogging, path, policy);

        var sw               = Stopwatch.StartNew();
        Exception? caught    = null;
        string responseBody  = string.Empty;
        bool truncated       = false;

        if (!bufferResponse)
        {
            try   { await _next(context); }
            catch (Exception ex) { caught = ex; throw; }
            finally
            {
                sw.Stop();
                WriteLog(context, policy, requestBody, responseBody, truncated, sw, caught);
            }
            return;
        }

        await using var mem = new MemoryStream();
        context.Response.Body = mem;
        try   { await _next(context); }
        catch (Exception ex) { caught = ex; throw; }
        finally
        {
            sw.Stop();
            context.Response.Body = originalBody;

            mem.Position = 0;
            var len  = (int)mem.Length;
            var cap  = Math.Max(0, policy.MaxResponseBodyLogSizeBytes);
            var take = Math.Min(len, cap);

            if (take > 0)
                responseBody = Encoding.UTF8.GetString(mem.GetBuffer().AsSpan(0, take));

            truncated = cap > 0 && len > cap;

            mem.Position = 0;
            if (mem.Length > 0)
                await mem.CopyToAsync(originalBody, context.RequestAborted);

            WriteLog(context, policy, requestBody, responseBody, truncated, sw, caught);
        }
    }
    
    private void WriteLog(
        HttpContext context,
        LoggingPolicyOptions policy,
        string requestBody,
        string responseBody,
        bool truncated,
        Stopwatch sw,
        Exception? caught)
    {
        var statusCode  = context.Response.StatusCode;
        var level       = ResolveLevel(statusCode, caught);
        var elapsed     = sw.Elapsed.TotalMilliseconds;
        var reqSnippet  = Truncate(requestBody,  policy);
        var respSnippet = Truncate(responseBody, policy);

      
        using var reqProp  = string.IsNullOrEmpty(reqSnippet)  ? null : LogContext.PushProperty("RequestBody",       reqSnippet);
        using var resProp  = string.IsNullOrEmpty(respSnippet) ? null : LogContext.PushProperty("ResponseBody",      respSnippet);
        using var truncProp = truncated                         ? LogContext.PushProperty("ResponseTruncated", true) : null;

        _logger.Log(level, caught, "{StatusCode} in {Elapsed:0.000} ms", statusCode, elapsed);
    }
    
    private static LogLevel ResolveLevel(int statusCode, Exception? ex) =>
        ex is not null || statusCode >= 500 ? LogLevel.Error
        : statusCode >= 400                 ? LogLevel.Warning
        :                                     LogLevel.Information;

    private static string GetPath(HttpContext context) =>
        context.Items.TryGetValue(RequestContextResolver.HttpContextKey, out var stored)
        && stored is RequestContext rc
            ? rc.Path
            : context.Request.Path.Value ?? "/";

    private static bool ShouldLog(string path, LoggingPolicyOptions p) =>
        MatchesAnyPrefix(path, p.RequestPathIncludePrefixes)
        && !ContainsAny(path, p.RequestPathExcludeContains);

    private static bool ShouldCaptureBody(bool enabled, string path, LoggingPolicyOptions p) =>
        enabled && (p.BodyAllowlistPathPrefixes.Count == 0
                    || MatchesAnyPrefix(path, p.BodyAllowlistPathPrefixes));

    private static async Task<string> ReadRequestBodyAsync(
        HttpRequest request, string path, LoggingPolicyOptions p)
    {
        if (!ShouldCaptureBody(p.EnableRequestBodyLogging, path, p) || request.ContentLength == 0)
            return string.Empty;

        request.EnableBuffering();
        request.Body.Position = 0;

        var buffer = new byte[p.MaxRequestBodyLogSizeBytes];
        var read   = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length));
        request.Body.Position = 0;

        return read == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string Truncate(string text, LoggingPolicyOptions p) =>
        !string.IsNullOrEmpty(text)
        && p.MaxLoggedBodyPreviewLength > 0
        && text.Length > p.MaxLoggedBodyPreviewLength
            ? text[..p.MaxLoggedBodyPreviewLength]
            : text;

    private static bool MatchesAnyPrefix(string path, IReadOnlyList<string> prefixes)
    {
        foreach (var prefix in prefixes)
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool ContainsAny(string path, IReadOnlyList<string> needles)
    {
        foreach (var needle in needles)
            if (path.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}

public static class SerilogMiddlewareExtensions
{
    public static IApplicationBuilder UseSerilogMiddleware(this IApplicationBuilder app)
        => app.UseMiddleware<SerilogMiddleware>();
}
