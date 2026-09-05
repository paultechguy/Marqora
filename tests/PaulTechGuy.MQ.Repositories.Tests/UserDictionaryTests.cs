// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Repositories;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Repositories.Tests;

/// <summary>
/// The word list is the one file Marqora writes that a person is expected to open: it can be
/// committed to a repository as a project glossary, reviewed in a diff, and edited in the app
/// itself.
///
/// So the bugs worth catching are about being a good citizen of a text file. Reading has to
/// tolerate whatever a person leaves behind - either line ending, blank lines, stray indentation,
/// a comment explaining why a word is there - and writing has to be stable, so that two machines
/// holding the same words produce the same bytes and a diff shows only what actually changed.
/// </summary>
public sealed class UserDictionaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "marqora-dictionary", Guid.NewGuid().ToString("n"));

    private readonly TextUserDictionaryRepository _repository;
    private readonly AppPaths _paths;

    public UserDictionaryTests()
    {
        Directory.CreateDirectory(_root);

        _paths = new AppPaths(_root, _root);
        _paths.EnsureCreated();

        _repository = new TextUserDictionaryRepository(_paths, NullLogger<TextUserDictionaryRepository>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ------------------------------------------------------------------- reading

    [Fact]
    public async Task A_list_that_does_not_exist_yet_reads_as_empty()
    {
        // The ordinary state on a fresh install, and not a failure.
        (await Load()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Words_are_read_back_in_the_order_the_file_holds_them()
    {
        await Write("Marqora\nKaTeX\nmermaid\n");

        (await Load()).ShouldBe(["Marqora", "KaTeX", "mermaid"]);
    }

    [Theory]
    [InlineData("Marqora\nKaTeX\n")]
    [InlineData("Marqora\r\nKaTeX\r\n")]
    [InlineData("Marqora\r\nKaTeX\n")]
    public async Task Either_line_ending_is_read(string contents)
    {
        await Write(contents);

        (await Load()).ShouldBe(["Marqora", "KaTeX"]);
    }

    [Fact]
    public async Task Blank_lines_and_indentation_are_forgiven()
    {
        await Write("\n  Marqora  \n\n\tKaTeX\n\n");

        (await Load()).ShouldBe(["Marqora", "KaTeX"]);
    }

    [Fact]
    public async Task A_comment_explains_a_word_without_becoming_one()
    {
        // The reason a shared glossary is worth committing: it can say why a word is in it.
        await Write("# Words this project uses\nMarqora\n# the diagram library\nmermaid\n");

        (await Load()).ShouldBe(["Marqora", "mermaid"]);
    }

    // ------------------------------------------------------------------- writing

    [Fact]
    public async Task Writing_sorts_so_that_two_machines_produce_the_same_file()
    {
        await _repository.SaveAsync(["mermaid", "Marqora", "KaTeX"], TestContext.Current.CancellationToken);

        (await Load()).ShouldBe(["KaTeX", "Marqora", "mermaid"]);
    }

    [Fact]
    public async Task Writing_removes_a_word_that_differs_only_in_case()
    {
        await _repository.SaveAsync(["Marqora", "marqora", "MARQORA"], TestContext.Current.CancellationToken);

        (await Load()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Writing_drops_blanks_rather_than_leaving_empty_lines()
    {
        await _repository.SaveAsync(["Marqora", "   ", "", "KaTeX"], TestContext.Current.CancellationToken);

        (await Load()).ShouldBe(["KaTeX", "Marqora"]);
    }

    [Fact]
    public async Task The_file_carries_no_byte_order_mark()
    {
        // A BOM would show up as stray characters at the top of every diff.
        await _repository.SaveAsync(["Marqora"], TestContext.Current.CancellationToken);

        byte[] bytes = await File.ReadAllBytesAsync(_paths.UserDictionaryPath, TestContext.Current.CancellationToken);

        bytes.Take(3).ShouldNotBe([(byte)0xEF, (byte)0xBB, (byte)0xBF]);
    }

    [Fact]
    public async Task A_write_replaces_the_previous_list_rather_than_appending_to_it()
    {
        await _repository.SaveAsync(["Marqora"], TestContext.Current.CancellationToken);
        await _repository.SaveAsync(["KaTeX"], TestContext.Current.CancellationToken);

        (await Load()).ShouldBe(["KaTeX"]);
    }

    [Fact]
    public async Task A_write_leaves_no_temporary_file_behind()
    {
        await _repository.SaveAsync(["Marqora"], TestContext.Current.CancellationToken);

        File.Exists(_paths.UserDictionaryPath + ".tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task A_round_trip_through_the_file_keeps_every_word()
    {
        string[] words = ["Marqora", "KaTeX", "mermaid", "frontmatter", "WinUI"];

        await _repository.SaveAsync(words, TestContext.Current.CancellationToken);

        (await Load()).ShouldBe(words, ignoreOrder: true);
    }

    private Task<IReadOnlyList<string>> Load() =>
        _repository.LoadAsync(TestContext.Current.CancellationToken);

    private Task Write(string contents) =>
        File.WriteAllTextAsync(_paths.UserDictionaryPath, contents, TestContext.Current.CancellationToken);
}
