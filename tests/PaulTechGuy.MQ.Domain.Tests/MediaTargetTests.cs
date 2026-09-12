// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// What kind of place a reference points at, decided from the text alone.
///
/// The drive-letter cases are the reason this type exists. A scheme test written as "a letter,
/// then more letters, then a colon" with a zero-or-more in the middle reads "C:" as a scheme, and
/// every picture named by an absolute Windows path was waved through as somebody else's URL -
/// silently, because nothing downstream ever saw it. Folio hit the same trap and fixed it the
/// same way; this is the third copy of that reasoning and meant to be the last.
/// </summary>
public class MediaTargetTests
{
    [Theory]
    [InlineData("https://example.com/x.png")]
    [InlineData("http://example.com/x.png")]
    [InlineData("HTTPS://EXAMPLE.COM/X.PNG")]
    [InlineData("//example.com/x.png")]
    [InlineData("https://localhost:8080/x.png")]
    [InlineData("http://127.0.0.1/x.png")]
    public void An_address_on_the_web_is_remote(string url) =>
        MediaTarget.Classify(url).ShouldBe(MediaTargetKind.Remote);

    [Fact]
    public void Loopback_is_not_an_exemption()
    {
        // Still a request leaving the process. "No network calls" does not grow an asterisk for
        // the machine you happen to be sitting at.
        MediaTarget.Classify("http://localhost/x.png").ShouldBe(MediaTargetKind.Remote);
    }

    [Theory]
    [InlineData(@"C:\pics\x.png")]
    [InlineData("C:/pics/x.png")]
    [InlineData("c:/pics/x.png")]
    [InlineData("file:///C:/pics/x.png")]
    [InlineData("FILE:///C:/pics/x.png")]
    public void A_local_absolute_path_is_its_own_kind(string url) =>
        MediaTarget.Classify(url).ShouldBe(MediaTargetKind.LocalAbsolute);

    [Theory]
    [InlineData("mailto:paul@example.com")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("blob:something")]
    [InlineData("ftp://example.com/x.png")]
    public void Anything_else_with_a_real_scheme_is_somebody_elses_problem(string url) =>
        MediaTarget.Classify(url).ShouldBe(MediaTargetKind.OtherScheme);

    [Theory]
    [InlineData("x.png")]
    [InlineData("./art/x.png")]
    [InlineData("../art/x.png")]
    [InlineData("/images/x.png")]
    [InlineData("art/x.png?v=2")]
    public void Everything_else_resolves_against_the_folder(string url) =>
        MediaTarget.Classify(url).ShouldBe(MediaTargetKind.Relative);

    [Fact]
    public void A_rooted_or_climbing_path_stays_relative_on_purpose()
    {
        // Neither resolves inside the document's folder, and both have always been reported as
        // broken by the dead-link check. Moving them into another kind would change what an
        // existing document says for reasons that have nothing to do with this rule.
        MediaTarget.Classify("/images/x.png").ShouldBe(MediaTargetKind.Relative);
        MediaTarget.Classify("../x.png").ShouldBe(MediaTargetKind.Relative);
    }

    [Fact]
    public void A_fragment_is_a_place_in_this_document() =>
        MediaTarget.Classify("#install").ShouldBe(MediaTargetKind.Fragment);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_at_all_is_treated_as_relative(string? url) =>
        MediaTarget.Classify(url).ShouldBe(MediaTargetKind.Relative);

    [Fact]
    public void Surrounding_whitespace_does_not_change_the_answer() =>
        MediaTarget.Classify("  https://example.com/x.png  ").ShouldBe(MediaTargetKind.Remote);

    // ---- the path behind an absolute reference ----------------------------

    [Fact]
    public void A_file_url_gives_back_a_filesystem_path() =>
        MediaTarget.LocalPathOf("file:///C:/pics/x.png").ShouldBe(@"C:\pics\x.png");

    [Fact]
    public void A_plain_absolute_path_gives_back_itself() =>
        MediaTarget.LocalPathOf(@"C:\pics\x.png").ShouldBe(@"C:\pics\x.png");

    [Theory]
    [InlineData("https://example.com/x.png")]
    [InlineData("art/x.png")]
    [InlineData("#install")]
    public void Anything_that_is_not_a_local_absolute_has_no_path(string url) =>
        MediaTarget.LocalPathOf(url).ShouldBeNull();

    // ---- shares -----------------------------------------------------------

    [Theory]
    [InlineData(@"\\server\share\x.png")]
    [InlineData("//server/share/x.png")]
    public void A_share_is_recognized_so_it_is_never_probed(string path)
    {
        // File.Exists on a share blocks until the other machine answers, and the check that would
        // call it runs while somebody is typing. A disconnected VPN would stall the editor.
        MediaTarget.IsNetworkShare(path).ShouldBeTrue();
    }

    [Theory]
    [InlineData(@"C:\pics\x.png")]
    [InlineData("art/x.png")]
    [InlineData("")]
    [InlineData(null)]
    public void An_ordinary_path_is_not_a_share(string? path) =>
        MediaTarget.IsNetworkShare(path).ShouldBeFalse();
}
