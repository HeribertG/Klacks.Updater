// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Simulated docker CLI for the Docker engine upgrade tests, on top of RecordingProcessRunner. `compose config` resolves
/// the postgres image and PGDATA from the process environment, then .env, then the compose defaults (like compose does);
/// `compose up ... postgres` makes the running container use the resolved data directory. Commands starting with
/// FailOn throw like a failing process.
/// </summary>
namespace Klacks.Updater.Tests;

using System.Text.Json;
using Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

public sealed class FakeDockerEngine
{
    public const string DefaultImage = "ghcr.io/heribertg/klacks-postgres:19";
    public const string DefaultSubdir = "pgdata";
    public const string ImageVar = "KLACKS_POSTGRES_IMAGE";
    public const string SubdirVar = "KLACKS_POSTGRES_DATA_SUBDIR";

    private readonly string _envPath;

    public FakeDockerEngine(string envPath)
    {
        _envPath = envPath;
        Runner = new RecordingProcessRunner(Handle);
        RunningDataDirectory = DockerEnginePaths.DataDirectory(DefaultSubdir);
    }

    public RecordingProcessRunner Runner { get; }

    public bool Parametrised { get; set; } = true;

    public string ContainerName { get; set; } = "klacks-postgres";

    public string RunningDataDirectory { get; set; }

    public string OldMajor { get; set; } = "17\n";

    public string NewServerVersionNum { get; set; } = "190000";

    public string VolumeEntries { get; set; } = "pgdata\nengine-upgrade\n";

    public string? FailOn { get; set; }

    public int ReadinessFailures { get; set; }

    public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

    private string Handle(string call)
    {
        if (FailOn is not null && call.StartsWith(FailOn, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'docker {call}' exited 1: simulated failure");
        }

        if (call.StartsWith("exec klacks-postgres pg_isready ", StringComparison.Ordinal) && ReadinessFailures > 0)
        {
            ReadinessFailures--;
            throw new InvalidOperationException($"'docker {call}' exited 2: no response");
        }

        if (call == "compose config --format json postgres")
        {
            var (image, subdir) = Resolve(Runner.Environments[^1]);
            return JsonSerializer.Serialize(new
            {
                name = "apps",
                services = new Dictionary<string, object>
                {
                    ["postgres"] = new Dictionary<string, object>
                    {
                        ["container_name"] = ContainerName,
                        ["image"] = image,
                        ["environment"] = new Dictionary<string, string>
                        {
                            ["POSTGRES_PASSWORD"] = "secret-from-env",
                            ["PGDATA"] = DockerEnginePaths.DataDirectory(subdir),
                        },
                    },
                },
            });
        }

        if (call.StartsWith("compose up -d --wait --wait-timeout 600 postgres", StringComparison.Ordinal))
        {
            RunningDataDirectory = DockerEnginePaths.DataDirectory(Resolve(new Dictionary<string, string>()).Subdir);
            return string.Empty;
        }

        if (call.StartsWith("exec klacks-postgres touch ", StringComparison.Ordinal))
        {
            Files.Add(call["exec klacks-postgres touch ".Length..]);
            return string.Empty;
        }

        if (call.StartsWith("exec klacks-postgres find ", StringComparison.Ordinal))
        {
            var path = call[(call.IndexOf(" -path ", StringComparison.Ordinal) + " -path ".Length)..];
            return Files.Contains(path) ? path + "\n" : string.Empty;
        }

        if (call.StartsWith("exec klacks-postgres rm -rf -- ", StringComparison.Ordinal))
        {
            foreach (var removed in call["exec klacks-postgres rm -rf -- ".Length..].Split(' '))
            {
                Files.RemoveWhere(file => file.StartsWith(removed + "/", StringComparison.Ordinal));
            }

            return string.Empty;
        }

        if (call == "exec klacks-postgres printenv PGDATA")
        {
            return RunningDataDirectory + "\n";
        }

        if (call.StartsWith("exec klacks-postgres cat ", StringComparison.Ordinal))
        {
            return OldMajor;
        }

        if (call.StartsWith("exec klacks-postgres df ", StringComparison.Ordinal))
        {
            return "Filesystem     1024-blocks      Used Available Capacity Mounted on\n/dev/sda1        100000000  20000000  50000000      29% /var/lib/postgresql/data\n";
        }

        if (call.StartsWith("exec klacks-postgres ls -A ", StringComparison.Ordinal))
        {
            return VolumeEntries;
        }

        if (call.Contains(DockerEngineSql.IdentityQuery, StringComparison.Ordinal))
        {
            return NewServerVersionNum + "|" + RunningDataDirectory + "\n";
        }

        return string.Empty;
    }

    private (string Image, string Subdir) Resolve(IReadOnlyDictionary<string, string> environment)
    {
        if (!Parametrised)
        {
            return (DefaultImage, DefaultSubdir);
        }

        var lines = File.Exists(_envPath) ? File.ReadAllLines(_envPath) : [];
        return (Value(environment, lines, ImageVar) ?? DefaultImage, Value(environment, lines, SubdirVar) ?? DefaultSubdir);
    }

    private static string? Value(IReadOnlyDictionary<string, string> environment, string[] lines, string key)
    {
        if (environment.TryGetValue(key, out var fromEnvironment) && fromEnvironment.Length > 0)
        {
            return fromEnvironment;
        }

        var raw = DockerEngineEnvFile.ReadRaw(lines, key);
        return raw == DockerEngineStateKeys.Missing || raw.Length == 0 ? null : raw;
    }
}
