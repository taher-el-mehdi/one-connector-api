using System.Net;
using System.Net.Http;
using DocuWareSageConnector.Domain.Enums;

namespace DocuWareSageConnector.Infrastructure.Synchronization;

public static class ErrorClassifier
{
    public static ErrorClass Classify(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return ErrorClass.Permanent;
        }

        if (IsAuthenticationFailure(exception) || IsMalformed(exception) || IsPermanentSage(exception))
        {
            return ErrorClass.Permanent;
        }

        return IsTransient(exception) ? ErrorClass.Transient : ErrorClass.Permanent;
    }

    public static bool IsTransient(Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            return false;
        }

        if (IsAuthenticationFailure(exception) || IsMalformed(exception) || IsPermanentSage(exception))
        {
            return false;
        }

        if (exception is TimeoutException or HttpRequestException or IOException)
        {
            return true;
        }

        if (TryGetStatusCode(exception, out var status))
        {
            return status == HttpStatusCode.TooManyRequests
                   || status == HttpStatusCode.RequestTimeout
                   || (int)status >= 500;
        }

        var message = exception.Message ?? string.Empty;
        return message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || message.Contains("temporar", StringComparison.OrdinalIgnoreCase)
               || message.Contains(" 429", StringComparison.Ordinal)
               || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetRetryAfter(Exception exception, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (exception is HttpRequestException http
            && http.Data["Retry-After"] is string header
            && double.TryParse(header, out var seconds))
        {
            retryAfter = TimeSpan.FromSeconds(seconds);
            return true;
        }

        var message = exception.Message ?? string.Empty;
        const string marker = "Retry-After:";
        var index = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var slice = message[(index + marker.Length)..].Trim();
            var token = new string(slice.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
            if (double.TryParse(token, out var parsed))
            {
                retryAfter = TimeSpan.FromSeconds(parsed);
                return true;
            }
        }

        return false;
    }

    private static bool IsAuthenticationFailure(Exception exception)
    {
        if (TryGetStatusCode(exception, out var status)
            && status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return true;
        }

        var message = exception.Message ?? string.Empty;
        return message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
               || message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
               || message.Contains("invalid credentials", StringComparison.OrdinalIgnoreCase)
               || message.Contains("authentication failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMalformed(Exception exception)
    {
        return exception is ArgumentException or FormatException or InvalidOperationException
               || (exception.Message?.Contains("malformed", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsPermanentSage(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("80011", StringComparison.Ordinal)
               || message.Contains("gewijzigd", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Index", StringComparison.OrdinalIgnoreCase)
                 && message.Contains("gewijzigd", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSageLock(Exception exception)
    {
        var message = exception.Message ?? string.Empty;
        return message.Contains("80003", StringComparison.Ordinal)
               || message.Contains("gebruikt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetStatusCode(Exception exception, out HttpStatusCode status)
    {
        status = 0;
        var type = exception.GetType();
        var property = type.GetProperty("StatusCode") ?? type.GetProperty("HttpStatusCode");
        if (property is null)
        {
            return false;
        }

        var value = property.GetValue(exception);
        switch (value)
        {
            case HttpStatusCode code:
                status = code;
                return true;
            case int number:
                status = (HttpStatusCode)number;
                return true;
            default:
                return false;
        }
    }
}
