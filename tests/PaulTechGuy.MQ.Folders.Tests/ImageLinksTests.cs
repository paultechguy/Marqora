// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class ImageLinksTests
{
    private const string Folder = @"C:\notes\guide";

    [Fact]
    public void An_image_beside_the_document_is_linked_by_name() =>
        ImageLinks.For(Folder, @"C:\notes\guide\shot.png").ShouldBe("![shot](shot.png)");

    [Fact]
    public void An_image_below_the_document_takes_forward_slashes() =>
        ImageLinks.For(Folder, @"C:\notes\guide\images\2026\shot.png").ShouldBe("![shot](images/2026/shot.png)");

    [Fact]
    public void A_target_with_a_space_or_parenthesis_is_wrapped() =>
        ImageLinks.For(Folder, @"C:\notes\guide\my images\shot (2).png").ShouldBe("![shot (2)](<my images/shot (2).png>)");

    [Fact]
    public void Brackets_in_the_name_are_escaped_in_the_alt_text() =>
        ImageLinks.For(Folder, @"C:\notes\guide\[draft] shot.png").ShouldBe(@"![\[draft\] shot](<[draft] shot.png>)");

    [Theory]
    [InlineData(@"C:\notes\shot.png")]
    [InlineData(@"C:\notes\guide-old\shot.png")]
    [InlineData(@"D:\notes\guide\shot.png")]
    [InlineData(@"C:\notes\guide")]
    public void An_image_outside_the_document_folder_is_not_linked(string image) =>
        ImageLinks.For(Folder, image).ShouldBeNull();

    [Fact]
    public void The_folder_is_matched_without_regard_to_case() =>
        ImageLinks.For(@"c:\NOTES\guide", @"C:\notes\Guide\shot.png").ShouldBe("![shot](shot.png)");
}
