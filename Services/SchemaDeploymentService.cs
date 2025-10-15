namespace CiCdAzureSqlDbSchemaManager.Services;

using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Compare;
using Microsoft.Extensions.Logging;
using CiCdAzureSqlDbSchemaManager.Models;

/// <summary>
/// Service for comparing and deploying database schemas using DacFx
/// </summary>
public class SchemaDeploymentService
{
    private readonly ILogger<SchemaDeploymentService> _logger;

    public SchemaDeploymentService(ILogger<SchemaDeploymentService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Compares schema between source and target databases
    /// </summary>
    public async Task<SchemaComparisonResult?> CompareSchemaAsync(
        string sourceConnectionString,
        string targetConnectionString,
        DeploymentOptions options,
        List<string>? excludedTables = null)
    {
        try
        {
            _logger.LogInformation("Starting schema comparison...");

            var sourceEndpoint = new SchemaCompareDatabaseEndpoint(sourceConnectionString);
            var targetEndpoint = new SchemaCompareDatabaseEndpoint(targetConnectionString);

            var comparison = new SchemaComparison(sourceEndpoint, targetEndpoint);

            _logger.LogInformation(
                "Schema comparison will include: Tables, Stored Procedures, Functions, Views, Triggers, and other database objects"
            );

            // Log table exclusions if specified
            if (excludedTables != null && excludedTables.Any())
            {
                _logger.LogInformation("Table exclusions configured: {Count} table(s) will be excluded", excludedTables.Count);
                foreach (var tableName in excludedTables)
                {
                    _logger.LogDebug("  - Will exclude table: {Table}", tableName);
                }
            }

            _logger.LogInformation("Executing schema comparison...");
            var comparisonResult = await Task.Run(() => comparison.Compare());

            if (!comparisonResult.IsValid)
            {
                _logger.LogError("Schema comparison failed with errors.");
                return null;
            }

            // Filter out system tables and excluded tables
            var differences = comparisonResult.Differences
                .Where(d => !d.Name.Contains("__RefactorLog") && !d.Name.Contains("__MigrationHistory"))
                .Where(d => !IsExcludedTable(d.Name, excludedTables))
                .ToList();

            // Log excluded differences
            var totalDifferences = comparisonResult.Differences.Count(d =>
                !d.Name.Contains("__RefactorLog") && !d.Name.Contains("__MigrationHistory"));
            var excludedCount = totalDifferences - differences.Count;

            if (excludedCount > 0)
            {
                _logger.LogInformation("Filtered out {Count} difference(s) related to excluded tables", excludedCount);
            }

            // Log detailed breakdown of differences by object type
            var differencesByType = differences
                .GroupBy(d => d.DifferenceType.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            _logger.LogInformation("Schema comparison complete. Found {DifferenceCount} differences", differences.Count);

            if (differencesByType.Any())
            {
                _logger.LogInformation("Differences by object type:");
                foreach (var kvp in differencesByType.OrderByDescending(x => x.Value))
                {
                    _logger.LogInformation("  - {ObjectType}: {Count}", kvp.Key, kvp.Value);
                }
            }

            var result = new SchemaComparisonResult
            {
                IsEqual = !differences.Any(),
                DifferenceCount = differences.Count,
                Differences = differences.Select(d => new SchemaDifference
                {
                    Name = d.Name,
                    UpdateAction = d.UpdateAction.ToString(),
                    DifferenceType = d.DifferenceType.ToString()
                }).ToList(),
                ComparisonResult = comparisonResult
            };

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during schema comparison: {ErrorMessage}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Deploys schema changes from source to target
    /// </summary>
    public async Task<bool> DeploySchemaAsync(
        SchemaComparisonResult comparisonResult,
        string targetName,
        DeploymentOptions options)
    {
        try
        {
            if (comparisonResult.IsEqual)
            {
                _logger.LogInformation("No schema changes to deploy to {Target}", targetName);
                return true;
            }

            if (options.BlockDestructiveChanges)
            {
                var destructiveChanges = comparisonResult.Differences
                    .Where(d => d.UpdateAction.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
                               d.UpdateAction.Contains("Drop", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (destructiveChanges.Any())
                {
                    _logger.LogWarning(
                        "Blocking deployment to {Target} due to {Count} destructive changes",
                        targetName,
                        destructiveChanges.Count
                    );
                    foreach (var change in destructiveChanges)
                    {
                        _logger.LogWarning("  - {Name}: {Action}", change.Name, change.UpdateAction);
                    }
                    return false;
                }
            }

            if (options.PreviewMode)
            {
                _logger.LogInformation("Preview mode: Skipping actual deployment to {Target}", targetName);
                return true;
            }

            _logger.LogInformation("Deploying {Count} schema changes to {Target}...",
                comparisonResult.DifferenceCount,
                targetName
            );

            await Task.Run(() =>
            {
                comparisonResult.ComparisonResult?.PublishChangesToDatabase();
            });

            _logger.LogInformation("Schema deployment to {Target} completed successfully", targetName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error deploying schema to {Target}: {ErrorMessage}",
                targetName,
                ex.Message
            );
            return false;
        }
    }

    /// <summary>
    /// Generates a deployment script without applying changes
    /// </summary>
    public async Task<string?> GenerateDeploymentScriptAsync(
        SchemaComparisonResult comparisonResult,
        string targetName)
    {
        try
        {
            if (comparisonResult.IsEqual)
            {
                _logger.LogInformation("No schema changes - script would be empty for {Target}", targetName);
                return string.Empty;
            }

            _logger.LogInformation("Generating deployment script for {Target}...", targetName);

            var scriptResult = await Task.Run(() =>
            {
                var result = comparisonResult.ComparisonResult?.GenerateScript(targetName);
                return result;
            });

            var script = scriptResult?.Script;

            _logger.LogInformation(
                "Generated deployment script for {Target} ({Length} characters)",
                targetName,
                script?.Length ?? 0
            );

            return script;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error generating deployment script for {Target}: {ErrorMessage}",
                targetName,
                ex.Message
            );
            return null;
        }
    }

    /// <summary>
    /// Checks if a difference name matches an excluded table
    /// </summary>
    private bool IsExcludedTable(string differenceName, List<string>? excludedTables)
    {
        if (excludedTables == null || !excludedTables.Any())
            return false;

        // Normalize the difference name to check against excluded tables
        // DacFx difference names are typically in format like "[dbo].[TableName]" or include the object type
        foreach (var excludedTable in excludedTables)
        {
            // Parse the excluded table name
            var parts = excludedTable.Split('.');
            string schema = parts.Length > 1 ? parts[0].Trim() : "dbo";
            string tableName = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim();

            // Check if the difference name contains the table reference
            // Match patterns like: [schema].[table], schema.table, or just [table]
            if (differenceName.Contains($"[{schema}].[{tableName}]", StringComparison.OrdinalIgnoreCase) ||
                differenceName.Contains($"{schema}.{tableName}", StringComparison.OrdinalIgnoreCase) ||
                differenceName.Contains($"[{tableName}]", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Result of a schema comparison
/// </summary>
public class SchemaComparisonResult
{
    /// <summary>
    /// True if schemas are identical
    /// </summary>
    public bool IsEqual { get; set; }

    /// <summary>
    /// Number of differences found
    /// </summary>
    public int DifferenceCount { get; set; }

    /// <summary>
    /// List of schema differences
    /// </summary>
    public List<SchemaDifference> Differences { get; set; } = new();

    /// <summary>
    /// The underlying DacFx comparison result (used for deployment)
    /// </summary>
    public Microsoft.SqlServer.Dac.Compare.SchemaComparisonResult? ComparisonResult { get; set; }
}

/// <summary>
/// Represents a single schema difference
/// </summary>
public class SchemaDifference
{
    public string Name { get; set; } = string.Empty;
    public string UpdateAction { get; set; } = string.Empty;
    public string DifferenceType { get; set; } = string.Empty;
}
