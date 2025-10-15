namespace CiCdAzureSqlDbSchemaManager.Services;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using CiCdAzureSqlDbSchemaManager.Models;

/// <summary>
/// Service for managing database connections and validations
/// </summary>
public class DatabaseConnectionService
{
    private readonly ILogger<DatabaseConnectionService> _logger;

    public DatabaseConnectionService(ILogger<DatabaseConnectionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates that a database connection can be established
    /// </summary>
    public async Task<bool> ValidateConnectionAsync(DatabaseConfig config, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Validating connection to {DatabaseName}...", config.Name);

            using var connection = new SqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var serverVersion = connection.ServerVersion;
            var database = connection.Database;

            _logger.LogInformation(
                "Successfully connected to {DatabaseName} (Server: {Database}, Version: {Version})",
                config.Name,
                database,
                serverVersion
            );

            return true;
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Failed to connect to {DatabaseName}: {ErrorMessage}",
                config.Name,
                ex.Message
            );
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error connecting to {DatabaseName}: {ErrorMessage}",
                config.Name,
                ex.Message
            );
            return false;
        }
    }

    /// <summary>
    /// Validates connections to multiple databases in parallel
    /// </summary>
    public async Task<Dictionary<string, bool>> ValidateConnectionsAsync(
        IEnumerable<DatabaseConfig> configs,
        CancellationToken cancellationToken = default)
    {
        var tasks = configs.Select(async config => new
        {
            Name = config.Name,
            IsValid = await ValidateConnectionAsync(config, cancellationToken)
        });

        var results = await Task.WhenAll(tasks);

        return results.ToDictionary(r => r.Name, r => r.IsValid);
    }

    /// <summary>
    /// Gets database metadata (server, database name, version)
    /// </summary>
    public async Task<DatabaseMetadata?> GetDatabaseMetadataAsync(
        DatabaseConfig config,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(config.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT
                    @@SERVERNAME AS ServerName,
                    DB_NAME() AS DatabaseName,
                    @@VERSION AS Version,
                    (SELECT COUNT(*) FROM sys.tables WHERE type = 'U') AS TableCount,
                    (SELECT COUNT(*) FROM sys.views) AS ViewCount,
                    (SELECT COUNT(*) FROM sys.procedures) AS StoredProcCount
            ";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new DatabaseMetadata
                {
                    ConfigName = config.Name,
                    ServerName = reader.GetString(0),
                    DatabaseName = reader.GetString(1),
                    Version = reader.GetString(2),
                    TableCount = reader.GetInt32(3),
                    ViewCount = reader.GetInt32(4),
                    StoredProcCount = reader.GetInt32(5)
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to get metadata for {DatabaseName}: {ErrorMessage}",
                config.Name,
                ex.Message
            );
            return null;
        }
    }

    /// <summary>
    /// Tests if a table exists in the database
    /// </summary>
    public async Task<bool> TableExistsAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var parts = tableName.Split('.');
            var schema = parts.Length > 1 ? parts[0] : "dbo";
            var table = parts.Length > 1 ? parts[1] : parts[0];

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = @Schema AND TABLE_NAME = @Table
            ";
            command.Parameters.AddWithValue("@Schema", schema);
            command.Parameters.AddWithValue("@Table", table);

            var count = (int?)await command.ExecuteScalarAsync(cancellationToken);
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to check if table {TableName} exists: {ErrorMessage}",
                tableName,
                ex.Message
            );
            return false;
        }
    }
}

/// <summary>
/// Database metadata information
/// </summary>
public class DatabaseMetadata
{
    public string ConfigName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int TableCount { get; set; }
    public int ViewCount { get; set; }
    public int StoredProcCount { get; set; }
}
