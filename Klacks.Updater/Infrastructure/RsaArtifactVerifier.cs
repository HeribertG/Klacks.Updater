// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Verifies the RSA-SHA256 signature carried on the update row against the configured release public
/// key (PEM, deployment trust config). If no public key is configured the updater runs in unsigned
/// mode (allowed, but logged as a warning); once a key is set, a missing or invalid signature is
/// rejected so an unsigned or tampered artifact is never applied.
/// </summary>
/// <param name="options">Deployment trust configuration (signature public key)</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Security.Cryptography;
using System.Text;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class RsaArtifactVerifier : IArtifactVerifier
{
    private readonly UpdaterOptions _options;
    private readonly ILogger<RsaArtifactVerifier> _logger;

    public RsaArtifactVerifier(IOptions<UpdaterOptions> options, ILogger<RsaArtifactVerifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool Verify(UpdateOperation operation)
    {
        if (string.IsNullOrWhiteSpace(_options.SignaturePublicKey))
        {
            _logger.LogWarning("No signature public key configured — running in unsigned mode, artifact verification skipped.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(operation.ArtifactSignature))
        {
            _logger.LogError("Artifact signature missing while a public key is configured — rejecting operation {Id}.", operation.Id);
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(_options.SignaturePublicKey);

            var payload = Encoding.UTF8.GetBytes(ArtifactSignaturePayload.Build(operation));
            var signature = Convert.FromBase64String(operation.ArtifactSignature);

            var valid = rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!valid)
            {
                _logger.LogError("Artifact signature verification failed for operation {Id}.", operation.Id);
            }

            return valid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Artifact signature verification error for operation {Id}.", operation.Id);
            return false;
        }
    }
}
