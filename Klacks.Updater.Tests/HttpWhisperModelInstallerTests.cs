// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Net;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class HttpWhisperModelInstallerTests
{
    private const string BaseUrl = "http://klacks-stt:8000";
    private const string ModelId = "supertonic-3-int8";

    private HttpWhisperModelInstaller CreateInstaller(HttpStatusCode status, out StubHandler handler)
    {
        handler = new StubHandler(status);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(HttpWhisperModelInstaller.HttpClientName).Returns(new HttpClient(handler));
        return new HttpWhisperModelInstaller(factory, NullLogger<HttpWhisperModelInstaller>.Instance);
    }

    [TestCase(HttpStatusCode.OK)]
    [TestCase(HttpStatusCode.Created)]
    [TestCase(HttpStatusCode.Conflict)]
    public async Task Install_model_accepts_success_and_conflict(HttpStatusCode status)
    {
        var installer = CreateInstaller(status, out var handler);

        await installer.InstallModelAsync(BaseUrl, ModelId);

        handler.Requests.ShouldBe([$"POST {BaseUrl}/v1/models/{ModelId}"]);
    }

    [Test]
    public async Task Install_model_404_throws_the_dedicated_not_found_exception()
    {
        var installer = CreateInstaller(HttpStatusCode.NotFound, out _);

        var exception = await Should.ThrowAsync<SttModelNotFoundException>(() => installer.InstallModelAsync(BaseUrl, ModelId));

        exception.Message.ShouldContain("404");
    }

    [TestCase(HttpStatusCode.BadGateway)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task Install_model_other_errors_throw_a_plain_invalid_operation(HttpStatusCode status)
    {
        var installer = CreateInstaller(status, out _);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => installer.InstallModelAsync(BaseUrl, ModelId));

        exception.ShouldNotBeOfType<SttModelNotFoundException>();
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("body") });
        }
    }
}
