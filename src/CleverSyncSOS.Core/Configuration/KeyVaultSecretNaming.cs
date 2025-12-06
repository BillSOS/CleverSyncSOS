namespace CleverSyncSOS.Core.Configuration;

/// <summary>
/// Centralized Key Vault secret naming convention for CleverSyncSOS.
/// Defines three tiers of secrets: Global (system-wide), District-scoped, and School-scoped.
/// </summary>
public static class KeyVaultSecretNaming
{
    /// <summary>
    /// Global secret names (system-wide, not scoped to district/school).
    /// Format: {FunctionalName}
    /// </summary>
    public static class Global
    {
        /// <summary>
        /// Clever API OAuth Client ID (system-wide)
        /// </summary>
        public const string ClientId = "ClientId";

        /// <summary>
        /// Clever API OAuth Client Secret (system-wide)
        /// </summary>
        public const string ClientSecret = "ClientSecret";

        /// <summary>
        /// Admin Portal super admin bypass login password
        /// </summary>
        public const string SuperAdminPassword = "SuperAdminPassword";

        /// <summary>
        /// SessionDb database password (used with connection string template)
        /// </summary>
        public const string SessionDbPassword = "SessionDbPassword";

        /// <summary>
        /// SessionDb full connection string with embedded password (alternative approach)
        /// </summary>
        public const string SessionDbConnectionString = "SessionDbConnectionString";

        /// <summary>
        /// Pre-generated Clever access token (optional, for districts with bearer tokens)
        /// </summary>
        public const string AccessToken = "AccessToken";
    }

    /// <summary>
    /// District secret functional names (combined with KeyVaultDistrictPrefix).
    /// Format: {KeyVaultDistrictPrefix}--{FunctionalName}
    /// </summary>
    public static class District
    {
        /// <summary>
        /// District-specific API token
        /// </summary>
        public const string ApiToken = "ApiToken";

        /// <summary>
        /// District administrator contact email
        /// </summary>
        public const string ContactEmail = "ContactEmail";

        /// <summary>
        /// District-specific connection string (if districts have shared databases)
        /// </summary>
        public const string ConnectionString = "ConnectionString";
    }

    /// <summary>
    /// School secret functional names (combined with KeyVaultSchoolPrefix).
    /// Format: {KeyVaultSchoolPrefix}--{FunctionalName}
    /// </summary>
    public static class School
    {
        /// <summary>
        /// School database connection string (with embedded password)
        /// </summary>
        public const string ConnectionString = "ConnectionString";

        /// <summary>
        /// School-specific API key
        /// </summary>
        public const string ApiKey = "ApiKey";

        /// <summary>
        /// School database password (alternative to embedded password in connection string)
        /// </summary>
        public const string DatabasePassword = "DatabasePassword";
    }

    /// <summary>
    /// Builds a district-scoped secret name.
    /// Format: {keyVaultDistrictPrefix}--{functionalName}
    /// </summary>
    /// <param name="keyVaultDistrictPrefix">The district prefix from Districts.KeyVaultDistrictPrefix column</param>
    /// <param name="functionalName">The functional name (e.g., from District class constants)</param>
    /// <returns>Formatted secret name</returns>
    /// <exception cref="ArgumentException">Thrown if parameters are null or empty</exception>
    public static string BuildDistrictSecretName(string keyVaultDistrictPrefix, string functionalName)
    {
        if (string.IsNullOrWhiteSpace(keyVaultDistrictPrefix))
            throw new ArgumentException("KeyVaultDistrictPrefix cannot be null or empty", nameof(keyVaultDistrictPrefix));
        if (string.IsNullOrWhiteSpace(functionalName))
            throw new ArgumentException("FunctionalName cannot be null or empty", nameof(functionalName));

        return $"{keyVaultDistrictPrefix}--{functionalName}";
    }

    /// <summary>
    /// Builds a school-scoped secret name.
    /// Format: {keyVaultSchoolPrefix}--{functionalName}
    /// </summary>
    /// <param name="keyVaultSchoolPrefix">The school prefix from Schools.KeyVaultSchoolPrefix column</param>
    /// <param name="functionalName">The functional name (e.g., from School class constants)</param>
    /// <returns>Formatted secret name</returns>
    /// <exception cref="ArgumentException">Thrown if parameters are null or empty</exception>
    public static string BuildSchoolSecretName(string keyVaultSchoolPrefix, string functionalName)
    {
        if (string.IsNullOrWhiteSpace(keyVaultSchoolPrefix))
            throw new ArgumentException("KeyVaultSchoolPrefix cannot be null or empty", nameof(keyVaultSchoolPrefix));
        if (string.IsNullOrWhiteSpace(functionalName))
            throw new ArgumentException("FunctionalName cannot be null or empty", nameof(functionalName));

        return $"{keyVaultSchoolPrefix}--{functionalName}";
    }
}
