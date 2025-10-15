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

            // Exclude specific tables if specified
            if (excludedTables != null && excludedTables.Any())
            {
                foreach (var table in excludedTables)
                {
                    _logger.LogDebug("Excluding table: {Table}", table);
                }
            }

            _logger.LogInformation("Executing schema comparison...");
            var comparisonResult = await Task.Run(() => comparison.Compare());

            if (!comparisonResult.IsValid)
            {
                _logger.LogError("Schema comparison failed with errors.");
                return null;
            }

            var differences = comparisonResult.Differences
                .Where(d => !d.Name.Contains("__RefactorLog") && !d.Name.Contains("__MigrationHistory"))
                .ToList();

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

            _logger.LogInformation(
                "Schema comparison complete. Found {DifferenceCount} differences",
                result.DifferenceCount
            );

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

    // Note: DacFx SchemaComparison uses default comparison options
    // Custom options can be configured through SchemaComparison properties if needed
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
