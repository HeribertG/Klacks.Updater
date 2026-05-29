// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// Verifies that a claimed update operation's artifact was signed by the trusted release key before
/// it is applied to a customer machine.
/// </summary>
public interface IArtifactVerifier
{
    bool Verify(UpdateOperation operation);
}
