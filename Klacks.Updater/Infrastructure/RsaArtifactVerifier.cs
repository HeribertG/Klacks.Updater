// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Verifies the RSA-SHA256 signature carried on the update row against the configured release public
/// key (PEM, deployment trust config). If no public key is configured the updater runs in unsigned
/// mode (allowed, but logged as a warning); once a key is set, a missing or invalid signature is
/// rejected so an unsigned or tampered artifact is never applied. A PostgreSQL engine upgrade always requires a
/// configured key and a valid signature over its domain-separated payload; only Updater:AllowUnsignedEngineUpgrade
/// (tests) lifts that. Signed fields are format-checked first (SignedFieldFormat), so no field can forge the
/// newline-separated payload of another operation type.
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
        if (!SignedFieldFormat.IsValid(operation))
        {
            _logger.LogError("Operation {Id} rejected: a signed field has an invalid format.", operation.Id);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.SignaturePublicKey) && operation.OperationType == UpdateOperationType.PostgresUpgrade)
        {
            if (_options.AllowUnsignedEngineUpgrade)
            {
                _logger.LogWarning("No signature public key configured — unsigned engine upgrade {Id} allowed by Updater:AllowUnsignedEngineUpgrade (tests only).", operation.Id);
                return true;
            }

            _logger.LogError("Engine upgrade {Id} rejected: engine upgrades always require a signature public key.", operation.Id);
            return false;
        }

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
