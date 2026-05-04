using System.Diagnostics;
using System.Text;
using API.Logging;
using Microsoft.Extensions.Options;
using Serilog.Context;

namespace API.Middleware;

public sealed class SerilogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SerilogMiddleware> _logger;
    private readonly IOptionsMonitor<LoggingPolicyOptions> _options;

    public SerilogMiddleware(
        RequestDelegate next,
        ILogger<SerilogMiddleware> logger,
        IOptionsMonitor<LoggingPolicyOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var policy = _options.CurrentValue;
        var http = ResolveRequestContext(context);

        if (!ShouldLog(http.Path, policy))
        {
            await _next(context);
            return;
        }

        var requestBody = await ReadRequestBodyAsync(context.Request, http.Path, policy);

        var originalBody = context.Response.Body;
        var bufferResponse = ShouldCaptureBody(policy.EnableResponseBodyLogging, http.Path, policy);

        var sw = Stopwatch.StartNew();
        Exception? caught = null;
        string responseBody = string.Empty;
        var responseTruncated = false;

        if (!bufferResponse)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                caught = ex;
                throw;
            }
            finally
            {
                sw.Stop();
                LogCompleted(context, http, policy, requestBody, responseBody, responseTruncated, sw, caught);
            }

            return;
        }

        await using var mem = new MemoryStream();
        context.Response.Body = mem;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            caught = ex;
            throw;
        }
        finally
        {
            sw.Stop();
            context.Response.Body = originalBody;

            mem.Position = 0;
            var len = (int)mem.Length;
            var cap = Math.Max(0, policy.MaxResponseBodyLogSizeBytes);
            var take = Math.Min(len, cap);
            if (take > 0)
                responseBody = Encoding.UTF8.GetString(mem.GetBuffer().AsSpan(0, take));

            responseTruncated = cap > 0 && len > cap;

            mem.Position = 0;
            if (mem.Length > 0)
                await mem.CopyToAsync(originalBody, context.RequestAborted);

            LogCompleted(context, http, policy, requestBody, responseBody, responseTruncated, sw, caught);
        }
    }
    
    private void LogCompleted(
        HttpContext context,
        RequestContext http,
        LoggingPolicyOptions policy,
        string requestBody,
        string responseBody,
        bool responseTruncated,
        Stopwatch sw,
        Exception? caught)
    {
        var statusCode = context.Response.StatusCode;
        var level      = ResolveAccessLogLevel(statusCode, caught);
        var elapsedMs  = sw.Elapsed.TotalMilliseconds;

        var requestTextForLog  = TruncateForLog(requestBody, policy);
        var responseTextForLog = TruncateForLog(responseBody, policy);

        using (LogContext.PushProperty("ResponseTruncated", responseTruncated))
        {
            _logger.Log(
                level,
                caught,
                AccessLog.HttpTemplate,
                http.MobileId,
                http.UserId,
                http.ClientDevice,
                http.Method,
                http.Path,
                statusCode,
                elapsedMs,
                requestTextForLog,
                responseTextForLog);
        }
    }

    /// <summary>
    /// İstisna varsa her zaman Error. Yoksa 5xx → Error, 4xx → Warning, diğerleri → Information.
    /// </summary>
    private static LogLevel ResolveAccessLogLevel(int statusCode, Exception? pipelineException) =>
        pipelineException is not null || statusCode >= 500 ? LogLevel.Error
        : statusCode >= 400 ? LogLevel.Warning
        : LogLevel.Information;

    private static bool ShouldLog(string path, LoggingPolicyOptions p)
        => MatchesAnyPrefix(path, p.RequestPathIncludePrefixes)
           && !ContainsAny(path, p.RequestPathExcludeContains);

    private static bool ShouldCaptureBody(bool enabled, string path, LoggingPolicyOptions p)
        => enabled
           && (p.BodyAllowlistPathPrefixes.Count == 0
               || MatchesAnyPrefix(path, p.BodyAllowlistPathPrefixes));

    private static async Task<string> ReadRequestBodyAsync(
        HttpRequest request, string path, LoggingPolicyOptions p)
    {
        if (!ShouldCaptureBody(p.EnableRequestBodyLogging, path, p)
            || request.ContentLength == 0)
        {
            return string.Empty;
        }

        request.EnableBuffering();
        request.Body.Position = 0;

        var buffer = new byte[p.MaxRequestBodyLogSizeBytes];
        var read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length));

        request.Body.Position = 0;

        return read == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string TruncateForLog(string payload, LoggingPolicyOptions p)
    {
        if (string.IsNullOrEmpty(payload))
            return payload;

        return p.MaxLoggedBodyPreviewLength > 0 && payload.Length > p.MaxLoggedBodyPreviewLength
            ? payload[..p.MaxLoggedBodyPreviewLength]
            : payload;
    }

    private static bool MatchesAnyPrefix(string path, IReadOnlyList<string> prefixes)
    {
        for (var i = 0; i < prefixes.Count; i++)
        {
            if (path.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsAny(string path, IReadOnlyList<string> needles)
    {
        for (var i = 0; i < needles.Count; i++)
        {
            if (path.Contains(needles[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static RequestContext ResolveRequestContext(HttpContext context) =>
        context.Items.TryGetValue(AccessLog.HttpContextKey, out var existing) && existing is RequestContext h
            ? h
            : AccessLog.Resolve(context);
}

public static class SerilogMiddlewareExtensions
{
    public static IApplicationBuilder UseSerilogMiddleware(this IApplicationBuilder app)
        => app.UseMiddleware<SerilogMiddleware>();
}
