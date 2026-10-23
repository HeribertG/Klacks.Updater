// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class NativeInstalledVersionReconcilerTests
{
    private string _root = string.Empty;
    private IVersionLinkSwitcher _links = null!;
    private IInstalledVersionRecorder _recorder = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "klacks-reconcile-" + Guid.NewGuid().ToString("N"));
        _links = Substitute.For<IVersionLinkSwitcher>();
        _recorder = Substitute.For<IInstalledVersionRecorder>();
    }

    private NativeInstalledVersionReconciler CreateReconciler(string? programRoot = null) =>
        new(
            Options.Create(new UpdaterOptions { NativeProgramRoot = programRoot ?? _root }),
            _links,
            _recorder,
            NullLogger<NativeInstalledVersionReconciler>.Instance);

    [Test]
    public void Reconcile_records_the_version_current_points_at()
    {
        _links.ReadTarget(Path.Combine(_root, "current")).Returns(Path.Combine(_root, "versions", "1.0.50"));

        CreateReconciler().Reconcile();

        _recorder.Received(1).Record("1.0.50");
    }

    [Test]
    public void Reconcile_accepts_a_link_target_with_a_trailing_separator()
    {
        _links.ReadTarget(Arg.Any<string>())
            .Returns(Path.Combine(_root, "versions", "1.0.50-beta.1") + Path.DirectorySeparatorChar);

        CreateReconciler().Reconcile();

        _recorder.Received(1).Record("1.0.50-beta.1");
    }

    [Test]
    public void Reconcile_records_nothing_when_current_points_nowhere()
    {
        _links.ReadTarget(Arg.Any<string>()).Returns((string?)null);

        CreateReconciler().Reconcile();

        _recorder.DidNotReceive().Record(Arg.Any<string>());
    }

    [Test]
    public void Reconcile_records_nothing_when_current_is_not_a_version_directory()
    {
        _links.ReadTarget(Arg.Any<string>()).Returns(Path.Combine(_root, "staging"));

        CreateReconciler().Reconcile();

        _recorder.DidNotReceive().Record(Arg.Any<string>());
    }

    [Test]
    public void Reconcile_records_nothing_when_current_points_outside_the_versions_directory()
    {
        _links.ReadTarget(Arg.Any<string>()).Returns(Path.Combine(_root, "elsewhere", "1.0.50"));

        CreateReconciler().Reconcile();

        _recorder.DidNotReceive().Record(Arg.Any<string>());
    }

    [Test]
    public void Reconcile_records_nothing_when_current_points_into_a_nested_version_directory()
    {
        _links.ReadTarget(Arg.Any<string>()).Returns(Path.Combine(_root, "versions", "1.0.49", "1.0.50"));

        CreateReconciler().Reconcile();

        _recorder.DidNotReceive().Record(Arg.Any<string>());
    }

    [Test]
    public void Reconcile_never_throws_when_the_link_cannot_be_read()
    {
        _links.When(links => links.ReadTarget(Arg.Any<string>())).Do(_ => throw new UnauthorizedAccessException("denied"));

        Should.NotThrow(() => CreateReconciler().Reconcile());

        _recorder.DidNotReceive().Record(Arg.Any<string>());
    }

    [Test]
    public void Reconcile_never_throws_when_the_recorder_fails()
    {
        _links.ReadTarget(Arg.Any<string>()).Returns(Path.Combine(_root, "versions", "1.0.50"));
        _recorder.When(recorder => recorder.Record(Arg.Any<string>()))
            .Do(_ => throw new UnauthorizedAccessException("registry denied"));

        Should.NotThrow(() => CreateReconciler().Reconcile());
    }

    [Test]
    public void Reconcile_never_throws_without_a_valid_program_root()
    {
        Should.NotThrow(() => CreateReconciler(programRoot: "relative").Reconcile());

        _recorder.DidNotReceive().Record(Arg.Any<string>());
    }
}
