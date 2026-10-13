// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.IO.Compression;
using System.Net;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class NativeBundleApplierTests
{
    private const string BundleUrl = "https://example.invalid/klacks-win-x64.zip";

    private string _root = string.Empty;
    private byte[] _bundle = [];
    private IServiceController _service = null!;
    private IVersionLinkSwitcher _links = null!;
    private List<string> _calls = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "klacks-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bundle = CreateBundle();
        _calls = [];
        _service = Substitute.For<IServiceController>();
        _service.StopAsync(Arg.Any<CancellationToken>()).Returns(_ => Record("stop"));
        _service.StartAsync(Arg.Any<CancellationToken>()).Returns(_ => Record("start"));
        _links = Substitute.For<IVersionLinkSwitcher>();
        _links.SwitchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Record("switch:" + Path.GetFileName(call.ArgAt<string>(1))));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
    }

    private Task Record(string call)
    {
        _calls.Add(call);
        return Task.CompletedTask;
    }

    private static byte[] CreateBundle()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("app/Klacks.Api.dll").Open());
            writer.Write("binary");
        }

        return memory.ToArray();
    }

    private NativeBundleApplier CreateApplier(int retainedVersions = 3)
    {
        var options = Options.Create(new UpdaterOptions { NativeProgramRoot = _root, NativeRetainedVersions = retainedVersions });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(NativeBundleApplier.HttpClientName).Returns(_ => new HttpClient(new StaticBundleHandler(_bundle)));
        return new NativeBundleApplier(options, factory, _service, _links, NullLogger<NativeBundleApplier>.Instance);
    }

    private async Task<string> BundleSha256Async()
    {
        var path = Path.Combine(_root, "expected.zip");
        await File.WriteAllBytesAsync(path, _bundle);
        var hash = await NativeBundleApplier.ComputeSha256Async(path);
        File.Delete(path);
        return hash;
    }

    private static UpdateOperation Operation(string sha256, string target = "1.2.0") => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.Update,
        FromVersion = "1.1.0",
        TargetVersion = target,
        ArtifactRef = BundleUrl,
        ArtifactSha256 = sha256,
    };

    [Test]
    public async Task Activate_installs_the_bundle_then_stops_switches_and_starts()
    {
        await CreateApplier().ActivateAsync(Operation(await BundleSha256Async()));

        File.Exists(Path.Combine(_root, "versions", "1.2.0", "app", "Klacks.Api.dll")).ShouldBeTrue();
        _calls.ShouldBe(["stop", "switch:1.2.0", "start"]);
        Directory.EnumerateFileSystemEntries(Path.Combine(_root, "staging")).ShouldBeEmpty();
    }

    [Test]
    public async Task Activate_with_hash_mismatch_installs_nothing_and_leaves_the_service_alone()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateApplier().ActivateAsync(Operation(new string('0', 64))));

        Directory.Exists(Path.Combine(_root, "versions", "1.2.0")).ShouldBeFalse();
        _calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Activate_rejects_a_target_version_that_escapes_the_versions_directory()
    {
        var sha256 = await BundleSha256Async();

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateApplier().ActivateAsync(Operation(sha256, "../../evil")));

        _calls.ShouldBeEmpty();
    }

    [Test]
    public async Task ActivateVersion_switches_back_to_an_installed_version()
    {
        Directory.CreateDirectory(Path.Combine(_root, "versions", "1.1.0"));

        await CreateApplier().ActivateVersionAsync("1.1.0");

        _calls.ShouldBe(["stop", "switch:1.1.0", "start"]);
    }

    [Test]
    public async Task ActivateVersion_fails_when_the_version_is_gone()
    {
        await Should.ThrowAsync<DirectoryNotFoundException>(() => CreateApplier().ActivateVersionAsync("1.0.0"));

        _calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Activate_keeps_the_current_and_previous_versions_and_prunes_older_ones()
    {
        var created = DateTime.UtcNow.AddDays(-10);
        foreach (var version in new[] { "1.0.0", "1.0.5", "1.1.0" })
        {
            var directory = Directory.CreateDirectory(Path.Combine(_root, "versions", version));
            directory.CreationTimeUtc = created;
            created = created.AddDays(1);
        }

        await CreateApplier(retainedVersions: 2).ActivateAsync(Operation(await BundleSha256Async()));

        Directory.EnumerateDirectories(Path.Combine(_root, "versions"))
            .Select(directory => Path.GetFileName(directory))
            .OrderBy(name => name)
            .ShouldBe(["1.1.0", "1.2.0"]);
    }

    [Test]
    public async Task Activate_never_prunes_the_rollback_target_or_the_updaters_own_version()
    {
        var created = DateTime.UtcNow.AddDays(-10);
        foreach (var version in new[] { "1.0.0", "1.0.5", "1.1.0" })
        {
            var directory = Directory.CreateDirectory(Path.Combine(_root, "versions", version));
            directory.CreationTimeUtc = created;
            created = created.AddDays(1);
        }

        var rollbackTarget = Path.Combine(_root, "versions", "1.0.0");
        var updaterOwn = Path.Combine(_root, "versions", "1.0.5");
        _links.ReadTarget(Arg.Any<string>()).Returns(updaterOwn);
        var operation = Operation(await BundleSha256Async()) with { FromVersion = "1.0.0" };

        await CreateApplier(retainedVersions: 2).ActivateAsync(operation);

        Directory.Exists(rollbackTarget).ShouldBeTrue();
        Directory.Exists(updaterOwn).ShouldBeTrue();
        Directory.Exists(Path.Combine(_root, "versions", "1.2.0")).ShouldBeTrue();
    }

    private sealed class StaticBundleHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }
}
