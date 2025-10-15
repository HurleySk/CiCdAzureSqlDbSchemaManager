# Azure SQL DB Schema Manager

A .NET console application for managing Azure SQL Database schema deployments and config table data synchronization from a development database to multiple target environments.

## Features

- **Schema Comparison & Deployment**: Uses Microsoft SqlPackage DacFx to compare and deploy schema changes
  - Includes: Tables, Stored Procedures, Functions, Views, Triggers, and other database objects
  - Configurable table exclusions for selective deployments
- **Config Table Data Sync**: Automatically synchronizes specified "config tables" data from source to targets
- **Multi-Target Deployment**: Deploy to multiple databases in parallel with configurable parallelism
- **Preview Mode**: See what changes will be applied without actually deploying them
- **Flexible CLI**: Command-line arguments for various deployment scenarios
- **Safety Features**:
  - Block destructive changes (optional)
  - Transaction support for data sync
  - Continue-on-error mode
  - Pre-deployment connection validation
- **Comprehensive Logging**: Detailed logging of all operations with object-type breakdowns

## Prerequisites

- .NET 9.0 SDK or later
- Access to Azure SQL Databases (or SQL Server)
- Appropriate permissions to read from source and write to target databases

## Installation

1. Clone this repository
2. Copy the example configuration file to create your local settings:
   ```bash
   cp appsettings.example.json appsettings.json
   ```
3. Configure your database connections in `appsettings.json` (see Configuration section below)
4. Build the project:
   ```bash
   dotnet build
   ```

**Important**: `appsettings.json` is gitignored to protect your connection strings. Never commit this file with real credentials.

## Configuration

Edit `appsettings.json` (created from `appsettings.example.json`) to configure your deployment:

```json
{
  "SourceDatabase": {
    "Name": "Development",
    "ConnectionString": "Server=your-dev-server.database.windows.net;Database=your-dev-db;..."
  },
  "TargetDatabases": [
    {
      "Name": "QA",
      "ConnectionString": "Server=your-qa-server.database.windows.net;Database=your-qa-db;..."
    },
    {
      "Name": "Staging",
      "ConnectionString": "Server=your-staging-server.database.windows.net;Database=your-staging-db;..."
    }
  ],
  "ConfigTables": [
    "dbo.AppSettings",
    "dbo.FeatureFlags",
    "dbo.SystemConfiguration"
  ],
  "Options": {
    "PreviewMode": false,
    "MaxParallelDeployments": 3,
    "BackupBeforeDeployment": false,
    "DeploymentTimeout": 300,
    "ContinueOnError": true,
    "BlockDestructiveChanges": false,
    "IgnoreColumnOrder": true,
    "IgnoreIndexOptions": false,
    "IgnoreComments": true,
    "UseTransaction": true
  },
  "ExcludedTables": [],
  "IncludedSchemas": []
}
```

### Configuration Options

#### Database Configuration
- **SourceDatabase**: The development database to deploy FROM
- **TargetDatabases**: List of target databases to deploy TO
- **ConfigTables**: Tables that should have both schema AND data synchronized
- **ExcludedTables**: Tables to exclude from schema comparison and deployment
  - Format: `"schema.tablename"` or just `"tablename"` (defaults to `dbo` schema)
  - Example: `["dbo.TempTable", "audit.AuditLog", "SystemLog"]`
  - Use cases: Exclude temporary tables, audit logs, or environment-specific tables
- **IncludedSchemas**: Optional list of schemas to include (if empty, all schemas are included)

#### Deployment Options
- **PreviewMode**: If true, shows changes without applying them
- **MaxParallelDeployments**: Maximum number of target databases to deploy to simultaneously
- **BackupBeforeDeployment**: Create BACPAC backup before deployment (future feature)
- **DeploymentTimeout**: Timeout in seconds for deployment operations
- **ContinueOnError**: If true, continues deploying to other targets even if one fails
- **BlockDestructiveChanges**: If true, prevents dropping tables or columns
- **IgnoreColumnOrder**: Ignore differences in column ordering
- **IgnoreIndexOptions**: Ignore index option differences
- **IgnoreComments**: Ignore comment differences
- **UseTransaction**: Wrap data sync operations in transactions

## Usage

### Basic Deployment

Deploy schema and config data to all configured targets:

```bash
dotnet run
```

or after building:

```bash
./bin/Debug/net9.0/CiCdAzureSqlDbSchemaManager
```

### Command-Line Options

#### Preview Mode
See what changes will be made without applying them:

```bash
dotnet run -- --preview
```

#### Deploy to Specific Targets
Deploy only to QA and Staging environments:

```bash
dotnet run -- --targets QA,Staging
```

#### Schema Only
Deploy only schema changes, skip config table data sync:

```bash
dotnet run -- --schema-only
```

#### Data Only
Synchronize only config table data, skip schema deployment:

```bash
dotnet run -- --data-only
```

#### Auto-Confirm
Skip the confirmation prompt:

```bash
dotnet run -- --yes
```

#### Combined Options
```bash
dotnet run -- --preview --targets QA
dotnet run -- --schema-only --yes
dotnet run -- --targets Staging --data-only
```

### Excluding Tables from Deployment

To exclude specific tables from schema comparison and deployment, add them to the `ExcludedTables` array in your `appsettings.json`:

```json
{
  "ExcludedTables": [
    "dbo.TempTable",
    "audit.AuditLog",
    "SystemLog",
    "dbo.SessionData"
  ]
}
```

This is useful for:
- **Environment-specific tables**: Tables that should remain unique per environment
- **Audit/logging tables**: Tables that shouldn't be overwritten with dev data structure
- **Temporary tables**: Tables used for testing or temporary operations
- **Large historical data tables**: Tables where schema changes aren't needed

**Note**: Excluded tables will not be compared or modified during deployment, but they will remain in the target database unchanged.

### Help

Display help information:

```bash
dotnet run -- --help
```

## How It Works

### Schema Deployment Process

1. **Connection Validation**: Validates connectivity to source and all target databases
2. **Schema Comparison**: Uses DacFx SchemaComparison to compare source and target schemas
   - Compares all database objects: Tables, Stored Procedures, Functions, Views, Triggers, etc.
   - Applies table exclusions if configured
   - Provides detailed breakdown of differences by object type
3. **Change Analysis**: Identifies differences (tables, columns, indexes, stored procedures, functions, views, triggers, etc.)
4. **Destructive Change Check**: Optionally blocks destructive operations
5. **Script Generation**: Generates T-SQL deployment script (in preview mode)
6. **Deployment**: Applies schema changes to target database

### Config Table Data Sync Process

1. **Table Validation**: Ensures tables exist in both source and target
2. **Data Extraction**: Reads all data from source table
3. **Transaction Begin**: Starts transaction (if UseTransaction is true)
4. **Truncate**: Clears target table
5. **Bulk Insert**: Efficiently copies all data to target using SqlBulkCopy
6. **Transaction Commit**: Commits changes

### Parallel Deployment

The tool deploys to multiple targets in parallel using:
- `Task.WhenAll()` for parallel execution
- `SemaphoreSlim` to limit max parallelism
- Individual error handling per target
- Comprehensive result tracking

## Project Structure

```
CiCdAzureSqlDbSchemaManager/
├── Configuration/
│   └── AppSettings.cs              # Configuration model
├── Models/
│   ├── DatabaseConfig.cs           # Database connection configuration
│   ├── DeploymentOptions.cs        # Deployment settings
│   └── DeploymentResult.cs         # Result tracking models
├── Services/
│   ├── DatabaseConnectionService.cs    # Connection management & validation
│   ├── SchemaDeploymentService.cs      # DacFx schema comparison/deployment
│   ├── DataSyncService.cs              # Config table data synchronization
│   └── ParallelDeploymentService.cs    # Multi-target orchestration
├── Program.cs                      # Entry point & CLI
├── appsettings.example.json       # Configuration template (commit this)
├── appsettings.json               # Your config file (gitignored)
└── README.md                      # This file
```

## Safety Considerations

### Recommended Practices

1. **Always test in lower environments first**: QA → Staging → Production
2. **Use Preview Mode**: Run with `--preview` first to see what will change
3. **Enable BlockDestructiveChanges** for production deployments
4. **Backup databases** before major schema changes
5. **Use specific target filters** to deploy incrementally
6. **Review config tables carefully**: Data sync completely replaces target table data

### Connection Strings Security

- **Never commit** `appsettings.json` with real credentials to source control (it's already in .gitignore)
- The repository includes `appsettings.example.json` as a template - copy this to create your own `appsettings.json`
- Consider using **Azure Key Vault** or **Managed Identity** for production
- Use **Azure SQL firewall rules** to restrict access
- For CI/CD pipelines, use secure variable groups or secrets management

## Troubleshooting

### Connection Failures
- Verify firewall rules allow access to Azure SQL
- Check connection string format and credentials
- Ensure database exists

### Schema Deployment Failures
- Review differences in preview mode first
- Check for blocking destructive changes setting
- Verify sufficient permissions (db_owner or db_ddladmin)

### Data Sync Failures
- Ensure config tables exist in both source and target
- Check for referential integrity constraints
- Verify table schemas match (columns, data types)

## Future Enhancements

- BACPAC backup before deployment
- Differential data sync (MERGE instead of TRUNCATE/INSERT)
- SQL script file generation
- Rollback support
- Email notifications
- Integration with Azure DevOps Pipelines
- Support for dacpac files as source

## License

This project is provided as-is for demonstration and internal use.

## Contributing

Contributions are welcome! Please submit pull requests or open issues for bugs and feature requests.
