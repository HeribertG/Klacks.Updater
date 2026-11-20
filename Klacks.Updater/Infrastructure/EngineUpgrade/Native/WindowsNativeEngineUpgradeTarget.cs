// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Engine upgrade target of the native Windows installation. Inspection reads the KlacksPostgres ImagePath (the truth
/// about which binaries and data directory run), PG_VERSION, the pointer and the configured bin directory into OldState.
/// The artifact is downloaded into staging, checked against the signed SHA-256, unpacked and verified (postgres.exe
/// version resource, lib\vector.dll) before it is moved to &lt;ProgramRoot&gt;\pgsql-&lt;major&gt;. Dumps are taken with the new
/// pg_dump per database. The old service is stopped without its dependents (KlacksUpdater loses its dependency on
/// KlacksPostgres for good first; fallback on SCM error 1051 is pg_ctl stop with the old binaries). The new cluster is
/// created in &lt;DataRoot&gt;\pgdata-&lt;major&gt; (ACL SYSTEM/Administrators/NetworkService full control like the installer, before initdb, a sidecar marker naming
/// the operation, scram, password file deleted in finally, the Klacks block of the old postgresql.conf), the service is
/// switched with sc.exe config binPath= and the result read back, databases are created from template0 with the old
/// encoding and locale and restored with pg_restore --exit-on-error. Commit writes the pointer and
/// Updater.PostgresBinDirectory. Revert restores ImagePath, pointer and configuration from OldState, deletes the new
/// data directory only when its marker names this operation, keeps the binaries and starts the old service.
/// </summary>
/// <param name="options">Updater configuration (program root, connection string, backup directory, service names)</param>
/// <param name="configuration">Host configuration; Hosting:DataRoot is the native data root</param>
/// <param name="httpClientFactory">Creates the client for the engine download</param>
/// <param name="serviceController">Stops and starts the Klacks application service</param>
/// <param name="processRunner">Runs initdb, pg_dump, pg_restore, pg_ctl and sc.exe with argument lists</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using Npgsql;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Native;

[SupportedOSPlatform("windows")]
public class WindowsNativeEngineUpgradeTarget : IEngineUpgradeTarget
{
    public const int DependentServicesRunningError = 1051;

    private const string ImagePathValueName = "ImagePath";
    private const string ArchiveFileName = "engine.zip";
    private const string ExtractDirectoryName = "extracted";
    private const string PasswordFileSuffix = ".pw";
    private const string EngineHashFileName = "klacks-engine.sha256";
    private const string HttpsScheme = "https";
    private const string LibpqDatabaseVariable = "PGDATABASE";
    private const string LibpqHostVariable = "PGHOST";
    private const string LibpqPortVariable = "PGPORT";
    private const string LibpqUserVariable = "PGUSER";
    private const string LibpqPasswordVariable = "PGPASSWORD";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ConnectRetryInterval = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions PointerSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly UpdaterOptions _options;
    private readonly NativeEngineLayout _layout;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceController _serviceController;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<WindowsNativeEngineUpgradeTarget> _logger;
    private readonly NpgsqlConnectionStringBuilder _connection;

    public WindowsNativeEngineUpgradeTarget(
        IOptions<UpdaterOptions> options,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IServiceController serviceController,
        IProcessRunner processRunner,
        ILogger<WindowsNativeEngineUpgradeTarget> logger)
    {
        _options = options.Value;
        _layout = new NativeEngineLayout(_options.NativeProgramRoot, configuration[UpdaterConstants.NativeDataRootKey] ?? string.Empty);
        _httpClientFactory = httpClientFactory;
        _serviceController = serviceController;
        _processRunner = processRunner;
        _logger = logger;
        _connection = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
    }

    private string EngineUpgradeDirectory => Path.Combine(_options.BackupDirectory, UpdaterConstants.EngineUpgradeDirectoryName);

    public Task<EngineInspection> InspectAsync(CancellationToken cancellationToken)
    {
        var serviceName = _options.NativePostgresServiceName;
        var imagePath = ReadImagePath(serviceName);
        var parsed = imagePath is null ? null : NativeEngineCommands.ParseServiceImagePath(imagePath);
        if (parsed is null)
        {
            return Task.FromResult(Unsupported($"The service {serviceName} is missing or its path is not a pg_ctl runservice command."));
        }

        var (pgCtlPath, dataDirectory) = parsed.Value;
        if (!_layout.IsPinnedPgCtl(pgCtlPath) || !_layout.IsPinnedDataDirectory(dataDirectory))
        {
            return Task.FromResult(Unsupported($"The service {serviceName} runs an engine outside the installation roots."));
        }

        var versionFile = Path.Combine(dataDirectory, NativeEngineLayout.VersionFileName);
        if (!File.Exists(versionFile)
            || !int.TryParse(File.ReadAllText(versionFile).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var currentMajor))
        {
            return Task.FromResult(Unsupported($"The data directory {dataDirectory} has no readable PG_VERSION."));
        }

        var oldState = new Dictionary<string, string>
        {
            [NativeEngineStateKeys.ImagePath] = imagePath!,
            [NativeEngineStateKeys.BinDirectory] = Path.GetDirectoryName(pgCtlPath) ?? string.Empty,
            [NativeEngineStateKeys.DataDirectory] = dataDirectory,
            [NativeEngineStateKeys.Pointer] = PostgresEnginePointerFile.ReadRaw(_layout.PointerPath) ?? NativeEngineStateKeys.Missing,
            [NativeEngineStateKeys.ConfiguredBinDirectory] = PostgresEnginePointerFile.ReadPostgresBinDirectory(_layout.NativeConfigPath) ?? string.Empty,
        };

        var freeBytes = Math.Min(FreeBytes(_layout.DataRoot), FreeBytes(_options.BackupDirectory));
        return Task.FromResult(new EngineInspection(currentMajor, oldState, freeBytes, null));
    }

    public async Task PrepareArtifactAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        var engineDirectory = _layout.EngineDirectory(context.ToMajor);
        if (Directory.Exists(engineDirectory))
        {
            var recordedSha256 = Path.Combine(engineDirectory, EngineHashFileName);
            if (!File.Exists(recordedSha256) || !string.Equals(File.ReadAllText(recordedSha256).Trim(), context.ArtifactSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{engineDirectory} exists but was not installed from this signed engine bundle; it is left untouched.");
            }

            VerifyEngine(engineDirectory, context.ToMajor);
            _logger.LogInformation("Reusing the verified engine in {Directory}.", engineDirectory);
        }
        else
        {
            await InstallEngineAsync(context, engineDirectory, cancellationToken);
        }

        context.NewState[NativeEngineStateKeys.BinDirectory] = NativeEngineLayout.BinDirectory(engineDirectory);
    }

    public Task StopApplicationAsync(CancellationToken cancellationToken) => _serviceController.StopAsync(cancellationToken);

    public Task StartApplicationAsync(CancellationToken cancellationToken) => _serviceController.StartAsync(cancellationToken);

    public async Task<IReadOnlyList<EngineDump>> DumpAsync(EngineUpgradeContext context, IReadOnlyList<EngineDatabase> databases, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(EngineUpgradeDirectory);
        var pgDump = Path.Combine(NewBinDirectory(context), NativeEngineLayout.PgDumpExecutableName);
        var dumps = new List<EngineDump>();
        for (var index = 0; index < databases.Count; index++)
        {
            var dumpFile = Path.Combine(EngineUpgradeDirectory, NativeEngineCommands.DumpFileName(context.OperationId, index));
            var environment = LibpqEnvironment();
            environment[LibpqDatabaseVariable] = databases[index].Name;
            await _processRunner.RunAsync(pgDump, NativeEngineCommands.BuildPgDumpArguments(dumpFile), environment: environment, cancellationToken: cancellationToken);
            dumps.Add(new EngineDump(databases[index].Name, dumpFile));
        }

        context.NewState[NativeEngineStateKeys.Databases] = JsonSerializer.Serialize(databases);
        return dumps;
    }

    public async Task StopOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        await RemoveUpdaterDependencyAsync(cancellationToken);
        await StopPostgresServiceAsync(context.OldState[NativeEngineStateKeys.BinDirectory], context.OldState[NativeEngineStateKeys.DataDirectory], cancellationToken);
    }

    public async Task ActivateNewEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        var dataDirectory = _layout.DataDirectory(context.ToMajor);
        PrepareNewDataDirectory(context, dataDirectory);
        context.NewState[NativeEngineStateKeys.DataDirectory] = dataDirectory;

        var binDirectory = NewBinDirectory(context);
        await InitializeClusterAsync(binDirectory, dataDirectory, cancellationToken);

        var oldConfiguration = await File.ReadAllLinesAsync(Path.Combine(context.OldState[NativeEngineStateKeys.DataDirectory], NativeEngineLayout.ConfigurationFileName), cancellationToken);
        await File.AppendAllLinesAsync(
            Path.Combine(dataDirectory, NativeEngineLayout.ConfigurationFileName),
            NativeEngineCommands.KlacksConfigurationBlock(oldConfiguration, _connection.Port),
            cancellationToken);

        var imagePath = NativeEngineCommands.BuildServiceImagePath(
            Path.Combine(binDirectory, NativeEngineLayout.PgCtlExecutableName), _options.NativePostgresServiceName, dataDirectory);
        await SetImagePathAsync(imagePath, cancellationToken);
        StartPostgresService();
    }

    public async Task RestoreAsync(EngineUpgradeContext context, IReadOnlyList<EngineDump> dumps, CancellationToken cancellationToken)
    {
        var databases = JsonSerializer.Deserialize<List<EngineDatabase>>(context.NewState[NativeEngineStateKeys.Databases])
            ?? throw new InvalidOperationException("The journal holds no database list to restore.");
        var pgRestore = Path.Combine(NewBinDirectory(context), NativeEngineLayout.PgRestoreExecutableName);

        await using (var connection = await ConnectMaintenanceAsync(cancellationToken))
        {
            foreach (var database in databases)
            {
                await using var command = new NpgsqlCommand(NativeEngineCommands.BuildCreateDatabaseSql(database), connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (var dump in dumps)
        {
            await _processRunner.RunAsync(
                pgRestore,
                NativeEngineCommands.BuildPgRestoreArguments(dump.Database, dump.Reference),
                environment: LibpqEnvironment(),
                cancellationToken: cancellationToken);
        }
    }

    public Task CommitConfigurationAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        var pointer = new PostgresEnginePointer(
            PostgresEnginePointer.CurrentSchemaVersion,
            context.ToMajor,
            _layout.EngineDirectory(context.ToMajor),
            _layout.DataDirectory(context.ToMajor));
        PostgresEnginePointerFile.Write(_layout.PointerPath, pointer);
        PostgresEnginePointerFile.WritePostgresBinDirectory(_layout.NativeConfigPath, pointer.BinDirectory);
        File.Delete(NativeEngineLayout.CreatedByMarker(pointer.DataDirectory));
        return Task.CompletedTask;
    }

    public async Task RevertToOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        EnsureTrustedOldState(context.OldState);
        var oldImagePath = context.OldState[NativeEngineStateKeys.ImagePath];
        var currentImagePath = ReadImagePath(_options.NativePostgresServiceName);
        var running = currentImagePath is null ? null : NativeEngineCommands.ParseServiceImagePath(currentImagePath);
        if (running is not null)
        {
            await StopPostgresServiceAsync(Path.GetDirectoryName(running.Value.PgCtlPath) ?? string.Empty, running.Value.DataDirectory, cancellationToken);
        }

        if (!string.Equals(currentImagePath, oldImagePath, StringComparison.Ordinal))
        {
            await SetImagePathAsync(oldImagePath, cancellationToken);
        }

        var oldPointer = context.OldState[NativeEngineStateKeys.Pointer];
        if (oldPointer == NativeEngineStateKeys.Missing)
        {
            PostgresEnginePointerFile.Delete(_layout.PointerPath);
        }
        else
        {
            PostgresEnginePointerFile.WriteRaw(_layout.PointerPath, oldPointer);
        }

        var oldBinDirectory = context.OldState[NativeEngineStateKeys.ConfiguredBinDirectory];
        if (!string.Equals(PostgresEnginePointerFile.ReadPostgresBinDirectory(_layout.NativeConfigPath) ?? string.Empty, oldBinDirectory, StringComparison.Ordinal))
        {
            PostgresEnginePointerFile.WritePostgresBinDirectory(_layout.NativeConfigPath, oldBinDirectory);
        }

        DeleteNewDataDirectoryIfOwned(context);
        StartPostgresService();
    }

    private void EnsureTrustedOldState(IDictionary<string, string> oldState)
    {
        var parsed = NativeEngineCommands.ParseServiceImagePath(oldState[NativeEngineStateKeys.ImagePath]);
        var configuredBinDirectory = oldState[NativeEngineStateKeys.ConfiguredBinDirectory];
        var pointer = oldState[NativeEngineStateKeys.Pointer];
        var trusted = parsed is not null
            && _layout.IsPinnedPgCtl(parsed.Value.PgCtlPath)
            && _layout.IsPinnedDataDirectory(parsed.Value.DataDirectory)
            && (configuredBinDirectory.Length == 0 || _layout.IsPinnedBinDirectory(configuredBinDirectory))
            && (pointer == NativeEngineStateKeys.Missing || IsPinnedPointer(pointer));
        if (!trusted)
        {
            throw new InvalidOperationException("The journaled old engine state points outside the installation; it is not applied.");
        }
    }

    private bool IsPinnedPointer(string content)
    {
        try
        {
            var pointer = JsonSerializer.Deserialize<PostgresEnginePointer>(content, PointerSerializerOptions);
            return pointer is not null
                && pointer.SchemaVersion == PostgresEnginePointer.CurrentSchemaVersion
                && pointer.Major > 0
                && pointer.PostgresDirectory is not null
                && pointer.DataDirectory is not null
                && NativeEngineCommands.IsSamePath(pointer.PostgresDirectory, _layout.EngineDirectory(pointer.Major))
                && NativeEngineCommands.IsSamePath(pointer.DataDirectory, _layout.DataDirectory(pointer.Major));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not delete the staging directory {Path}.", path);
        }
    }

    private static EngineInspection Unsupported(string reason) =>
        new(0, new Dictionary<string, string>(), 0, reason);

    private string NewBinDirectory(EngineUpgradeContext context) =>
        context.NewState.TryGetValue(NativeEngineStateKeys.BinDirectory, out var binDirectory)
            ? binDirectory
            : NativeEngineLayout.BinDirectory(_layout.EngineDirectory(context.ToMajor));

    private async Task InstallEngineAsync(EngineUpgradeContext context, string engineDirectory, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(context.ArtifactRef, UriKind.Absolute, out var url) || url.Scheme != HttpsScheme)
        {
            throw new InvalidOperationException("The engine bundle must be an https URL.");
        }

        var staging = Path.Combine(_layout.StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var archive = Path.Combine(staging, ArchiveFileName);
            var client = _httpClientFactory.CreateClient(NativeBundleApplier.HttpClientName);
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(archive);
                await source.CopyToAsync(target, cancellationToken);
            }

            var actualSha256 = await NativeBundleApplier.ComputeSha256Async(archive, cancellationToken);
            if (!string.Equals(actualSha256, context.ArtifactSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Engine bundle hash mismatch: expected {context.ArtifactSha256}, got {actualSha256}.");
            }

            var extracted = Path.Combine(staging, ExtractDirectoryName);
            ZipFile.ExtractToDirectory(archive, extracted);
            var root = FindEngineRoot(extracted);
            VerifyEngine(root, context.ToMajor);
            await File.WriteAllTextAsync(Path.Combine(root, EngineHashFileName), actualSha256, CancellationToken.None);
            Directory.Move(root, engineDirectory);
            _logger.LogInformation("Installed PostgreSQL {Major} into {Directory}.", context.ToMajor, engineDirectory);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private static string FindEngineRoot(string extracted)
    {
        if (File.Exists(Path.Combine(NativeEngineLayout.BinDirectory(extracted), NativeEngineLayout.PostgresExecutableName)))
        {
            return extracted;
        }

        var children = Directory.GetDirectories(extracted);
        if (children.Length == 1 && File.Exists(Path.Combine(NativeEngineLayout.BinDirectory(children[0]), NativeEngineLayout.PostgresExecutableName)))
        {
            return children[0];
        }

        throw new InvalidOperationException("The engine bundle contains no bin\\postgres.exe.");
    }

    private static void VerifyEngine(string engineDirectory, int major)
    {
        var binDirectory = NativeEngineLayout.BinDirectory(engineDirectory);
        var required = new[]
        {
            NativeEngineLayout.PostgresExecutableName,
            NativeEngineLayout.PgCtlExecutableName,
            NativeEngineLayout.InitDbExecutableName,
            NativeEngineLayout.PgDumpExecutableName,
            NativeEngineLayout.PgRestoreExecutableName,
        };
        foreach (var executable in required)
        {
            if (!File.Exists(Path.Combine(binDirectory, executable)))
            {
                throw new InvalidOperationException($"The engine in {engineDirectory} has no bin\\{executable}.");
            }
        }

        if (!File.Exists(Path.Combine(engineDirectory, NativeEngineLayout.LibDirectoryName, NativeEngineLayout.VectorLibraryName)))
        {
            throw new InvalidOperationException($"The engine in {engineDirectory} has no lib\\{NativeEngineLayout.VectorLibraryName} (pgvector).");
        }

        var version = FileVersionInfo.GetVersionInfo(Path.Combine(binDirectory, NativeEngineLayout.PostgresExecutableName));
        var engineMajor = version.FileMajorPart != 0 ? version.FileMajorPart : version.ProductMajorPart;
        if (engineMajor != major)
        {
            throw new InvalidOperationException($"The engine in {engineDirectory} is PostgreSQL {engineMajor}, expected {major}.");
        }
    }

    private void PrepareNewDataDirectory(EngineUpgradeContext context, string dataDirectory)
    {
        if (string.Equals(Path.GetFullPath(dataDirectory), Path.GetFullPath(context.OldState[NativeEngineStateKeys.DataDirectory]), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The new data directory {dataDirectory} is the active one.");
        }

        if (Directory.Exists(dataDirectory))
        {
            if (!IsOwnedBy(dataDirectory, context.OperationId))
            {
                throw new InvalidOperationException($"{dataDirectory} already exists and was not created by this upgrade; it is left untouched.");
            }

            Directory.Delete(dataDirectory, recursive: true);
        }

        File.WriteAllText(NativeEngineLayout.CreatedByMarker(dataDirectory), context.OperationId.ToString("D"));
        var directory = Directory.CreateDirectory(dataDirectory);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(Rule(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl));
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl));
        security.AddAccessRule(Rule(WellKnownSidType.NetworkServiceSid, FileSystemRights.FullControl));
        directory.SetAccessControl(security);
    }

    private static FileSystemAccessRule Rule(WellKnownSidType sidType, FileSystemRights rights) =>
        new(
            new SecurityIdentifier(sidType, null),
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);

    private static bool IsOwnedBy(string dataDirectory, Guid operationId)
    {
        var marker = NativeEngineLayout.CreatedByMarker(dataDirectory);
        return File.Exists(marker) && Guid.TryParse(File.ReadAllText(marker).Trim(), out var owner) && owner == operationId;
    }

    private void DeleteNewDataDirectoryIfOwned(EngineUpgradeContext context)
    {
        var dataDirectory = _layout.DataDirectory(context.ToMajor);
        var oldDataDirectory = context.OldState[NativeEngineStateKeys.DataDirectory];
        if (string.Equals(Path.GetFullPath(dataDirectory), Path.GetFullPath(oldDataDirectory), StringComparison.OrdinalIgnoreCase)
            || !IsOwnedBy(dataDirectory, context.OperationId))
        {
            return;
        }

        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
            _logger.LogInformation("Deleted the failed cluster {Directory}.", dataDirectory);
        }

        File.Delete(NativeEngineLayout.CreatedByMarker(dataDirectory));
    }

    private async Task InitializeClusterAsync(string binDirectory, string dataDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(EngineUpgradeDirectory);
        foreach (var stalePasswordFile in Directory.GetFiles(EngineUpgradeDirectory, "*" + PasswordFileSuffix))
        {
            File.Delete(stalePasswordFile);
        }

        var passwordFile = Path.Combine(EngineUpgradeDirectory, Guid.NewGuid().ToString("N") + PasswordFileSuffix);
        try
        {
            await File.WriteAllTextAsync(passwordFile, _connection.Password ?? string.Empty, CancellationToken.None);
            await _processRunner.RunAsync(
                Path.Combine(binDirectory, NativeEngineLayout.InitDbExecutableName),
                NativeEngineCommands.BuildInitDbArguments(dataDirectory, _connection.Username ?? string.Empty, passwordFile),
                cancellationToken: cancellationToken);
        }
        finally
        {
            File.Delete(passwordFile);
        }
    }

    private async Task RemoveUpdaterDependencyAsync(CancellationToken cancellationToken)
    {
        if (!WindowsServices.Exists(UpdaterConstants.NativeUpdaterServiceName))
        {
            return;
        }

        using var updater = new ServiceController(UpdaterConstants.NativeUpdaterServiceName);
        var dependencies = updater.ServicesDependedOn.Select(service => service.ServiceName).ToList();
        var remaining = NativeEngineCommands.DependenciesWithout(dependencies, _options.NativePostgresServiceName);
        if (remaining.Count == dependencies.Count)
        {
            return;
        }

        await _processRunner.RunAsync(
            SttServiceRegistration.ServiceControlExecutable,
            NativeEngineCommands.BuildSetDependenciesArguments(UpdaterConstants.NativeUpdaterServiceName, remaining),
            cancellationToken: cancellationToken);
        _logger.LogInformation("{Updater} no longer depends on {Postgres}.", UpdaterConstants.NativeUpdaterServiceName, _options.NativePostgresServiceName);
    }

    private async Task StopPostgresServiceAsync(string binDirectory, string dataDirectory, CancellationToken cancellationToken)
    {
        using var service = new ServiceController(_options.NativePostgresServiceName);
        if (service.Status is ServiceControllerStatus.Stopped)
        {
            return;
        }

        try
        {
            if (service.Status is not ServiceControllerStatus.StopPending)
            {
                service.Stop(stopDependentServices: false);
            }
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: DependentServicesRunningError })
        {
            _logger.LogWarning("SCM refused to stop {Service} while dependents run; stopping the cluster with pg_ctl.", _options.NativePostgresServiceName);
            await _processRunner.RunAsync(
                Path.Combine(binDirectory, NativeEngineLayout.PgCtlExecutableName),
                NativeEngineCommands.BuildPgCtlStopArguments(dataDirectory),
                cancellationToken: cancellationToken);
        }

        service.WaitForStatus(ServiceControllerStatus.Stopped, UpdaterConstants.ServiceStateTimeout);
    }

    private void StartPostgresService()
    {
        using var service = new ServiceController(_options.NativePostgresServiceName);
        if (service.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
        {
            service.Start();
        }

        service.WaitForStatus(ServiceControllerStatus.Running, UpdaterConstants.ServiceStateTimeout);
    }

    private async Task SetImagePathAsync(string imagePath, CancellationToken cancellationToken)
    {
        await _processRunner.RunAsync(
            SttServiceRegistration.ServiceControlExecutable,
            NativeEngineCommands.BuildSetBinPathArguments(_options.NativePostgresServiceName, imagePath),
            cancellationToken: cancellationToken);

        var written = ReadImagePath(_options.NativePostgresServiceName);
        if (!string.Equals(written, imagePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The service path of {_options.NativePostgresServiceName} reads back as '{written}', expected '{imagePath}'.");
        }
    }

    private static string? ReadImagePath(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(SttServiceRegistration.ServiceRegistryPath(serviceName));
        return key?.GetValue(ImagePathValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private async Task<NpgsqlConnection> ConnectMaintenanceAsync(CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(_options.ConnectionString)
        {
            Database = NpgsqlEngineSnapshotReader.MaintenanceDatabase,
            Pooling = false,
        };
        var deadline = DateTime.UtcNow.Add(ConnectTimeout);
        while (true)
        {
            var connection = new NpgsqlConnection(builder.ConnectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (NpgsqlException) when (DateTime.UtcNow < deadline)
            {
                await connection.DisposeAsync();
                await Task.Delay(ConnectRetryInterval, cancellationToken);
            }
        }
    }

    private Dictionary<string, string> LibpqEnvironment() => new()
    {
        [LibpqHostVariable] = _connection.Host ?? string.Empty,
        [LibpqPortVariable] = _connection.Port.ToString(CultureInfo.InvariantCulture),
        [LibpqUserVariable] = _connection.Username ?? string.Empty,
        [LibpqPasswordVariable] = _connection.Password ?? string.Empty,
    };

    private static long FreeBytes(string path) => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? path).AvailableFreeSpace;
}
