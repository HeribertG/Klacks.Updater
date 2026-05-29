// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Tests;

using System.Security.Cryptography;
using System.Text;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class RsaArtifactVerifierTests
{
    private static UpdateOperation Op(string signature) => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.Update,
        TargetVersion = "1.2.0",
        ArtifactRef = "ghcr.io/x/api:1.2.0",
        ArtifactSha256 = "abc123",
        ArtifactSignature = signature,
    };

    private static string Sign(RSA rsa, UpdateOperation op)
    {
        var payload = Encoding.UTF8.GetBytes(ArtifactSignaturePayload.Build(op));
        return Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static RsaArtifactVerifier Verifier(string publicKeyPem)
    {
        var options = Options.Create(new UpdaterOptions { SignaturePublicKey = publicKeyPem });
        return new RsaArtifactVerifier(options, NullLogger<RsaArtifactVerifier>.Instance);
    }

    [Test]
    public void Valid_signature_passes()
    {
        using var rsa = RSA.Create(2048);
        var op = Op(string.Empty);
        var signed = op with { ArtifactSignature = Sign(rsa, op) };

        Verifier(rsa.ExportSubjectPublicKeyInfoPem()).Verify(signed).ShouldBeTrue();
    }

    [Test]
    public void Tampered_payload_fails()
    {
        using var rsa = RSA.Create(2048);
        var op = Op(string.Empty);
        var signature = Sign(rsa, op);
        var tampered = op with { TargetVersion = "9.9.9", ArtifactSignature = signature };

        Verifier(rsa.ExportSubjectPublicKeyInfoPem()).Verify(tampered).ShouldBeFalse();
    }

    [Test]
    public void No_public_key_runs_in_unsigned_mode()
    {
        Verifier(string.Empty).Verify(Op("anything")).ShouldBeTrue();
    }

    [Test]
    public void Missing_signature_with_configured_key_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        Verifier(rsa.ExportSubjectPublicKeyInfoPem()).Verify(Op(string.Empty)).ShouldBeFalse();
    }
}
