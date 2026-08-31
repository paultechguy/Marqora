// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Repositories.Tests;

/// <summary>
/// A catalogue pointed at a throwaway snippets folder, using the test seam AppPaths
/// already carries for exactly this.
/// </summary>
internal sealed class Catalog : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "marqora-snippets", Guid.NewGuid().ToString("n"));

    public Catalog()
    {
        var paths = new AppPaths(_root, _root);
        Folder = paths.SnippetsDirectory;
        Directory.CreateDirectory(Folder);
        Subject = new SnippetCatalog(paths, NullLogger<SnippetCatalog>.Instance);
    }

    public string Folder { get; }

    public ISnippetCatalog Subject { get; }

    public Catalog With(string fileName, string body = "body")
    {
        File.WriteAllText(Path.Combine(Folder, fileName), body);

        return this;
    }

    public IReadOnlyList<Snippet> User() =>
        [.. Subject.List(SnippetGroup.General).Where(s => !s.IsBuiltIn)];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a test run over.
        }
    }
}

public class SnippetCatalogTests
{
    [Fact]
    public void Built_in_snippets_are_available_without_a_folder()
    {
        using var catalog = new Catalog();

        catalog.Subject.List(SnippetGroup.General).ShouldNotBeEmpty();
        catalog.Subject.List(SnippetGroup.Diagram).ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_built_in_diagram_is_a_mermaid_fence()
    {
        using var catalog = new Catalog();

        foreach (Snippet snippet in catalog.Subject.List(SnippetGroup.Diagram))
        {
            snippet.Body.ShouldNotBeNull();
            snippet.Body!.ShouldStartWith("```mermaid\n");
            snippet.Body.ShouldEndWith("```");
        }
    }

    [Fact]
    public void A_sort_prefix_orders_the_menu_without_appearing_in_it()
    {
        using var catalog = new Catalog().With("20-second.md").With("10-first.md");

        catalog.User().Select(s => s.Name).ShouldBe(["first", "second"]);
    }

    [Fact]
    public void Separators_in_a_filename_become_spaces_but_casing_is_left_alone()
    {
        using var catalog = new Catalog().With("API_notes-draft.md");

        // Title-casing would turn "API" into "Api". The user named their own file.
        catalog.User().ShouldHaveSingleItem().Name.ShouldBe("API notes draft");
    }

    [Theory]
    [InlineData("plain.md")]
    [InlineData("plain.markdown")]
    [InlineData("plain.txt")]
    public void Recognised_extensions_are_picked_up(string fileName)
    {
        using var catalog = new Catalog().With(fileName);

        catalog.User().ShouldHaveSingleItem().Name.ShouldBe("plain");
    }

    [Fact]
    public void Anything_else_in_the_folder_is_ignored()
    {
        using var catalog = new Catalog().With("notes.docx").With("image.png");

        catalog.User().ShouldBeEmpty();
    }

    [Fact]
    public void A_user_snippet_shadows_a_built_in_of_the_same_name()
    {
        using var catalog = new Catalog().With("Front Matter.md", "mine");

        IReadOnlyList<Snippet> all = catalog.Subject.List(SnippetGroup.General);

        all.Count(s => string.Equals(s.Name, "Front Matter", StringComparison.OrdinalIgnoreCase))
            .ShouldBe(1);
        all.Single(s => string.Equals(s.Name, "Front Matter", StringComparison.OrdinalIgnoreCase))
            .IsBuiltIn.ShouldBeFalse();
    }

    [Fact]
    public async Task A_body_is_read_from_disk_at_the_moment_it_is_needed()
    {
        using var catalog = new Catalog().With("note.md", "first version");

        Snippet snippet = catalog.User().ShouldHaveSingleItem();
        (await catalog.Subject.ReadBodyAsync(snippet)).ShouldBe("first version");

        // Edited behind the app's back. Reading late is what makes this work without a
        // file watcher.
        File.WriteAllText(snippet.Path!, "second version");
        (await catalog.Subject.ReadBodyAsync(snippet)).ShouldBe("second version");
    }

    [Fact]
    public async Task A_snippet_that_has_gone_since_the_menu_opened_reads_as_nothing()
    {
        using var catalog = new Catalog().With("note.md");

        Snippet snippet = catalog.User().ShouldHaveSingleItem();
        File.Delete(snippet.Path!);

        (await catalog.Subject.ReadBodyAsync(snippet)).ShouldBeNull();
    }

    [Fact]
    public void A_missing_folder_is_not_an_error()
    {
        using var catalog = new Catalog();
        Directory.Delete(catalog.Folder, recursive: true);

        catalog.Subject.List(SnippetGroup.General).ShouldNotBeEmpty();
        catalog.User().ShouldBeEmpty();
    }
}
