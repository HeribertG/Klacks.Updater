// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Docker on-prem/server engine upgrade target: moves the postgres service to a new major by dump and restore inside the
/// same volume. The old cluster stays untouched in its subdirectory (pgdata or pg&lt;old&gt;) and the new one is created in
/// pg&lt;new&gt; by the image entrypoint, switched by the two compose variables of the image and data subdirectory in .env.
/// Inspection proves both variables are effective (a probe run of `docker compose config` with overriding values),
/// records the raw .env values ("missing" when absent) and the effective image and subdirectory, and measures the free
/// space of the volume. Dumps are custom-format files written by pg_dump inside the old container into a per-operation
/// folder of the exchange directory (never via stdout). The old cluster is stopped with `docker stop` and a grace period
/// instead of `docker compose stop` so no dependent service (including this updater) is stopped with it. The image is
/// pulled and pinned by digest and must come from the configured repository. Before the restore drops anything it waits
/// until the new server accepts TCP connections (the entrypoint's init server only listens on the socket) and proves
/// that psql reaches major ToMajor in pg&lt;new&gt;. Revert works from FromMajor, ToMajor and OldState alone: it restores
/// both .env values (removing keys that were missing), starts postgres with the old settings, checks that the old data
/// directory with PG_VERSION == FromMajor runs and only then deletes the failed pg&lt;new&gt; folder (only when the ownership
/// marker of this operation proves it created that folder; a foreign pg&lt;new&gt; is never touched) and the dumps of the
/// operation, so a later installer run is not blocked. Database names reach pg_dump/pg_restore only as quoted conninfo,
/// and journaled .env values are restored only when they are plain values. Every step is idempotent. No password is passed anywhere: all
/// client calls use the local socket of the container; the compose config output (it contains the resolved database
/// password) is parsed in memory and never logged.
/// </summary>
/// <param name="options">Compose project directory, service and container names, postgres user, trusted image repository and variable names</param>
/// <param name="runner">Runs the docker CLI with argument lists</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Text.Json;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

public class DockerEngineUpgradeTarget : IEngineUpgradeTarget
{
    public const string ProbeImage = "klacks-engine-upgrade-probe/postgres:probe";
    public const string ProbeSubdir = "klacks-engine-upgrade-probe";
    public const string StopGraceSeconds = "120";
    public const string WaitTimeoutSeconds = "600";

    private const string Docker = "docker";
    private const string Compose = "compose";
    private const string EnvFileName = ".env";
    private const string MaintenanceDatabase = "postgres";
    private const string PrivateDirectoryMode = "0700";
    private const string DataDirectoryVariable = "PGDATA";
    private const string Loopback = "127.0.0.1";
    private const string ComposeConfigFailedMessage = "'docker compose config' failed; its output is withheld because it contains secrets.";
    private const int ReadinessAttempts = 30;
    private static readonly TimeSpan ReadinessDelay = TimeSpan.FromSeconds(2);

    private readonly UpdaterOptions _options;
    private readonly IProcessRunner _runner;
    private readonly ILogger<DockerEngineUpgradeTarget> _logger;

    public DockerEngineUpgradeTarget(IOptions<UpdaterOptions> options, IProcessRunner runner, ILogger<DockerEngineUpgradeTarget> logger)
    {
        _options = options.Value;
        _runner = runner;
        _logger = logger;
    }

    private string Container => _options.PostgresContainer;

    private string EnvPath => Path.Combine(_options.ComposeProjectDir, EnvFileName);

    public async Task<EngineInspection> InspectAsync(CancellationToken cancellationToken)
    {
        var service = ComposeConfigReader.ReadService(await ComposeConfigAsync(null, cancellationToken), _options.PostgresServiceName);
        if (service is null)
        {
            return Unsupported(ComposeServices.MissingServiceMessage(_options.PostgresServiceName));
        }

        if (!string.Equals(service.ContainerName, Container, StringComparison.Ordinal))
        {
            return Unsupported($"The compose service '{_options.PostgresServiceName}' does not run as container '{Container}'.");
        }

        var subdir = DockerEnginePaths.SubdirOf(service.DataDirectory);
        if (subdir is null || service.Image is null)
        {
            return Unsupported($"PGDATA of the compose service '{_options.PostgresServiceName}' is not a subdirectory of {DockerEnginePaths.DataRoot}.");
        }

        var probeEnvironment = new Dictionary<string, string>
        {
            [_options.PostgresImageEnvVar] = ProbeImage,
            [_options.PostgresDataSubdirEnvVar] = ProbeSubdir,
        };
        var probe = ComposeConfigReader.ReadService(await ComposeConfigAsync(probeEnvironment, cancellationToken), _options.PostgresServiceName);
        if (probe is null
            || !string.Equals(probe.Image, ProbeImage, StringComparison.Ordinal)
            || !string.Equals(probe.DataDirectory, DockerEnginePaths.DataDirectory(ProbeSubdir), StringComparison.Ordinal))
        {
            return Unsupported(
                $"The compose file does not take the postgres image and data directory from {_options.PostgresImageEnvVar} and {_options.PostgresDataSubdirEnvVar}. Download the current bundle and retry.");
        }

        var lines = await ReadEnvAsync(cancellationToken);
        if (DockerEngineEnvFile.CountOccurrences(lines, _options.PostgresImageEnvVar) > 1
            || DockerEngineEnvFile.CountOccurrences(lines, _options.PostgresDataSubdirEnvVar) > 1)
        {
            return Unsupported($"The .env file sets {_options.PostgresImageEnvVar} or {_options.PostgresDataSubdirEnvVar} more than once.");
        }

        var envImage = DockerEngineEnvFile.ReadRaw(lines, _options.PostgresImageEnvVar);
        var envDataSubdir = DockerEngineEnvFile.ReadRaw(lines, _options.PostgresDataSubdirEnvVar);
        if (!IsRecordableEnvValue(lines, _options.PostgresImageEnvVar, envImage)
            || !IsRecordableEnvValue(lines, _options.PostgresDataSubdirEnvVar, envDataSubdir))
        {
            return Unsupported($"The .env values of {_options.PostgresImageEnvVar} or {_options.PostgresDataSubdirEnvVar} cannot be restored safely; set plain values.");
        }

        var runningDataDirectory = (await ExecAsync(["printenv", DataDirectoryVariable], cancellationToken)).Trim();
        if (!string.Equals(runningDataDirectory, service.DataDirectory, StringComparison.Ordinal))
        {
            return Unsupported($"The running container '{Container}' does not use the configured data directory. Run 'docker compose up -d' first.");
        }

        var major = DockerEngineCommandOutput.ParseMajor(
            await ExecAsync(["cat", DockerEnginePaths.VersionFile(subdir)], cancellationToken));
        var freeBytes = DockerEngineCommandOutput.ParseAvailableBytes(await ExecAsync(["df", "-Pk", DockerEnginePaths.DataRoot], cancellationToken));

        var oldState = new Dictionary<string, string>
        {
            [DockerEngineStateKeys.EnvImage] = envImage,
            [DockerEngineStateKeys.EnvDataSubdir] = envDataSubdir,
            [DockerEngineStateKeys.Image] = service.Image,
            [DockerEngineStateKeys.DataSubdir] = subdir,
        };

        return new EngineInspection(major, oldState, freeBytes, null);
    }

    public async Task PrepareArtifactAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        var repository = PostgresImageReference.Repository(context.ArtifactRef);
        if (!string.Equals(repository, _options.PostgresImageRepository, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The engine image repository '{repository}' is not the trusted repository '{_options.PostgresImageRepository}'.");
        }

        if (!PostgresImageReference.IsDigest(context.ArtifactSha256))
        {
            throw new InvalidOperationException("The engine image digest is not of the form sha256:<64 hex>.");
        }

        var image = PostgresImageReference.Pin(repository, context.ArtifactSha256);
        _logger.LogInformation("Engine upgrade {Id}: pulling {Image}.", context.OperationId, image);
        await _runner.RunAsync(Docker, ["pull", image], cancellationToken: cancellationToken);
        context.NewState[DockerEngineStateKeys.Image] = image;
    }

    public Task StopApplicationAsync(CancellationToken cancellationToken) =>
        ComposeAsync(["stop", _options.ApiServiceName], cancellationToken);

    public Task StartApplicationAsync(CancellationToken cancellationToken) =>
        ComposeAsync(["start", _options.ApiServiceName], cancellationToken);

    public async Task<IReadOnlyList<EngineDump>> DumpAsync(
        EngineUpgradeContext context, IReadOnlyList<EngineDatabase> databases, CancellationToken cancellationToken)
    {
        var directory = DockerEnginePaths.DumpDirectory(context.OperationId);
        context.NewState[DockerEngineStateKeys.DumpDirectory] = directory;
        context.NewState[DockerEngineStateKeys.Databases] = JsonSerializer.Serialize(databases);
        await ExecAsync(["install", "-d", "-m", PrivateDirectoryMode, UpdaterConstants.DockerEngineUpgradeExchangeDirectory, directory], cancellationToken);
        await ExecAsync(["chmod", PrivateDirectoryMode, UpdaterConstants.DockerEngineUpgradeExchangeDirectory, directory], cancellationToken);

        var dumps = new List<EngineDump>(databases.Count);
        for (var index = 0; index < databases.Count; index++)
        {
            var file = DockerEnginePaths.DumpFile(context.OperationId, index);
            _logger.LogInformation("Engine upgrade {Id}: dumping database {Database} to {File}.", context.OperationId, databases[index].Name, file);
            await ExecAsync(["pg_dump", "-Fc", "-U", _options.PostgresUser, "-d", DockerEngineSql.ConnectionDatabase(databases[index].Name), "-f", file], cancellationToken);
            dumps.Add(new EngineDump(databases[index].Name, file));
        }

        return dumps;
    }

    public async Task StopOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        var targetSubdir = TargetSubdirOf(context);
        await StopApplicationAsync(cancellationToken);

        var entries = DockerEngineCommandOutput.ParseEntries(await ExecAsync(["ls", "-A", DockerEnginePaths.DataRoot], cancellationToken));
        if (entries.Contains(targetSubdir, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"The postgres volume already contains '{targetSubdir}', which this operation did not create (left over from a failed upgrade). Nothing was removed; contact Klacks support (see README.md, PostgreSQL major upgrade).");
        }

        await ExecAsync(["touch", DockerEnginePaths.OwnershipMarker(context.OperationId, targetSubdir)], cancellationToken);

        _logger.LogInformation("Engine upgrade {Id}: stopping the PostgreSQL {Major} container.", context.OperationId, context.FromMajor);
        await _runner.RunAsync(Docker, ["stop", "-t", StopGraceSeconds, Container], cancellationToken: cancellationToken);
    }

    public async Task ActivateNewEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        var targetSubdir = TargetSubdirOf(context);
        if (!context.OldState.ContainsKey(DockerEngineStateKeys.EnvImage) || !context.OldState.ContainsKey(DockerEngineStateKeys.EnvDataSubdir))
        {
            throw new InvalidOperationException("The original .env values were not recorded; refusing to change .env.");
        }

        var image = context.NewState.TryGetValue(DockerEngineStateKeys.Image, out var pinned)
            ? pinned
            : PostgresImageReference.Pin(PostgresImageReference.Repository(context.ArtifactRef), context.ArtifactSha256);
        if (!PostgresImageReference.IsPinnedFor(image, _options.PostgresImageRepository))
        {
            throw new InvalidOperationException($"The engine image '{image}' is not pinned by digest in the trusted repository.");
        }

        context.NewState[DockerEngineStateKeys.Image] = image;
        context.NewState[DockerEngineStateKeys.DataSubdir] = targetSubdir;
        await WriteEnvAsync(image, targetSubdir, cancellationToken);

        _logger.LogInformation("Engine upgrade {Id}: starting PostgreSQL {Major} from {Image} in {Subdir}.", context.OperationId, context.ToMajor, image, targetSubdir);
        await StartPostgresAsync(cancellationToken);
    }

    public async Task RestoreAsync(EngineUpgradeContext context, IReadOnlyList<EngineDump> dumps, CancellationToken cancellationToken)
    {
        var targetDirectory = DockerEnginePaths.DataDirectory(TargetSubdirOf(context));
        await WaitForTcpReadinessAsync(cancellationToken);
        var (major, dataDirectory) = DockerEngineCommandOutput.ParseIdentity(await PsqlAsync(DockerEngineSql.IdentityQuery, cancellationToken));
        if (major != context.ToMajor || !string.Equals(dataDirectory, targetDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"psql reached PostgreSQL {major} in {dataDirectory}, expected {context.ToMajor} in {targetDirectory}; refusing to recreate databases.");
        }

        var databases = context.NewState.TryGetValue(DockerEngineStateKeys.Databases, out var json)
            ? JsonSerializer.Deserialize<List<EngineDatabase>>(json) ?? []
            : [];

        for (var index = 0; index < dumps.Count; index++)
        {
            var dump = dumps[index];
            if (!string.Equals(dump.Reference, DockerEnginePaths.DumpFile(context.OperationId, index), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The dump reference '{dump.Reference}' is not a dump file of this operation.");
            }

            var database = databases.FirstOrDefault(d => string.Equals(d.Name, dump.Database, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No recorded properties for the dumped database '{dump.Database}'.");

            _logger.LogInformation("Engine upgrade {Id}: restoring database {Database}.", context.OperationId, database.Name);
            await PsqlAsync(DockerEngineSql.DropDatabase(database.Name), cancellationToken);
            await PsqlAsync(DockerEngineSql.CreateDatabase(database), cancellationToken);
            await ExecAsync(["pg_restore", "--exit-on-error", "-U", _options.PostgresUser, "-d", DockerEngineSql.ConnectionDatabase(database.Name), dump.Reference], cancellationToken);
        }

        NpgsqlConnection.ClearAllPools();
    }

    public async Task CommitConfigurationAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        var targetSubdir = TargetSubdirOf(context);
        if (!context.NewState.TryGetValue(DockerEngineStateKeys.Image, out var image)
            || !PostgresImageReference.IsPinnedFor(image, _options.PostgresImageRepository))
        {
            throw new InvalidOperationException("The pinned engine image is not recorded; refusing to commit the configuration.");
        }

        await WriteEnvAsync(image, targetSubdir, cancellationToken);
    }

    public async Task RevertToOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        var targetSubdir = TargetSubdirOf(context);
        var targetDirectory = DockerEnginePaths.DataDirectory(targetSubdir);
        foreach (var key in new[] { DockerEngineStateKeys.EnvImage, DockerEngineStateKeys.EnvDataSubdir })
        {
            if (context.OldState.TryGetValue(key, out var value) && !DockerEngineEnvFile.IsRestorableRawValue(value))
            {
                throw new InvalidOperationException($"The journaled value of {key} is not a plain value; refusing to write it into .env.");
            }
        }

        await StopApplicationAsync(cancellationToken);

        var lines = await ReadEnvAsync(cancellationToken);
        var restored = lines;
        if (context.OldState.TryGetValue(DockerEngineStateKeys.EnvImage, out var oldImage))
        {
            restored = DockerEngineEnvFile.Apply(restored, _options.PostgresImageEnvVar, oldImage);
        }

        if (context.OldState.TryGetValue(DockerEngineStateKeys.EnvDataSubdir, out var oldSubdir))
        {
            restored = DockerEngineEnvFile.Apply(restored, _options.PostgresDataSubdirEnvVar, oldSubdir);
        }

        if (!restored.SequenceEqual(lines, StringComparer.Ordinal))
        {
            await AtomicFile.WriteAllLinesAsync(EnvPath, restored, cancellationToken);
            _logger.LogInformation("Engine upgrade {Id}: restored the original postgres settings in .env.", context.OperationId);
        }

        await StartPostgresAsync(cancellationToken);

        var runningDataDirectory = (await ExecAsync(["printenv", DataDirectoryVariable], cancellationToken)).Trim();
        if (string.Equals(runningDataDirectory, targetDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"postgres still runs on {targetDirectory} after the revert; the folder is not deleted.");
        }

        if (context.OldState.TryGetValue(DockerEngineStateKeys.DataSubdir, out var subdir)
            && !string.Equals(runningDataDirectory, DockerEnginePaths.DataDirectory(subdir), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"postgres runs on {runningDataDirectory} after the revert instead of the original {DockerEnginePaths.DataDirectory(subdir)}.");
        }

        var runningSubdir = DockerEnginePaths.SubdirOf(runningDataDirectory)
            ?? throw new InvalidOperationException($"postgres runs on {runningDataDirectory} after the revert, outside {DockerEnginePaths.DataRoot}.");
        var runningMajor = DockerEngineCommandOutput.ParseMajor(await ExecAsync(["cat", DockerEnginePaths.VersionFile(runningSubdir)], cancellationToken));
        if (runningMajor != context.FromMajor)
        {
            throw new InvalidOperationException($"postgres runs PostgreSQL {runningMajor} data after the revert, expected {context.FromMajor}; the folder is not deleted.");
        }

        var dumpDirectory = DockerEnginePaths.DumpDirectory(context.OperationId);
        var marker = DockerEnginePaths.OwnershipMarker(context.OperationId, targetSubdir);
        var found = await ExecAsync(["find", DockerEnginePaths.DataRoot, "-maxdepth", DockerEnginePaths.DepthBelowDataRoot(marker), "-path", marker], cancellationToken);
        if (DockerEngineCommandOutput.ParseEntries(found).Contains(marker, StringComparer.Ordinal))
        {
            _logger.LogInformation("Engine upgrade {Id}: PostgreSQL {Major} is active again; removing {Directory}.", context.OperationId, context.FromMajor, targetDirectory);
            await ExecAsync(["rm", "-rf", "--", targetDirectory], cancellationToken);
        }
        else
        {
            _logger.LogInformation("Engine upgrade {Id}: PostgreSQL {Major} is active again; {Directory} was not created by this operation and is kept.", context.OperationId, context.FromMajor, targetDirectory);
        }

        await ExecAsync(["rm", "-rf", "--", dumpDirectory], cancellationToken);

        NpgsqlConnection.ClearAllPools();
    }

    private static EngineInspection Unsupported(string reason) =>
        new(0, new Dictionary<string, string>(), 0, reason);

    private static bool IsRecordableEnvValue(string[] lines, string key, string rawValue) =>
        DockerEngineEnvFile.IsRestorableRawValue(rawValue)
        && (rawValue != DockerEngineStateKeys.Missing || DockerEngineEnvFile.CountOccurrences(lines, key) == 0);

    private async Task WaitForTcpReadinessAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ExecAsync(["pg_isready", "-h", Loopback, "-U", _options.PostgresUser, "-d", MaintenanceDatabase], cancellationToken);
                return;
            }
            catch (InvalidOperationException) when (attempt < ReadinessAttempts)
            {
                await Task.Delay(ReadinessDelay, cancellationToken);
            }
        }
    }

    private static string TargetSubdirOf(EngineUpgradeContext context)
    {
        if (context.ToMajor <= 0 || context.ToMajor <= context.FromMajor)
        {
            throw new InvalidOperationException($"Invalid engine upgrade from {context.FromMajor} to {context.ToMajor}.");
        }

        var targetSubdir = DockerEnginePaths.TargetSubdir(context.ToMajor);
        if (context.OldState.TryGetValue(DockerEngineStateKeys.DataSubdir, out var oldSubdir)
            && string.Equals(oldSubdir, targetSubdir, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The old cluster already lives in '{targetSubdir}'.");
        }

        return targetSubdir;
    }

    private async Task WriteEnvAsync(string image, string subdir, CancellationToken cancellationToken)
    {
        var lines = await ReadEnvAsync(cancellationToken);
        var updated = DockerEngineEnvFile.Apply(lines, _options.PostgresImageEnvVar, image);
        updated = DockerEngineEnvFile.Apply(updated, _options.PostgresDataSubdirEnvVar, subdir);
        if (!updated.SequenceEqual(lines, StringComparer.Ordinal))
        {
            await AtomicFile.WriteAllLinesAsync(EnvPath, updated, cancellationToken);
        }
    }

    private async Task<string[]> ReadEnvAsync(CancellationToken cancellationToken) =>
        File.Exists(EnvPath) ? await File.ReadAllLinesAsync(EnvPath, cancellationToken) : [];

    private Task StartPostgresAsync(CancellationToken cancellationToken) =>
        ComposeAsync(["up", "-d", "--wait", "--wait-timeout", WaitTimeoutSeconds, _options.PostgresServiceName], cancellationToken);

    private async Task<string> ComposeConfigAsync(IDictionary<string, string>? environment, CancellationToken cancellationToken)
    {
        try
        {
            return await _runner.RunAsync(Docker, [Compose, "config", "--format", "json", _options.PostgresServiceName], _options.ComposeProjectDir, environment, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(ComposeConfigFailedMessage);
        }
    }

    private Task ComposeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _runner.RunAsync(Docker, [Compose, .. arguments], _options.ComposeProjectDir, cancellationToken: cancellationToken);

    private Task<string> ExecAsync(IReadOnlyList<string> command, CancellationToken cancellationToken) =>
        _runner.RunAsync(Docker, ["exec", Container, .. command], cancellationToken: cancellationToken);

    private Task<string> PsqlAsync(string sql, CancellationToken cancellationToken) =>
        ExecAsync(["psql", "-X", "-v", "ON_ERROR_STOP=1", "-U", _options.PostgresUser, "-d", MaintenanceDatabase, "-tA", "-c", sql], cancellationToken);
}
