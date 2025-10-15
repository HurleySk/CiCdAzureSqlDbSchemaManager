namespace CiCdAzureSqlDbSchemaManager.Models;

/// <summary>
/// Represents the result of a deployment operation to a single target database
/// </summary>
public class DeploymentResult
{
    /// <summary>
    /// Target database name
    /// </summary>
    public string TargetName { get; set; } = string.Empty;

    /// <summary>
    /// True if the deployment succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if deployment failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Detailed deployment log messages
    /// </summary>
    public List<string> LogMessages { get; set; } = new();

    /// <summary>
    /// Number of schema changes applied
    /// </summary>
    public int SchemaChangesApplied { get; set; }

    /// <summary>
    /// Number of config tables synchronized
    /// </summary>
    public int ConfigTablesSynced { get; set; }

    /// <summary>
    /// Deployment duration
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Start time of deployment
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// End time of deployment
    /// </summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// Represents the overall deployment summary for all targets
/// </summary>
public class DeploymentSummary
{
    /// <summary>
    /// Individual results for each target database
    /// </summary>
    public List<DeploymentResult> Results { get; set; } = new();

    /// <summary>
    /// Total number of successful deployments
    /// </summary>
    public int SuccessCount => Results.Count(r => r.Success);

    /// <summary>
    /// Total number of failed deployments
    /// </summary>
    public int FailureCount => Results.Count(r => !r.Success);

    /// <summary>
    /// Overall success (all targets succeeded)
    /// </summary>
    public bool OverallSuccess => Results.All(r => r.Success);

    /// <summary>
    /// Total duration of all deployments
    /// </summary>
    public TimeSpan TotalDuration { get; set; }
}
