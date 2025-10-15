namespace CiCdAzureSqlDbSchemaManager.Services;

using Microsoft.Extensions.Logging;
using CiCdAzureSqlDbSchemaManager.Models;
using CiCdAzureSqlDbSchemaManager.Configuration;
using System.Diagnostics;

/// <summary>
/// Service for orchestrating parallel deployments to multiple target databases
/// </summary>
public class ParallelDeploymentService
{
    private readonly ILogger<ParallelDeploymentService> _logger;
    private readonly DatabaseConnectionService _connectionService;
    private readonly SchemaDeploymentService _schemaService;
    private readonly DataSyncService _dataSyncService;

    public ParallelDeploymentService(
        ILogger<ParallelDeploymentService> logger,
        DatabaseConnectionService connectionService,
        SchemaDeploymentService schemaService,
        DataSyncService dataSyncService)
    {
        _logger = logger;
        _connectionService = connectionService;
        _schemaService = schemaService;
        _dataSyncService = dataSyncService;
    }

    /// <summary>
    /// Deploys schema and data to multiple target databases in parallel
    /// </summary>
    public async Task<DeploymentSummary> DeployToTargetsAsync(
        AppSettings settings,
        bool schemaOnly = false,
        bool dataOnly = false,
        List<string>? targetFilter = null,
        CancellationToken cancellationToken = default)
    {
        var overallStopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting deployment to {Count} target(s)...", settings.TargetDatabases.Count);

        // Validate source connection
        _logger.LogInformation("Validating source database connection...");
        var sourceValid = await _connectionService.ValidateConnectionAsync(
            settings.SourceDatabase,
            cancellationToken
        );

        if (!sourceValid)
        {
            _logger.LogError("Source database connection validation failed. Aborting deployment.");
            return new DeploymentSummary
            {
                TotalDuration = overallStopwatch.Elapsed,
                Results = new List<DeploymentResult>
                {
                    new DeploymentResult
                    {
                        TargetName = "Source Validation",
                        Success = false,
                        ErrorMessage = "Failed to connect to source database"
                    }
                }
            };
        }

        // Filter targets if specified
        var targets = targetFilter != null && targetFilter.Any()
            ? settings.TargetDatabases.Where(t => targetFilter.Contains(t.Name)).ToList()
            : settings.TargetDatabases;

        if (!targets.Any())
        {
            _logger.LogWarning("No target databases found matching filter");
            return new DeploymentSummary { TotalDuration = overallStopwatch.Elapsed };
        }

        // Validate target connections
        _logger.LogInformation("Validating target database connections...");
        var targetValidations = await _connectionService.ValidateConnectionsAsync(targets, cancellationToken);

        var invalidTargets = targetValidations.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
        if (invalidTargets.Any())
        {
            _logger.LogWarning(
                "The following targets failed connection validation: {Targets}",
                string.Join(", ", invalidTargets)
            );

            if (!settings.Options.ContinueOnError)
            {
                return new DeploymentSummary
                {
                    TotalDuration = overallStopwatch.Elapsed,
                    Results = invalidTargets.Select(t => new DeploymentResult
                    {
                        TargetName = t,
                        Success = false,
                        ErrorMessage = "Connection validation failed"
                    }).ToList()
                };
            }
        }

        // Deploy to each target in parallel (with max parallelism limit)
        var semaphore = new SemaphoreSlim(settings.Options.MaxParallelDeployments);
        var deploymentTasks = targets
            .Where(t => !invalidTargets.Contains(t.Name))
            .Select(async target =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await DeployToSingleTargetAsync(
                        settings,
                        target,
                        schemaOnly,
                        dataOnly,
                        cancellationToken
                    );
                }
                finally
                {
                    semaphore.Release();
                }
            });

        var results = await Task.WhenAll(deploymentTasks);

        overallStopwatch.Stop();

        var summary = new DeploymentSummary
        {
            Results = results.ToList(),
            TotalDuration = overallStopwatch.Elapsed
        };

        _logger.LogInformation(
            "Deployment complete: {SuccessCount}/{TotalCount} targets succeeded in {Duration}",
            summary.SuccessCount,
            summary.Results.Count,
            summary.TotalDuration
        );

        return summary;
    }

    /// <summary>
    /// Deploys schema and data to a single target database
    /// </summary>
    private async Task<DeploymentResult> DeployToSingleTargetAsync(
        AppSettings settings,
        DatabaseConfig target,
        bool schemaOnly,
        bool dataOnly,
        CancellationToken cancellationToken)
    {
        var result = new DeploymentResult
        {
            TargetName = target.Name,
            StartTime = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Starting deployment to {Target}...", target.Name);

            // Schema deployment
            if (!dataOnly)
            {
                result.LogMessages.Add("Starting schema comparison...");

                var comparisonResult = await _schemaService.CompareSchemaAsync(
                    settings.SourceDatabase.ConnectionString,
                    target.ConnectionString,
                    settings.Options,
                    settings.ExcludedTables
                );

                if (comparisonResult == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "Schema comparison failed";
                    result.LogMessages.Add("ERROR: Schema comparison failed");
                    return result;
                }

                result.LogMessages.Add($"Found {comparisonResult.DifferenceCount} schema difference(s)");

                if (settings.Options.PreviewMode)
                {
                    result.LogMessages.Add("Preview mode: Generating deployment script...");

                    var script = await _schemaService.GenerateDeploymentScriptAsync(
                        comparisonResult,
                        target.Name
                    );

                    if (script != null)
                    {
                        result.LogMessages.Add($"Generated script ({script.Length} characters)");
                        result.LogMessages.Add("Preview mode: Skipping actual deployment");
                    }
                }
                else if (comparisonResult.DifferenceCount > 0)
                {
                    result.LogMessages.Add("Deploying schema changes...");

                    var deployed = await _schemaService.DeploySchemaAsync(
                        comparisonResult,
                        target.Name,
                        settings.Options
                    );

                    if (!deployed)
                    {
                        result.Success = false;
                        result.ErrorMessage = "Schema deployment failed";
                        result.LogMessages.Add("ERROR: Schema deployment failed");
                        return result;
                    }

                    result.SchemaChangesApplied = comparisonResult.DifferenceCount;
                    result.LogMessages.Add($"Successfully applied {result.SchemaChangesApplied} schema change(s)");
                }
                else
                {
                    result.LogMessages.Add("No schema changes needed");
                }
            }

            // Data synchronization for config tables
            if (!schemaOnly && settings.ConfigTables.Any())
            {
                result.LogMessages.Add($"Starting data sync for {settings.ConfigTables.Count} config table(s)...");

                foreach (var tableName in settings.ConfigTables)
                {
                    result.LogMessages.Add($"Syncing table: {tableName}");

                    var syncResult = await _dataSyncService.SyncConfigTableAsync(
                        settings.SourceDatabase.ConnectionString,
                        target.ConnectionString,
                        tableName,
                        settings.Options.UseTransaction,
                        cancellationToken
                    );

                    if (!syncResult.Success)
                    {
                        result.Success = false;
                        result.ErrorMessage = $"Data sync failed for table {tableName}: {syncResult.ErrorMessage}";
                        result.LogMessages.Add($"ERROR: Failed to sync {tableName}");

                        if (!settings.Options.ContinueOnError)
                        {
                            return result;
                        }
                    }
                    else
                    {
                        result.ConfigTablesSynced++;
                        result.LogMessages.Add($"  {tableName}: Synced {syncResult.RowsSynced} row(s)");
                    }
                }

                result.LogMessages.Add($"Data sync complete: {result.ConfigTablesSynced}/{settings.ConfigTables.Count} table(s) synced");
            }

            result.Success = true;
            result.LogMessages.Add($"Deployment to {target.Name} completed successfully");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.LogMessages.Add($"ERROR: {ex.Message}");

            _logger.LogError(ex, "Error deploying to {Target}: {ErrorMessage}", target.Name, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
            result.EndTime = DateTime.UtcNow;
            result.Duration = stopwatch.Elapsed;

            _logger.LogInformation(
                "Deployment to {Target} {Status} in {Duration}",
                target.Name,
                result.Success ? "succeeded" : "failed",
                result.Duration
            );
        }

        return result;
    }
}
