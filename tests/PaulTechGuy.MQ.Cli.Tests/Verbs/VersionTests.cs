// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-VB-VER-01 (with A11): version and --version print the version Directory.Build.props gives
/// every project of the build, which is the version Marqora.exe reports. That the two built
/// executables agree byte for byte is checked where both exist, in New-Release.ps1.
/// </summary>
public sealed class VersionTests
{
    private static string BuildVersion =>
        XDocument.Load(Repo.At("Directory.Build.props")).Descendants("Version").Single().Value;

    [Theory]
    [InlineData("version")]
    [InlineData("--version")]
    public async Task Version_is_the_builds_version(string arg)
    {
        CliResult result = await CliRunner.RunAsync([arg], cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0);
        result.Out.ShouldBe(BuildVersion + "\n");
    }
}
