// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// The speech-service image tag pinned by an activated API image: the new tag, the tag currently recorded in the compose
/// .env (null when absent, so the previous version is unknown), and whether the compose profile of the speech service is
/// active. A restart is needed only when the service is active and the tag differs from the recorded one.
/// </summary>
public sealed record SttTagUpdate(string Tag, string? PreviousTag, bool IsServiceActive)
{
    public bool RequiresRestart => IsServiceActive && !string.Equals(Tag, PreviousTag, StringComparison.Ordinal);
}
