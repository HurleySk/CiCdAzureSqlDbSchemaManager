using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using CiCdAzureSqlDbSchemaManager.Configuration;
using CiCdAzureSqlDbSchemaManager.Services;
using System.Diagnostics;

namespace CiCdAzureSqlDbSchemaManager;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Azure SQL DB Schema Manager");
        Console.WriteLine("===========================\n");

        try
        {
            // Parse command line arguments
            var cliArgs = ParseArguments(args);

            // Load configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                .Build();

            var settings = configuration.Get<AppSettings>();
            if (settings == null)
            {
                Console.WriteLine("ERROR: Failed to load application settings");
                return 1;
            }

            // Apply CLI overrides
            ApplyCliOverrides(settings, cliArgs);

            // Setup dependency injection
            var services = new ServiceCollection();
            ConfigureServices(services, settings);

            using var serviceProvider = services.BuildServiceProvider();

            // Execute deployment
            var deploymentService = serviceProvider.GetRequiredService<ParallelDeploymentService>();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // Display configuration summary
            DisplayConfigurationSummary(settings, cliArgs, logger);

            // Confirm before proceeding (unless --yes flag is provided)
            if (!cliArgs.AutoConfirm && !settings.Options.PreviewMode)
            {
                Console.Write("\nProceed with deployment? (y/n): ");
                var response = Console.ReadLine();
                if (response?.Trim().ToLower() != "y")
                {
                    Console.WriteLine("Deployment cancelled by user.");
                    return 0;
                }
            }

            Console.WriteLine();

            // Execute deployment
            var stopwatch = Stopwatch.StartNew();

            var summary = await deploymentService.DeployToTargetsAsync(
                settings,
                cliArgs.SchemaOnly,
                cliArgs.DataOnly,
                cliArgs.TargetFilter
            );

            stopwatch.Stop();

            // Display results
            DisplayDeploymentSummary(summary);

            return summary.OverallSuccess ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFATAL ERROR: {ex.Message}");
            Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
            return 1;
        }
    }

    static void ConfigureServices(IServiceCollection services, AppSettings settings)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Application services
        services.AddSingleton(settings);
        services.AddTransient<DatabaseConnectionService>();
        services.AddTransient<SchemaDeploymentService>();
        services.AddTransient<DataSyncService>();
        services.AddTransient<ParallelDeploymentService>();
    }

    static CliArguments ParseArguments(string[] args)
    {
        var cliArgs = new CliArguments();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--preview":
                case "-p":
                    cliArgs.PreviewMode = true;
                    break;

                case "--schema-only":
                case "-s":
                    cliArgs.SchemaOnly = true;
                    break;

                case "--data-only":
                case "-d":
                    cliArgs.DataOnly = true;
                    break;

                case "--targets":
                case "-t":
                    if (i + 1 < args.Length)
                    {
                        cliArgs.TargetFilter = args[++i].Split(',').Select(t => t.Trim()).ToList();
                    }
                    break;

                case "--yes":
                case "-y":
                    cliArgs.AutoConfirm = true;
                    break;

                case "--help":
                case "-h":
                case "/?":
                    DisplayHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        return cliArgs;
    }

    static void ApplyCliOverrides(AppSettings settings, CliArguments cliArgs)
    {
        if (cliArgs.PreviewMode)
        {
            settings.Options.PreviewMode = true;
        }
    }

    static void DisplayConfigurationSummary(AppSettings settings, CliArguments cliArgs, ILogger logger)
    {
        Console.WriteLine("Configuration:");
        Console.WriteLine($"  Source Database: {settings.SourceDatabase.Name}");
        Console.WriteLine($"  Target Databases: {settings.TargetDatabases.Count}");

        if (cliArgs.TargetFilter != null && cliArgs.TargetFilter.Any())
        {
            Console.WriteLine($"    Filtered to: {string.Join(", ", cliArgs.TargetFilter)}");
        }
        else
        {
            foreach (var target in settings.TargetDatabases)
            {
                Console.WriteLine($"    - {target.Name}");
            }
        }

        Console.WriteLine($"  Config Tables: {settings.ConfigTables.Count}");
        foreach (var table in settings.ConfigTables)
        {
            Console.WriteLine($"    - {table}");
        }

        Console.WriteLine("\nOptions:");
        Console.WriteLine($"  Preview Mode: {settings.Options.PreviewMode}");
        Console.WriteLine($"  Schema Only: {cliArgs.SchemaOnly}");
        Console.WriteLine($"  Data Only: {cliArgs.DataOnly}");
        Console.WriteLine($"  Max Parallel: {settings.Options.MaxParallelDeployments}");
        Console.WriteLine($"  Block Destructive: {settings.Options.BlockDestructiveChanges}");
        Console.WriteLine($"  Continue On Error: {settings.Options.ContinueOnError}");
    }

    static void DisplayDeploymentSummary(CiCdAzureSqlDbSchemaManager.Models.DeploymentSummary summary)
    {
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("DEPLOYMENT SUMMARY");
        Console.WriteLine(new string('=', 60));

        Console.WriteLine($"\nTotal Duration: {summary.TotalDuration:hh\\:mm\\:ss}");
        Console.WriteLine($"Targets: {summary.Results.Count}");
        Console.WriteLine($"  Succeeded: {summary.SuccessCount}");
        Console.WriteLine($"  Failed: {summary.FailureCount}");

        Console.WriteLine("\nDetailed Results:");
        Console.WriteLine(new string('-', 60));

        foreach (var result in summary.Results)
        {
            var status = result.Success ? "SUCCESS" : "FAILED";
            var statusColor = result.Success ? ConsoleColor.Green : ConsoleColor.Red;

            Console.Write($"\n{result.TargetName}: ");
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = statusColor;
            Console.WriteLine(status);
            Console.ForegroundColor = originalColor;

            Console.WriteLine($"  Duration: {result.Duration:mm\\:ss}");

            if (result.Success)
            {
                Console.WriteLine($"  Schema Changes: {result.SchemaChangesApplied}");
                Console.WriteLine($"  Config Tables Synced: {result.ConfigTablesSynced}");
            }
            else
            {
                Console.WriteLine($"  Error: {result.ErrorMessage}");
            }

            if (result.LogMessages.Any())
            {
                Console.WriteLine("  Log Messages:");
                foreach (var msg in result.LogMessages.Take(5))
                {
                    Console.WriteLine($"    {msg}");
                }

                if (result.LogMessages.Count > 5)
                {
                    Console.WriteLine($"    ... and {result.LogMessages.Count - 5} more");
                }
            }
        }

        Console.WriteLine("\n" + new string('=', 60));

        if (summary.OverallSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("DEPLOYMENT COMPLETED SUCCESSFULLY");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("DEPLOYMENT COMPLETED WITH ERRORS");
            Console.ResetColor();
        }

        Console.WriteLine(new string('=', 60));
    }

    static void DisplayHelp()
    {
        Console.WriteLine(@"
Azure SQL DB Schema Manager
============================

Usage: CiCdAzureSqlDbSchemaManager [options]

Options:
  --preview, -p         Preview mode - show changes without applying them
  --schema-only, -s     Deploy only schema changes (skip data sync)
  --data-only, -d       Sync only config table data (skip schema)
  --targets, -t <list>  Comma-separated list of target database names to deploy to
  --yes, -y             Auto-confirm deployment without prompting
  --help, -h            Display this help message

Examples:
  # Preview deployment to all targets
  CiCdAzureSqlDbSchemaManager --preview

  # Deploy to specific targets only
  CiCdAzureSqlDbSchemaManager --targets QA,Staging

  # Deploy only schema changes
  CiCdAzureSqlDbSchemaManager --schema-only

  # Deploy only config table data
  CiCdAzureSqlDbSchemaManager --data-only

  # Auto-confirm deployment
  CiCdAzureSqlDbSchemaManager --yes

Configuration:
  Edit appsettings.json to configure source database, target databases,
  config tables, and deployment options.
");
    }
}

class CliArguments
{
    public bool PreviewMode { get; set; }
    public bool SchemaOnly { get; set; }
    public bool DataOnly { get; set; }
    public List<string>? TargetFilter { get; set; }
    public bool AutoConfirm { get; set; }
}
