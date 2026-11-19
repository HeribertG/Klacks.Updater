// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// A database of the old cluster that is migrated, with the properties the new cluster must reproduce.
/// </summary>
/// <param name="Name">Database name</param>
/// <param name="Owner">Owning role</param>
/// <param name="Encoding">Server encoding, e.g. UTF8</param>
/// <param name="Collate">LC_COLLATE of the database</param>
/// <param name="CType">LC_CTYPE of the database</param>
/// <param name="LocaleProvider">Locale provider (libc, icu, builtin)</param>
/// <param name="Locale">ICU/builtin locale when the provider is not libc, otherwise null</param>
public record EngineDatabase(string Name, string Owner, string Encoding, string Collate, string CType, string LocaleProvider, string? Locale);
