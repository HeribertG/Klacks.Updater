// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Native bin locator: the engine pointer postgres-engine.json wins (re-read on every call, because klacks.native.json
/// is loaded once at start and would keep the old directory after an engine upgrade), otherwise
/// Updater:PostgresBinDirectory.
/// </summary>
/// <param name="options">Updater configuration (PostgresBinDirectory)</param>
/// <param name="configuration">Host configuration; Hosting:DataRoot locates the pointer</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Native;

public class NativePostgresBinLocator : IPostgresBinLocator
{
    private readonly UpdaterOptions _options;
    private readonly string? _pointerPath;

    public NativePostgresBinLocator(IOptions<UpdaterOptions> options, IConfiguration configuration)
    {
        _options = options.Value;
        var dataRoot = configuration[UpdaterConstants.NativeDataRootKey];
        _pointerPath = string.IsNullOrWhiteSpace(dataRoot)
            ? null
            : Path.Combine(dataRoot, UpdaterConstants.NativeConfigDirectoryName, NativeEngineLayout.PointerFileName);
    }

    public string ResolveBinDirectory()
    {
        var pointer = _pointerPath is null ? null : PostgresEnginePointerFile.Read(_pointerPath);
        return pointer?.BinDirectory ?? _options.PostgresBinDirectory;
    }
}
