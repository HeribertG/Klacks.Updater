// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Thrown by the STT model installer when the service answers the model install call with 404, i.e. the running
/// Klacks.Stt does not know the requested model id (an older service version).
/// </summary>
public class SttModelNotFoundException : InvalidOperationException
{
    public SttModelNotFoundException(string message)
        : base(message)
    {
    }
}
