// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class ProcessRunnerTests
{
    [Test]
    public void BuildFailureMessage_prefers_stderr()
    {
        ProcessRunner.BuildFailureMessage("tool", "arg", 2, "out text", "err text")
            .ShouldBe("'tool arg' exited 2: err text");
    }

    [Test]
    public void BuildFailureMessage_falls_back_to_stdout_when_stderr_is_blank()
    {
        ProcessRunner.BuildFailureMessage("sc.exe", "config x", 1060, "  [SC] OpenService FAILED 1060\r\n", "  ")
            .ShouldBe("'sc.exe config x' exited 1060: [SC] OpenService FAILED 1060");
    }

    [Test]
    public void BuildFailureMessage_truncates_long_output()
    {
        var message = ProcessRunner.BuildFailureMessage("tool", "arg", 1, new string('x', 5000), string.Empty);

        message.Length.ShouldBeLessThan(2200);
        message.ShouldEndWith("...");
    }

    [Test]
    public async Task RunAsync_reports_stdout_when_process_fails_without_stderr()
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", "/c \"echo boom-from-stdout & exit 3\"")
            : ("sh", "-c \"echo boom-from-stdout; exit 3\"");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => ProcessRunner.RunAsync(fileName, arguments));

        exception.Message.ShouldContain("exited 3");
        exception.Message.ShouldContain("boom-from-stdout");
    }
}
