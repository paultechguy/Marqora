// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// The links the About box and the Support dialog offer.
///
/// Constants are not usually worth a test, but these are: a mistyped one ships a dead link
/// in a dialog nobody opens twice, and <see cref="ProjectLinks.IsUsable"/> is what decides
/// whether a button appears at all.
/// </summary>
public sealed class ProjectLinksTests
{
    [Fact]
    public void RepositoryUrl_PointsAtTheProjectOnGitHub()
    {
        ProjectLinks.IsUsable(ProjectLinks.RepositoryUrl).ShouldBeTrue();

        var uri = new Uri(ProjectLinks.RepositoryUrl);

        uri.Host.ShouldBe("github.com");
        uri.Scheme.ShouldBe("https");
    }

    [Fact]
    public void SponsorsUrl_PointsAtAGitHubSponsorsPage()
    {
        ProjectLinks.IsUsable(ProjectLinks.SponsorsUrl).ShouldBeTrue();

        var uri = new Uri(ProjectLinks.SponsorsUrl);

        uri.Host.ShouldBe("github.com");
        uri.AbsolutePath.ShouldStartWith("/sponsors/");
    }

    /// <summary>
    /// The licence link is built from the repository URL rather than written out again, so
    /// moving the repository cannot leave the About box pointing at the old one.
    /// </summary>
    [Fact]
    public void LicenceUrl_LivesUnderTheRepository()
    {
        ProjectLinks.IsUsable(ProjectLinks.LicenceUrl).ShouldBeTrue();

        ProjectLinks.LicenceUrl.ShouldStartWith(ProjectLinks.RepositoryUrl);
        ProjectLinks.LicenceUrl.ShouldEndWith("/LICENSE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("github.com/paultechguy/Marqora")]
    public void IsUsable_RejectsAnythingThatIsNotAnAbsoluteAddress(string? url)
    {
        ProjectLinks.IsUsable(url).ShouldBeFalse();
    }

    /// <summary>
    /// Handing an arbitrary URI to the shell hands it to whichever application claims that
    /// scheme. Nothing Marqora offers as a link has any business doing that, so only http
    /// and https get through.
    /// </summary>
    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    public void IsUsable_RejectsSchemesOtherThanHttp(string url)
    {
        ProjectLinks.IsUsable(url).ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://github.com/sponsors/paultechguy")]
    [InlineData("http://example.com")]
    public void IsUsable_AcceptsAbsoluteWebAddresses(string url)
    {
        ProjectLinks.IsUsable(url).ShouldBeTrue();
    }
}
