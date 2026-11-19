// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Text.Json;
using Klacks.Updater.Application;
using Klacks.Updater.Infrastructure.EngineUpgrade.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class PostgresEnginePointerFileTests
{
    private string _dataRoot = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "klacks-pointer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dataRoot, UpdaterConstants.NativeConfigDirectoryName));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dataRoot, recursive: true);

    private string PointerPath => Path.Combine(_dataRoot, UpdaterConstants.NativeConfigDirectoryName, NativeEngineLayout.PointerFileName);

    [Test]
    public void Pointer_is_written_with_exactly_the_installer_schema()
    {
        PostgresEnginePointerFile.Write(PointerPath, new PostgresEnginePointer(1, 19, @"C:\Program Files\Klacks\pgsql-19", @"C:\ProgramData\Klacks\pgdata-19"));

        using var document = JsonDocument.Parse(File.ReadAllText(PointerPath));
        document.RootElement.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["schemaVersion", "major", "postgresDirectory", "dataDirectory"]);
        document.RootElement.GetProperty("postgresDirectory").GetString().ShouldBe(@"C:\Program Files\Klacks\pgsql-19");
    }

    [Test]
    public void Missing_pointer_means_legacy_and_the_locator_falls_back_to_the_configured_directory()
    {
        PostgresEnginePointerFile.Read(PointerPath).ShouldBeNull();

        Locator(@"C:\Klacks\pgsql\bin").ResolveBinDirectory().ShouldBe(@"C:\Klacks\pgsql\bin");
    }

    [Test]
    public void Locator_follows_the_pointer()
    {
        File.WriteAllText(PointerPath, "\uFEFF{\"schemaVersion\":1,\"major\":19,\"postgresDirectory\":\"C:\\\\Pr\u00f6gram\\\\pgsql-19\",\"dataDirectory\":\"D:\\\\data\\\\pgdata-19\",\"extra\":true}");

        Locator(@"C:\Klacks\pgsql\bin").ResolveBinDirectory().ShouldBe(NativeEngineLayout.BinDirectory("C:\\Pr\u00f6gram\\pgsql-19"));
    }

    [TestCase("{\"schemaVersion\":2,\"major\":19,\"postgresDirectory\":\"a\",\"dataDirectory\":\"b\"}")]
    [TestCase("{\"schemaVersion\":1,\"major\":0,\"postgresDirectory\":\"a\",\"dataDirectory\":\"b\"}")]
    [TestCase("{\"schemaVersion\":1,\"major\":19,\"dataDirectory\":\"b\"}")]
    public void Unsupported_or_incomplete_pointer_is_refused(string content)
    {
        File.WriteAllText(PointerPath, content);

        Should.Throw<Exception>(() => PostgresEnginePointerFile.Read(PointerPath));
    }

    [Test]
    public void Native_config_bin_directory_is_replaced_and_other_entries_are_kept()
    {
        var configPath = Path.Combine(_dataRoot, UpdaterConstants.NativeConfigDirectoryName, UpdaterConstants.NativeConfigFileName);
        File.WriteAllText(configPath, "{\"Updater\":{\"Mode\":\"Native\",\"PostgresBinDirectory\":\"C:\\\\old\\\\bin\"},\"Other\":1}");

        PostgresEnginePointerFile.WritePostgresBinDirectory(configPath, @"C:\new\bin");

        PostgresEnginePointerFile.ReadPostgresBinDirectory(configPath).ShouldBe(@"C:\new\bin");
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        document.RootElement.GetProperty("Updater").GetProperty("Mode").GetString().ShouldBe("Native");
        document.RootElement.GetProperty("Other").GetInt32().ShouldBe(1);
    }

    private NativePostgresBinLocator Locator(string configuredBinDirectory) =>
        new(
            Options.Create(new UpdaterOptions { PostgresBinDirectory = configuredBinDirectory }),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [UpdaterConstants.NativeDataRootKey] = _dataRoot }).Build());
}
