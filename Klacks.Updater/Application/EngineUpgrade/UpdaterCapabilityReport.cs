// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Builds the JSON value of the UPDATER_CAPABILITIES setting that tells the API which updater runs and what it can
/// do: {"updaterVersion","mode","capabilities":[...],"reportedAtUtc"}.
/// </summary>
using System.Globalization;
using System.Text.Json.Nodes;

namespace Klacks.Updater.Application.EngineUpgrade;

public static class UpdaterCapabilityReport
{
    private const string RoundTripFormat = "O";
    private const string UpdaterVersionProperty = "updaterVersion";
    private const string ModeProperty = "mode";
    private const string CapabilitiesProperty = "capabilities";
    private const string ReportedAtProperty = "reportedAtUtc";

    public static string Build(string updaterVersion, UpdaterMode mode, IReadOnlyList<string> capabilities, DateTime reportedAtUtc) =>
        new JsonObject
        {
            [UpdaterVersionProperty] = updaterVersion,
            [ModeProperty] = mode.ToString(),
            [CapabilitiesProperty] = new JsonArray(capabilities.Select(capability => (JsonNode?)JsonValue.Create(capability)).ToArray()),
            [ReportedAtProperty] = reportedAtUtc.ToUniversalTime().ToString(RoundTripFormat, CultureInfo.InvariantCulture),
        }.ToJsonString();
}
