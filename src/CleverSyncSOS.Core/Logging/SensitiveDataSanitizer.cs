using System.Text.RegularExpressions;

namespace CleverSyncSOS.Core.Logging;

/// <summary>
/// Provides methods for sanitizing sensitive data in logs to prevent credential and PII leakage.
/// FR-010: Structured logging with sanitization to prevent credential leakage.
/// </summary>
public static class SensitiveDataSanitizer
{
    private const string RedactedPlaceholder = "***REDACTED***";
    private const int TokenPreviewLength = 4; // Show only last 4 characters

    /// <summary>
    /// Sanitizes an access token by redacting most of it, showing only the last few characters.
    /// </summary>
    /// <param name="token">The access token to sanitize.</param>
    /// <returns>Sanitized token string (e.g., "***REDACTED***...abc1")</returns>
    public static string SanitizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return RedactedPlaceholder;

        if (token.Length <= TokenPreviewLength)
            return RedactedPlaceholder;

        var preview = token.Substring(token.Length - TokenPreviewLength);
        return $"{RedactedPlaceholder}...{preview}";
    }

    /// <summary>
    /// Sanitizes a connection string by masking passwords, keys, and other sensitive values.
    /// </summary>
    /// <param name="connectionString">The connection string to sanitize.</param>
    /// <returns>Sanitized connection string with masked credentials.</returns>
    public static string SanitizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return RedactedPlaceholder;

        // Regex patterns for common connection string formats
        var sanitized = connectionString;

        // Mask SQL Server passwords
        sanitized = Regex.Replace(sanitized, @"(Password|Pwd)\s*=\s*[^;]+", $"Password={RedactedPlaceholder}", RegexOptions.IgnoreCase);

        // Mask account keys (Azure Storage, etc.)
        sanitized = Regex.Replace(sanitized, @"(AccountKey|SharedAccessKey)\s*=\s*[^;]+", $"AccountKey={RedactedPlaceholder}", RegexOptions.IgnoreCase);

        // Mask user secrets
        sanitized = Regex.Replace(sanitized, @"(User\s*Id|UID)\s*=\s*([^;]+)", match =>
        {
            var userId = match.Groups[2].Value;
            if (userId.Length > 2)
                return $"User Id={userId.Substring(0, 1)}***{userId.Substring(userId.Length - 1)}";
            return $"User Id={RedactedPlaceholder}";
        }, RegexOptions.IgnoreCase);

        return sanitized;
    }

    /// <summary>
    /// Sanitizes an exception by removing sensitive data from its message and inner exceptions.
    /// </summary>
    /// <param name="exception">The exception to sanitize.</param>
    /// <returns>A sanitized error message.</returns>
    public static string SanitizeException(Exception? exception)
    {
        if (exception == null)
            return string.Empty;

        var message = exception.Message;

        // Sanitize potential connection strings in exception messages
        message = SanitizeConnectionString(message);

        // Sanitize potential tokens (Bearer tokens, API keys, etc.)
        message = Regex.Replace(message, @"Bearer\s+[A-Za-z0-9\-._~+/]+=*", $"Bearer {RedactedPlaceholder}", RegexOptions.IgnoreCase);
        message = Regex.Replace(message, @"(token|key|secret|password)\s*[:=]\s*[^\s,;]+", $"$1={RedactedPlaceholder}", RegexOptions.IgnoreCase);

        // Sanitize potential Base64-encoded credentials
        message = Regex.Replace(message, @"Basic\s+[A-Za-z0-9+/]+=*", $"Basic {RedactedPlaceholder}", RegexOptions.IgnoreCase);

        // Include exception type but not the full stack trace (to avoid exposing parameter values)
        var sanitizedMessage = $"{exception.GetType().Name}: {message}";

        // If there's an inner exception, sanitize it too
        if (exception.InnerException != null)
        {
            sanitizedMessage += $" --> {SanitizeException(exception.InnerException)}";
        }

        return sanitizedMessage;
    }

    /// <summary>
    /// Sanitizes a URL by removing query parameters that might contain tokens or sensitive data.
    /// </summary>
    /// <param name="url">The URL to sanitize.</param>
    /// <returns>Sanitized URL with query parameters removed.</returns>
    public static string SanitizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        // Remove query string entirely (they might contain tokens or IDs)
        var questionMarkIndex = url.IndexOf('?');
        if (questionMarkIndex > 0)
        {
            var basePath = url.Substring(0, questionMarkIndex);
            return $"{basePath}?{RedactedPlaceholder}";
        }

        return url;
    }

    /// <summary>
    /// Sanitizes an email address for PII protection by masking most of the local part.
    /// </summary>
    /// <param name="email">The email address to sanitize.</param>
    /// <returns>Sanitized email (e.g., "j***@example.com")</returns>
    public static string SanitizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return RedactedPlaceholder;

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0 || atIndex == email.Length - 1)
            return RedactedPlaceholder;

        var localPart = email.Substring(0, atIndex);
        var domain = email.Substring(atIndex);

        // Show only first character of local part
        if (localPart.Length > 1)
            return $"{localPart[0]}***{domain}";

        return $"***{domain}";
    }

    /// <summary>
    /// Sanitizes a person's name for PII protection by showing only first initial and last initial.
    /// </summary>
    /// <param name="name">The name to sanitize.</param>
    /// <returns>Sanitized name (e.g., "J*** M***")</returns>
    public static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return RedactedPlaceholder;

        var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var sanitizedParts = parts.Select(part =>
        {
            if (part.Length > 1)
                return $"{part[0]}***";
            return "***";
        });

        return string.Join(" ", sanitizedParts);
    }

    /// <summary>
    /// Sanitizes HTTP response content that might contain sensitive data.
    /// </summary>
    /// <param name="responseContent">The HTTP response content to sanitize.</param>
    /// <returns>Sanitized response content.</returns>
    public static string SanitizeHttpResponse(string? responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
            return string.Empty;

        var sanitized = responseContent;

        // Sanitize JSON fields that commonly contain sensitive data
        sanitized = Regex.Replace(sanitized, @"""(access_token|refresh_token|id_token|token|secret|password|key)""\s*:\s*""[^""]+""",
            match => $"\"{match.Groups[1].Value}\":\"{RedactedPlaceholder}\"", RegexOptions.IgnoreCase);

        // Sanitize potential Bearer tokens in plain text
        sanitized = Regex.Replace(sanitized, @"Bearer\s+[A-Za-z0-9\-._~+/]+=*", $"Bearer {RedactedPlaceholder}", RegexOptions.IgnoreCase);

        return sanitized;
    }

    /// <summary>
    /// Creates a sanitized error summary for logging purposes.
    /// </summary>
    /// <param name="exception">The exception to summarize.</param>
    /// <param name="context">Optional context information (will be sanitized).</param>
    /// <returns>A safe error summary for logging.</returns>
    public static string CreateSafeErrorSummary(Exception exception, string? context = null)
    {
        var summary = $"Error Type: {exception.GetType().Name}";

        var sanitizedMessage = SanitizeException(exception);
        summary += $", Message: {sanitizedMessage}";

        if (!string.IsNullOrWhiteSpace(context))
        {
            summary += $", Context: {context}";
        }

        return summary;
    }
}
