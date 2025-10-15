namespace CiCdAzureSqlDbSchemaManager.Services;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text;

/// <summary>
/// Service for synchronizing data from config tables
/// </summary>
public class DataSyncService
{
    private readonly ILogger<DataSyncService> _logger;
    private readonly DatabaseConnectionService _connectionService;

    public DataSyncService(
        ILogger<DataSyncService> logger,
        DatabaseConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
    }

    /// <summary>
    /// Synchronizes config table data from source to target
    /// </summary>
    public async Task<DataSyncResult> SyncConfigTableAsync(
        string sourceConnectionString,
        string targetConnectionString,
        string tableName,
        bool useTransaction = true,
        CancellationToken cancellationToken = default)
    {
        var result = new DataSyncResult
        {
            TableName = tableName,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // Validate table exists in both databases
            var sourceExists = await _connectionService.TableExistsAsync(
                sourceConnectionString,
                tableName,
                cancellationToken
            );

            if (!sourceExists)
            {
                result.Success = false;
                result.ErrorMessage = $"Table {tableName} does not exist in source database";
                _logger.LogWarning(result.ErrorMessage);
                return result;
            }

            var targetExists = await _connectionService.TableExistsAsync(
                targetConnectionString,
                tableName,
                cancellationToken
            );

            if (!targetExists)
            {
                result.Success = false;
                result.ErrorMessage = $"Table {tableName} does not exist in target database";
                _logger.LogWarning(result.ErrorMessage);
                return result;
            }

            _logger.LogInformation("Starting data sync for table {TableName}...", tableName);

            // Get data from source
            var sourceData = await GetTableDataAsync(sourceConnectionString, tableName, cancellationToken);
            result.RowsRead = sourceData.Rows.Count;

            _logger.LogInformation("Read {RowCount} rows from source table {TableName}", result.RowsRead, tableName);

            // Sync to target
            var rowsSynced = await SyncDataToTargetAsync(
                targetConnectionString,
                tableName,
                sourceData,
                useTransaction,
                cancellationToken
            );

            result.RowsSynced = rowsSynced;
            result.Success = true;

            _logger.LogInformation(
                "Successfully synced {RowCount} rows to target table {TableName}",
                result.RowsSynced,
                tableName
            );
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Error syncing table {TableName}: {ErrorMessage}", tableName, ex.Message);
        }
        finally
        {
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime.Value - result.StartTime;
        }

        return result;
    }

    /// <summary>
    /// Synchronizes multiple config tables from source to target
    /// </summary>
    public async Task<List<DataSyncResult>> SyncConfigTablesAsync(
        string sourceConnectionString,
        string targetConnectionString,
        List<string> tableNames,
        bool useTransaction = true,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DataSyncResult>();

        foreach (var tableName in tableNames)
        {
            var result = await SyncConfigTableAsync(
                sourceConnectionString,
                targetConnectionString,
                tableName,
                useTransaction,
                cancellationToken
            );

            results.Add(result);

            if (!result.Success)
            {
                _logger.LogWarning("Skipping remaining tables due to sync failure for {TableName}", tableName);
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// Reads all data from a table
    /// </summary>
    private async Task<DataTable> GetTableDataAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken)
    {
        var dataTable = new DataTable();

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var query = $"SELECT * FROM {tableName}";

        using var command = new SqlCommand(query, connection);
        using var adapter = new SqlDataAdapter(command);

        await Task.Run(() => adapter.Fill(dataTable), cancellationToken);

        return dataTable;
    }

    /// <summary>
    /// Syncs data to target database using TRUNCATE and INSERT
    /// </summary>
    private async Task<int> SyncDataToTargetAsync(
        string connectionString,
        string tableName,
        DataTable sourceData,
        bool useTransaction,
        CancellationToken cancellationToken)
    {
        if (sourceData.Rows.Count == 0)
        {
            _logger.LogInformation("No data to sync for table {TableName}", tableName);
            return 0;
        }

        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        SqlTransaction? transaction = null;

        try
        {
            if (useTransaction)
            {
                transaction = connection.BeginTransaction();
            }

            // Truncate target table
            var truncateCommand = connection.CreateCommand();
            truncateCommand.CommandText = $"TRUNCATE TABLE {tableName}";
            if (transaction != null)
            {
                truncateCommand.Transaction = transaction;
            }

            await truncateCommand.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug("Truncated target table {TableName}", tableName);

            // Bulk insert data
            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction);
            bulkCopy.DestinationTableName = tableName;
            bulkCopy.BatchSize = 1000;
            bulkCopy.BulkCopyTimeout = 300; // 5 minutes

            // Map columns
            foreach (DataColumn column in sourceData.Columns)
            {
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }

            await bulkCopy.WriteToServerAsync(sourceData, cancellationToken);

            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            _logger.LogDebug("Bulk inserted {RowCount} rows into {TableName}", sourceData.Rows.Count, tableName);

            return sourceData.Rows.Count;
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    /// <summary>
    /// Gets the row count for a table
    /// </summary>
    public async Task<int> GetTableRowCountAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {tableName}";

            var count = (int?)await command.ExecuteScalarAsync(cancellationToken);
            return count ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting row count for {TableName}: {ErrorMessage}", tableName, ex.Message);
            return 0;
        }
    }
}

/// <summary>
/// Result of a data synchronization operation
/// </summary>
public class DataSyncResult
{
    /// <summary>
    /// Table name that was synchronized
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// True if sync was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if sync failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Number of rows read from source
    /// </summary>
    public int RowsRead { get; set; }

    /// <summary>
    /// Number of rows synced to target
    /// </summary>
    public int RowsSynced { get; set; }

    /// <summary>
    /// Start time of sync operation
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// End time of sync operation
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Duration of sync operation
    /// </summary>
    public TimeSpan Duration { get; set; }
}
