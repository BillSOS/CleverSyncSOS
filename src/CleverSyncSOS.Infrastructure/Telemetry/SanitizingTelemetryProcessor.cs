using CleverSyncSOS.Core.Logging;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace CleverSyncSOS.Infrastructure.Telemetry;

/// <summary>
/// Application Insights telemetry processor that sanitizes sensitive data before sending to Azure.
/// Prevents credential and PII leakage in telemetry data.
/// FR-010: Structured logging with sanitization to prevent credential leakage.
/// </summary>
public class SanitizingTelemetryProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;
    private static readonly string[] SensitiveHeaders = new[]
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-API-Key",
        "X-Auth-Token"
    };

    private static readonly string[] SensitivePropertyNames = new[]
    {
        "password",
        "secret",
        "token",
        "key",
        "connectionstring",
        "accesstoken",
        "refreshtoken",
        "clientsecret"
    };

    public SanitizingTelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public void Process(ITelemetry item)
    {
        // Sanitize different telemetry types
        switch (item)
        {
            case RequestTelemetry requestTelemetry:
                SanitizeRequestTelemetry(requestTelemetry);
                break;

            case DependencyTelemetry dependencyTelemetry:
                SanitizeDependencyTelemetry(dependencyTelemetry);
                break;

            case ExceptionTelemetry exceptionTelemetry:
                SanitizeExceptionTelemetry(exceptionTelemetry);
                break;

            case TraceTelemetry traceTelemetry:
                SanitizeTraceTelemetry(traceTelemetry);
                break;
        }

        // Sanitize custom properties for all telemetry types
        if (item is ISupportProperties telemetryWithProperties)
        {
            SanitizeCustomProperties(telemetryWithProperties.Properties);
        }

        // Pass to next processor in the chain
        _next.Process(item);
    }

    private void SanitizeRequestTelemetry(RequestTelemetry requestTelemetry)
    {
        // Sanitize URL (remove query parameters that might contain tokens)
        if (!string.IsNullOrEmpty(requestTelemetry.Url?.ToString()))
        {
            var sanitizedUrl = SensitiveDataSanitizer.SanitizeUrl(requestTelemetry.Url.ToString());
            if (Uri.TryCreate(sanitizedUrl, UriKind.Absolute, out var uri))
            {
                requestTelemetry.Url = uri;
            }
        }

        // Sanitize request headers (remove Authorization, Cookie, etc.)
        if (requestTelemetry.Properties != null)
        {
            foreach (var headerName in SensitiveHeaders)
            {
                // Check both exact match and case-insensitive variants
                var keysToRemove = requestTelemetry.Properties.Keys
                    .Where(k => k.Equals(headerName, StringComparison.OrdinalIgnoreCase) ||
                                k.Equals($"Request-{headerName}", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    requestTelemetry.Properties[key] = "***REDACTED***";
                }
            }
        }
    }

    private void SanitizeDependencyTelemetry(DependencyTelemetry dependencyTelemetry)
    {
        // Sanitize dependency data (e.g., SQL commands, HTTP calls)
        if (!string.IsNullOrEmpty(dependencyTelemetry.Data))
        {
            // Sanitize SQL connection strings
            if (dependencyTelemetry.Type == "SQL" || dependencyTelemetry.Type == "Azure SQL")
            {
                dependencyTelemetry.Data = SensitiveDataSanitizer.SanitizeConnectionString(dependencyTelemetry.Data);
            }

            // Sanitize HTTP URLs
            if (dependencyTelemetry.Type == "HTTP" || dependencyTelemetry.Type == "Http")
            {
                dependencyTelemetry.Data = SensitiveDataSanitizer.SanitizeUrl(dependencyTelemetry.Data);
            }
        }

        // Sanitize target (might contain server names with credentials)
        if (!string.IsNullOrEmpty(dependencyTelemetry.Target))
        {
            // Remove any @ credentials from server names (e.g., user@server -> ***@server)
            if (dependencyTelemetry.Target.Contains('@'))
            {
                var parts = dependencyTelemetry.Target.Split('@');
                if (parts.Length == 2)
                {
                    dependencyTelemetry.Target = $"***@{parts[1]}";
                }
            }
        }
    }

    private void SanitizeExceptionTelemetry(ExceptionTelemetry exceptionTelemetry)
    {
        // Sanitize exception message
        if (exceptionTelemetry.Exception != null)
        {
            var sanitizedMessage = SensitiveDataSanitizer.SanitizeException(exceptionTelemetry.Exception);
            exceptionTelemetry.Properties["SanitizedMessage"] = sanitizedMessage;
        }

        // Remove potentially sensitive exception details
        if (!string.IsNullOrEmpty(exceptionTelemetry.Message))
        {
            exceptionTelemetry.Message = SensitiveDataSanitizer.SanitizeException(
                new Exception(exceptionTelemetry.Message));
        }

        // Sanitize stack trace data in properties
        if (exceptionTelemetry.Properties != null)
        {
            foreach (var key in exceptionTelemetry.Properties.Keys.ToList())
            {
                var value = exceptionTelemetry.Properties[key];
                if (!string.IsNullOrEmpty(value))
                {
                    // Look for connection strings or tokens in property values
                    if (value.Contains("Password=") || value.Contains("AccountKey=") || value.Contains("Bearer "))
                    {
                        exceptionTelemetry.Properties[key] = SensitiveDataSanitizer.SanitizeConnectionString(value);
                    }
                }
            }
        }
    }

    private void SanitizeTraceTelemetry(TraceTelemetry traceTelemetry)
    {
        // Sanitize trace message
        if (!string.IsNullOrEmpty(traceTelemetry.Message))
        {
            // Check if message contains potential sensitive data
            var message = traceTelemetry.Message;

            if (ContainsSensitiveKeywords(message))
            {
                message = SensitiveDataSanitizer.SanitizeConnectionString(message);
                message = SensitiveDataSanitizer.SanitizeHttpResponse(message);
            }

            traceTelemetry.Message = message;
        }
    }

    private void SanitizeCustomProperties(IDictionary<string, string> properties)
    {
        if (properties == null || properties.Count == 0)
            return;

        // List of keys to sanitize
        var keysToSanitize = new List<string>();

        foreach (var key in properties.Keys)
        {
            // Check if property name suggests sensitive data
            if (SensitivePropertyNames.Any(sensitive =>
                key.Contains(sensitive, StringComparison.OrdinalIgnoreCase)))
            {
                keysToSanitize.Add(key);
            }
        }

        // Sanitize the identified properties
        foreach (var key in keysToSanitize)
        {
            var value = properties[key];
            if (!string.IsNullOrEmpty(value))
            {
                // Apply appropriate sanitization based on content
                if (value.Contains("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    properties[key] = SensitiveDataSanitizer.SanitizeToken(value);
                }
                else if (value.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
                         value.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase))
                {
                    properties[key] = SensitiveDataSanitizer.SanitizeConnectionString(value);
                }
                else if (value.Contains('@') && value.Contains('.'))
                {
                    properties[key] = SensitiveDataSanitizer.SanitizeEmail(value);
                }
                else
                {
                    properties[key] = "***REDACTED***";
                }
            }
        }
    }

    private bool ContainsSensitiveKeywords(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        var lowerText = text.ToLowerInvariant();
        return lowerText.Contains("password") ||
               lowerText.Contains("secret") ||
               lowerText.Contains("token") ||
               lowerText.Contains("bearer ") ||
               lowerText.Contains("accountkey") ||
               lowerText.Contains("connectionstring");
    }
}
