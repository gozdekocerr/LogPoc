using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Serilog.Context;

namespace API.Middleware;

internal static class AccessLog
{
    public static readonly object HttpContextKey = typeof(AccessLog);

    public const string HttpTemplate =
        "HTTP {MobileId} {UserId} {ClientDevice} {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:F4} ms with Request {Request} with Response {Response}";

    public const string HCorrelation = "X-Correlation-ID";
    public const string HMobile       = "X-Mobile-Id";
    public const string HUser         = "X-User-Id";
    public const string HPlatform     = "X-Platform";
    public const string HDevice       = "X-Client-Device";
    public const string HApiKey       = "X-Api-Key";
    public const string HForwarded    = "X-Forwarded-For";

    public static RequestContext Resolve(HttpContext ctx)
    {
        var activity = Activity.Current ?? ctx.Features.Get<IHttpActivityFeature>()?.Activity;
        var traceId  = activity?.TraceId.ToString();
        var spanId   = activity?.SpanId.ToString();

        var corrHeader = NullIfEmpty(ctx.Request.Headers[HCorrelation].FirstOrDefault());
        var correlationId = corrHeader ?? traceId ?? Guid.NewGuid().ToString("N");

        var path   = ctx.Features.Get<IHttpRequestFeature>()?.RawTarget ?? ctx.Request.Path.Value ?? "/";
        var method = ctx.Request.Method;

        return new RequestContext(
            Path: path,
            Method: method,
            EffectiveCorrelationId: correlationId,
            TraceId: traceId,
            SpanId: spanId,
            MobileId: NullIfEmpty(ctx.Request.Headers[HMobile].FirstOrDefault()),
            UserId: NullIfEmpty(ctx.Request.Headers[HUser].FirstOrDefault()),
            Platform: NullIfEmpty(ctx.Request.Headers[HPlatform].FirstOrDefault()),
            ClientDevice: NullIfEmpty(ctx.Request.Headers[HDevice].FirstOrDefault()),
            ApiKey: NullIfEmpty(ctx.Request.Headers[HApiKey].FirstOrDefault()),
            ClientIp: ClientIp(ctx));
    }

    private static string ClientIp(HttpContext ctx)
    {
        var fwd = ctx.Request.Headers[HForwarded].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fwd))
        {
            var hop = fwd.Split(',', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(hop)) return hop;
        }

        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

internal sealed record RequestContext(
    string Path,
    string Method,
    string EffectiveCorrelationId,
    string? TraceId,
    string? SpanId,
    string? MobileId,
    string? UserId,
    string? Platform,
    string? ClientDevice,
    string? ApiKey,
    string ClientIp);

public sealed class SerilogEnrichmentMiddleware(RequestDelegate next)
{
    public const string CorrelationIdItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var http = AccessLog.Resolve(context);

        context.Items[AccessLog.HttpContextKey] = http;
        context.Items[CorrelationIdItemKey]     = http.EffectiveCorrelationId;
        context.Response.Headers[AccessLog.HCorrelation] = http.EffectiveCorrelationId;

        using (LogContext.PushProperty("CorrelationId", http.EffectiveCorrelationId))
        using (LogContext.PushProperty("TraceId", http.TraceId))
        using (LogContext.PushProperty("SpanId", http.SpanId))
        using (LogContext.PushProperty("MobileId", http.MobileId))
        using (LogContext.PushProperty("UserId", http.UserId))
        using (LogContext.PushProperty("Platform", http.Platform))
        using (LogContext.PushProperty("ClientDevice", http.ClientDevice))
        using (LogContext.PushProperty("ApiKey", http.ApiKey))
        using (LogContext.PushProperty("ClientIp", http.ClientIp))
        using (LogContext.PushProperty("RequestMethod", http.Method))
        using (LogContext.PushProperty("RequestPath", http.Path))
        {
            await next(context);
        }
    }
}

public static class SerilogEnrichmentMiddlewareExtensions
{
    public static IApplicationBuilder UseSerilogEnrichment(this IApplicationBuilder app)
        => app.UseMiddleware<SerilogEnrichmentMiddleware>();
}
