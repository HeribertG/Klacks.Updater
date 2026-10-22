// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Pure helpers for the output of `docker compose config --services` (one service name per line): membership check
/// and the failure message for an on-prem compose file that predates a service.
/// </summary>

namespace Klacks.Updater.Infrastructure;

public static class ComposeServices
{
    public static bool Contains(string servicesOutput, string serviceName) =>
        servicesOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(serviceName, StringComparer.Ordinal);

    public static string MissingServiceMessage(string serviceName) =>
        $"The compose file does not define the service '{serviceName}'. Download the current on-prem bundle and retry.";
}
