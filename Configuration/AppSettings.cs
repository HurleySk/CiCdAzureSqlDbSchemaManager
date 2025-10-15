namespace CiCdAzureSqlDbSchemaManager.Configuration;

using CiCdAzureSqlDbSchemaManager.Models;

/// <summary>
/// Application settings loaded from appsettings.json
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Source (dev) database configuration
    /// </summary>
    public DatabaseConfig SourceDatabase { get; set; } = new();

    /// <summary>
    /// List of target databases to deploy to
    /// </summary>
    public List<DatabaseConfig> TargetDatabases { get; set; } = new();

    /// <summary>
    /// List of config table names (schema.tablename) that should have data synchronized
    /// </summary>
    public List<string> ConfigTables { get; set; } = new();

    /// <summary>
    /// Deployment options
    /// </summary>
    public DeploymentOptions Options { get; set; } = new();

    /// <summary>
    /// Optional: Tables to exclude from schema comparison
    /// </summary>
    public List<string> ExcludedTables { get; set; } = new();

    /// <summary>
    /// Optional: Schemas to include in deployment (if empty, all schemas included)
    /// </summary>
    public List<string> IncludedSchemas { get; set; } = new();
}
