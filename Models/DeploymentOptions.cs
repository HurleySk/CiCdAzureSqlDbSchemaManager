namespace CiCdAzureSqlDbSchemaManager.Models;

/// <summary>
/// Deployment configuration options
/// </summary>
public class DeploymentOptions
{
    /// <summary>
    /// If true, show deployment changes without applying them
    /// </summary>
    public bool PreviewMode { get; set; } = false;

    /// <summary>
    /// Maximum number of parallel target deployments
    /// </summary>
    public int MaxParallelDeployments { get; set; } = 3;

    /// <summary>
    /// If true, create BACPAC backup before deployment
    /// </summary>
    public bool BackupBeforeDeployment { get; set; } = false;

    /// <summary>
    /// Deployment timeout in seconds
    /// </summary>
    public int DeploymentTimeout { get; set; } = 300;

    /// <summary>
    /// If true, continue deploying to other targets even if one fails
    /// </summary>
    public bool ContinueOnError { get; set; } = true;

    /// <summary>
    /// If true, block destructive changes (e.g., dropping tables/columns)
    /// </summary>
    public bool BlockDestructiveChanges { get; set; } = false;

    /// <summary>
    /// If true, ignore column order differences
    /// </summary>
    public bool IgnoreColumnOrder { get; set; } = true;

    /// <summary>
    /// If true, ignore index differences
    /// </summary>
    public bool IgnoreIndexOptions { get; set; } = false;

    /// <summary>
    /// If true, ignore comments
    /// </summary>
    public bool IgnoreComments { get; set; } = true;

    /// <summary>
    /// If true, use transactions for deployment
    /// </summary>
    public bool UseTransaction { get; set; } = true;
}
