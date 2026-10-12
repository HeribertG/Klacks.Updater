// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Net;
using System.Text;
using Klacks.Updater.Application;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class HttpHealthGateTests
{
    private const string HealthUrl = "http://api.invalid/internal/health/deep";
    private const string VersionUrl = "http://api.invalid/api/Version";

    private static HttpHealthGate CreateGate(string runningVersionJson)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HttpHealthGate.HttpClientName)
            .Returns(_ => new HttpClient(new StubHandler(runningVersionJson)));
        var options = Options.Create(new UpdaterOptions { HealthUrl = HealthUrl, VersionUrl = VersionUrl });
        return new HttpHealthGate(factory, options, NullLogger<HttpHealthGate>.Instance);
    }

    [TestCase("1.2.3", 1, 2, 3)]
    [TestCase("1.2.3-rc1", 1, 2, 3)]
    public void ParseNumericVersion_ignores_the_prerelease_suffix(string version, int major, int minor, int patch)
    {
        HttpHealthGate.ParseNumericVersion(version).ShouldBe((major, minor, patch));
    }

    [TestCase("")]
    [TestCase("latest")]
    [TestCase("1.2")]
    public void ParseNumericVersion_rejects_non_versions(string version)
    {
        HttpHealthGate.ParseNumericVersion(version).ShouldBeNull();
    }

    [Test]
    public async Task Healthy_api_with_the_expected_version_passes()
    {
        var gate = CreateGate("""{"variant":"RELEASE","major":1,"minor":2,"patch":3}""");

        (await gate.WaitForHealthyAsync("1.2.3")).ShouldBeTrue();
    }

    [Test]
    public async Task Unparsable_expected_version_skips_the_version_check()
    {
        var gate = CreateGate("""{"major":9,"minor":9,"patch":9}""");

        (await gate.WaitForHealthyAsync("latest")).ShouldBeTrue();
    }

    [Test]
    public async Task Healthy_api_with_another_version_does_not_pass_before_cancellation()
    {
        var gate = CreateGate("""{"major":1,"minor":2,"patch":2}""");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        await Should.ThrowAsync<OperationCanceledException>(() => gate.WaitForHealthyAsync("1.2.3", cancellation.Token));
    }

    private sealed class StubHandler(string versionJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = request.RequestUri!.AbsoluteUri == VersionUrl
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(versionJson, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK);
            return Task.FromResult(response);
        }
    }
}
