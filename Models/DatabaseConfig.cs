namespace CiCdAzureSqlDbSchemaManager.Models;

/// <summary>
/// Represents a database configuration with connection details
/// </summary>
public class DatabaseConfig
{
    /// <summary>
    /// Friendly name for the database (e.g., "QA", "Staging", "Production")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Connection string to the Azure SQL Database
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional: Database server name (parsed from connection string if not provided)
    /// </summary>
    public string? Server { get; set; }

    /// <summary>
    /// Optional: Database name (parsed from connection string if not provided)
    /// </summary>
    public string? Database { get; set; }
}
