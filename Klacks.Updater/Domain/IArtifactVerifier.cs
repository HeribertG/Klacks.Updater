// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Verifies that a claimed update operation's artifact was signed by the trusted release key before
/// it is applied to a customer machine.
/// </summary>
public interface IArtifactVerifier
{
    bool Verify(UpdateOperation operation);
}
