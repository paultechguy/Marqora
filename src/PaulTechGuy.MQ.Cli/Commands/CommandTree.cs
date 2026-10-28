// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.CommandLine.Help;
using PaulTechGuy.MQ.Cli.Verbs;

namespace PaulTechGuy.MQ.Cli.Commands;

/// <summary>
/// Every verb marqora-cli has, its options and its examples (CLI-CMD-01): one command per verb,
/// named after the menu item it mirrors, with export and themes holding the verbs nested under
/// them. A verb's own file does the work; this is the one place the command line is spelled out,
/// so the manual and the help are checked against it.
/// </summary>
internal sealed class CommandTree
{
    private CommandTree(RootCommand root, CommandShared shared)
    {
        Root = root;
        Shared = shared;
    }

    public RootCommand Root { get; }

    public CommandShared Shared { get; }

    /// <summary>Every command, nested ones included, with the export verbs whether hidden or not.</summary>
    public IEnumerable<Command> AllCommands => Walk(Root).Skip(1);

    public static CommandTree Create(CliEnvironment environment)
    {
        var shared = new CommandShared(environment);
        var root = new RootCommand("Marqora's document commands, without a window: format, number, check and export markdown files.");

        // Directives - [suggest], [diagram] - would be features nobody documented. Response files
        // are switched off where the parser is configured (CLI-CMD-05).
        root.Directives.Clear();

        foreach (Option option in shared.Globals.All)
        {
            root.Options.Add(option);
        }

        root.Subcommands.Add(Format(shared));
        root.Subcommands.Add(Number(shared));
        root.Subcommands.Add(Unnumber(shared));
        root.Subcommands.Add(Replace(shared));
        root.Subcommands.Add(RenumberLists(shared));
        root.Subcommands.Add(DiagramLayout(shared));
        root.Subcommands.Add(Check(shared));
        root.Subcommands.Add(Toc(shared));
        root.Subcommands.Add(Outline(shared));
        root.Subcommands.Add(Folio(shared));
        root.Subcommands.Add(Unpack(shared));
        root.Subcommands.Add(Export(shared));
        root.Subcommands.Add(Themes(shared));
        root.Subcommands.Add(Version(shared));

        HelpOption help = root.Options.OfType<HelpOption>().Single();
        help.Action = new ExamplesHelpAction((HelpAction)help.Action!, shared.Examples);

        // --version prints what the version verb prints, and nothing else.
        VersionOption version = root.Options.OfType<VersionOption>().Single();
        version.Action = new VersionVerb.Action(environment);

        return new CommandTree(root, shared);
    }

    private static Command Format(CommandShared shared)
    {
        Command command = shared.Verb(
            "format",
            "Format markdown as the app's Format Document does.",
            [
                "marqora-cli format README.md",
                "marqora-cli format --write \"docs/**/*.md\"",
                "marqora-cli format --check \"docs/**/*.md\"",
            ]);

        InputOptions inputs = CommandShared.AddInputs(command);
        ModeOptions modes = CommandShared.AddModes(command, "the formatted text");

        shared.SetAction(command, "format", (context, parse) => FormatVerb.RunAsync(context, parse, inputs, modes));

        return command;
    }

    private static Command Number(CommandShared shared)
    {
        Command command = shared.Verb(
            "number",
            "Number the headings, as Format > Number Headings does.",
            [
                "marqora-cli number --dry-run Guide.md",
                "marqora-cli number --from h2 --style dot-space --write Guide.md",
            ],
            alias: "number-headings");

        InputOptions inputs = CommandShared.AddInputs(command);
        ModeOptions modes = CommandShared.AddModes(command, "the headings numbered");

        var from = new Option<string?>("--from")
        {
            Description = "The heading level numbering starts at. Defaults to the settings in force, then the level the document already numbers from, then h1, as the app's dialog suggests.",
            HelpName = "h1|h2|h3",
        }.AcceptOnlyFromAmong("h1", "h2", "h3");

        var style = new Option<string?>("--style")
        {
            Description = "What follows each number. Defaults to the settings in force, or two-spaces.",
            HelpName = "two-spaces|one-space|dot-space|paren-space",
        }.AcceptOnlyFromAmong("two-spaces", "one-space", "dot-space", "paren-space");

        command.Options.Add(from);
        command.Options.Add(style);

        shared.SetAction(command, "number", (context, parse) => NumberVerb.RunAsync(context, parse, inputs, modes, from, style));

        return command;
    }

    private static Command Unnumber(CommandShared shared)
    {
        Command command = shared.Verb(
            "unnumber",
            "Remove heading numbers, as Format > Remove Heading Numbers does.",
            [
                "marqora-cli unnumber Guide.md",
                "marqora-cli unnumber --write \"docs/**/*.md\"",
            ],
            alias: "remove-heading-numbers");

        InputOptions inputs = CommandShared.AddInputs(command);
        ModeOptions modes = CommandShared.AddModes(command, "the heading numbers removed");

        shared.SetAction(command, "unnumber", (context, parse) => UnnumberVerb.RunAsync(context, parse, inputs, modes));

        return command;
    }

    private static Command Replace(CommandShared shared)
    {
        Command command = shared.Verb(
            "replace",
            "Find and replace, as Edit > Replace All does, with the same options as the Find bar.",
            [
                "marqora-cli replace --find \"Marqora 1.0\" --with \"Marqora 1.1\" --match-case --write docs/*.md",
                "marqora-cli replace --find \"colou?r\" --with color --regex --dry-run README.md",
            ]);

        InputOptions inputs = CommandShared.AddInputs(command);
        ModeOptions modes = CommandShared.AddModes(command, "the replacements made");

        var find = new Option<string>("--find") { Description = "The text to find.", Required = true, HelpName = "text" };
        var with = new Option<string>("--with") { Description = "What to put in its place. May be empty, which deletes each match.", Required = true, HelpName = "text" };
        var matchCase = new Option<bool>("--match-case") { Description = "Only matches with the same capitals." };
        var wholeWord = new Option<bool>("--whole-word") { Description = "Only matches that are whole words." };
        var regex = new Option<bool>("--regex") { Description = "Treat --find as a .NET regular expression, and --with as its replacement pattern." };

        command.Options.Add(find);
        command.Options.Add(with);
        command.Options.Add(matchCase);
        command.Options.Add(wholeWord);
        command.Options.Add(regex);

        shared.SetAction(command, "replace", (context, parse) => ReplaceVerb.RunAsync(context, parse, inputs, modes, new ReplaceVerb.Options(find, with, matchCase, wholeWord, regex)));

        return command;
    }

    private static Command RenumberLists(CommandShared shared)
    {
        Command command = shared.Verb(
            "renumber-lists",
            "Renumber every ordered list, as Renumber List does for one.",
            [
                "marqora-cli renumber-lists --dry-run Guide.md",
                "marqora-cli renumber-lists --style sequential --write \"docs/**/*.md\"",
            ]);

        InputOptions inputs = CommandShared.AddInputs(command);
        ModeOptions modes = CommandShared.AddModes(command, "its lists renumbered");

        var style = new Option<string?>("--style")
        {
            Description = "How to number every list: 1, 2, 3 (sequential), all 1 (ones), or all 0 (zeros). Without it, each list gets the numbering the app would suggest for it.",
            HelpName = "sequential|ones|zeros",
        }.AcceptOnlyFromAmong("sequential", "ones", "zeros");

        command.Options.Add(style);

        shared.SetAction(command, "renumber-lists", (context, parse) => RenumberListsVerb.RunAsync(context, parse, inputs, modes, style));

        return command;
    }

    private static Command DiagramLayout(CommandShared shared)
    {
        Command command = shared.Verb(
            "diagram-layout",
            "Set the direction mermaid diagrams flow in, as Format > Diagram Layout does.",
            [
                "marqora-cli diagram-layout --direction left-to-right --write Guide.md",
                "marqora-cli diagram-layout --direction top-to-bottom --diagram 2 --dry-run Guide.md",
            ]);

        InputOptions inputs = CommandShared.AddInputs(command);
        ModeOptions modes = CommandShared.AddModes(command, "its diagrams' direction set");

        var direction = new Option<string>("--direction")
        {
            Description = "The direction to set.",
            Required = true,
            HelpName = "top-to-bottom|left-to-right",
        }.AcceptOnlyFromAmong("top-to-bottom", "left-to-right");

        var diagram = new Option<int?>("--diagram")
        {
            Description = "Only the n-th mermaid diagram in the document, counting from 1.",
            HelpName = "n",
        };

        diagram.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int?>() is < 1)
            {
                result.AddError("--diagram counts from 1.");
            }
        });

        command.Options.Add(direction);
        command.Options.Add(diagram);

        shared.SetAction(command, "diagram-layout", (context, parse) => DiagramLayoutVerb.RunAsync(context, parse, inputs, modes, direction, diagram));

        return command;
    }

    private static Command Check(CommandShared shared)
    {
        Command command = shared.Verb(
            "check",
            "Find dead local links and anchors and missing pictures, as the source pane's squiggles do. Exits 1 when it finds any.",
            [
                "marqora-cli check \"docs/**/*.md\"",
                "marqora-cli check --ignore-external-images README.md",
                "marqora-cli check --style --strict Guide.md",
            ]);

        InputOptions inputs = CommandShared.AddInputs(command);

        var style = new Option<bool>("--style") { Description = "Also report the style diagnostics, as hints. They fail the run only with --strict." };
        var strict = new Option<bool>("--strict") { Description = "With --style, any hint fails the run too." };
        var ignoreExternal = new Option<bool>("--ignore-external-images")
        {
            Description = "Pictures from the web and pictures outside the document's folder are not errors; --verbose lists them.",
        };

        command.Options.Add(style);
        command.Options.Add(strict);
        command.Options.Add(ignoreExternal);

        command.Validators.Add(result =>
        {
            if (result.GetValue(strict) && !result.GetValue(style))
            {
                result.AddError("--strict needs --style: it makes the style hints fail the run.");
            }
        });

        shared.SetAction(command, "check", (context, parse) => CheckVerb.RunAsync(context, parse, inputs, style, strict, ignoreExternal));

        return command;
    }

    private static Command Toc(CommandShared shared)
    {
        Command command = shared.Verb(
            "toc",
            "Print the document's contents as a nested list of links.",
            [
                "marqora-cli toc Guide.md",
                "marqora-cli toc Guide.md > contents.md",
            ]);

        InputOptions inputs = CommandShared.AddInputs(command);

        // Not a feature: here so the answer is a sentence rather than "unrecognized option".
        var write = new Option<bool>("--write", "-w") { Hidden = true };

        command.Options.Add(write);
        command.Validators.Add(result =>
        {
            if (result.GetValue(write))
            {
                result.AddError("toc prints a contents list; it does not write one into the document, because the app has no command that does.");
            }
        });

        shared.SetAction(command, "toc", (context, parse) => TocVerb.RunAsync(context, parse, inputs));

        return command;
    }

    private static Command Outline(CommandShared shared)
    {
        Command command = shared.Verb(
            "outline",
            "Print the heading tree, with line numbers, as the outline pane shows it.",
            [
                "marqora-cli outline Guide.md",
                "marqora-cli outline --json Guide.md",
            ]);

        InputOptions inputs = CommandShared.AddInputs(command);

        shared.SetAction(command, "outline", (context, parse) => OutlineVerb.RunAsync(context, parse, inputs));

        return command;
    }

    private static Command Folio(CommandShared shared)
    {
        Command command = shared.Verb(
            "folio",
            "Share documents as one Folio, as File > Share as Folio does: a folder, a zip, or a single page.",
            [
                "marqora-cli folio -o Handbook.zip docs/handbook/*.md",
                "marqora-cli folio -o Handbook/ --shrink-images 1600 docs/handbook/*.md",
                "marqora-cli folio --dry-run -o Handbook.zip docs/handbook/*.md",
            ]);

        InputOptions inputs = CommandShared.AddInputs(command, "The markdown files to share, or glob patterns, in the order the Folio is to be read.", readsStandardInput: false);

        var output = new Option<string>("--output", "-o")
        {
            Description = "Where the Folio goes: a .zip, a folder (ending in a separator, or one that exists), or an .html page.",
            Required = true,
            HelpName = "path",
        };

        var form = new Option<string?>("--form")
        {
            Description = "The form, when the path does not say: single, folder or zip.",
            HelpName = "single|folder|zip",
        }.AcceptOnlyFromAmong("single", "folder", "zip");

        var theme = ThemeOption();
        var shrink = new Option<int?>("--shrink-images")
        {
            Description = "Reduce pictures wider than this many pixels, as the preflight's \"Shrink images wider than\" does.",
            HelpName = "pixels",
        };

        var fetch = new Option<bool>("--fetch-web-images")
        {
            Description = "Collect pictures the documents name by web address. The only option that makes marqora-cli use the network; --dry-run names the sites first.",
        };

        var timeout = new Option<int?>("--timeout")
        {
            Description = $"For the single page: how long one document's render may take, in seconds, before the Folio fails with exit 5. Default {ExportVerb.DefaultTimeoutSeconds}.",
            HelpName = "seconds",
        };

        var dryRun = new Option<bool>("--dry-run") { Description = "Write nothing; print what the preflight would show." };
        var overwrite = new Option<bool>("--overwrite") { Description = "Replace an output that already exists: a page, a zip, or what is in a folder." };

        command.Options.Add(output);
        command.Options.Add(form);
        command.Options.Add(theme);
        command.Options.Add(shrink);
        command.Options.Add(fetch);
        command.Options.Add(timeout);
        command.Options.Add(dryRun);
        command.Options.Add(overwrite);

        shared.SetAction(command, "folio", (context, parse) => FolioVerb.RunAsync(context, parse, new FolioVerb.Options(inputs, output, form, theme, shrink, timeout, fetch, dryRun, overwrite)));

        return command;
    }

    private static Command Unpack(CommandShared shared)
    {
        Command command = shared.Verb(
            "unpack",
            "Take a single-file Folio apart into its documents and pictures, as File > Open Folio does.",
            [
                "marqora-cli unpack Handbook.html --out-dir Handbook",
            ]);

        var folio = new Argument<string>("folio") { Description = "The single-file Folio: an .html page." };
        var outDir = new Option<string>("--out-dir")
        {
            Description = "The folder to unpack into. It must be missing or empty, unless --overwrite is given.",
            Required = true,
            HelpName = "folder",
        };

        var overwrite = new Option<bool>("--overwrite") { Description = "Replace what is already in the folder." };

        command.Arguments.Add(folio);
        command.Options.Add(outDir);
        command.Options.Add(overwrite);

        shared.SetAction(command, "unpack", (context, parse) => UnpackVerb.RunAsync(context, parse, folio, outDir, overwrite));

        return command;
    }

    /// <summary>
    /// The exports, with the options every one of them shares (WP-M2-08): html, pdf and word, each
    /// through the shared export run and compared with the app's own file (WP-M2-09 to M2-13).
    /// </summary>
    private static Command Export(CommandShared shared)
    {
        Command export = shared.Verb("export", "Export documents as HTML, PDF or Word, as File > Export does.", []);

        foreach ((string name, string what, ExportFormat format) in new (string, string, ExportFormat)[] { ("html", "HTML", new HtmlFormat()), ("pdf", "PDF", new PdfFormat()), ("word", "Word", new WordFormat()) })
        {
            Command command = shared.Verb(name, $"Export each document as {what}.", [$"marqora-cli export {name} docs/guide.md", $"marqora-cli export {name} --out-dir dist docs/*.md"]);

            InputOptions inputs = CommandShared.AddInputs(command);

            var output = new Option<string?>("--output", "-o")
            {
                Description = format.WritesStandardOutput
                    ? "The file to write, for one document, or - for standard output. Without -o or --out-dir, each output is written beside its document."
                    : "The file to write, for one document. Without -o or --out-dir, each output is written beside its document.",
                HelpName = "file",
            };

            var outDir = new Option<string?>("--out-dir")
            {
                Description = $"The folder to write into, for any number of documents: each is named after its document, with {format.Extension}. Made if it is missing.",
                HelpName = "folder",
            };

            var timeout = new Option<int?>("--timeout")
            {
                Description = $"How long one document's render may take, in seconds, before that file fails with exit 5. Default {ExportVerb.DefaultTimeoutSeconds}.",
                HelpName = "seconds",
            };

            var dryRun = new Option<bool>("--dry-run") { Description = "Render and write nothing; print where each output would go, the theme and page setup, and what is already missing." };
            var overwrite = new Option<bool>("--overwrite") { Description = "Replace an output that already exists." };
            var theme = ThemeOption();

            command.Options.Add(output);
            command.Options.Add(outDir);
            command.Options.Add(theme);
            command.Options.Add(timeout);
            command.Options.Add(dryRun);
            command.Options.Add(overwrite);
            format.AddOptions(command);

            export.Subcommands.Add(command);

            var options = new ExportVerb.Options(inputs, output, outDir, theme, timeout, dryRun, overwrite);

            shared.SetAction(command, "export " + name, (context, parse) => ExportVerb.RunAsync(context, parse, options, format));
        }

        return export;
    }

    private static Command Themes(CommandShared shared)
    {
        Command themes = shared.Verb("themes", "The color themes exports and Folios can wear.", []);

        Command list = shared.Verb(
            "list",
            "List each color theme's id, name and description, in the gallery's order.",
            [
                "marqora-cli themes list",
                "marqora-cli themes list --json",
            ]);

        themes.Subcommands.Add(list);
        shared.SetAction(list, "themes list", (context, _) => ThemesListVerb.RunAsync(context));

        return themes;
    }

    private static Command Version(CommandShared shared)
    {
        Command command = shared.Verb(
            "version",
            "Print marqora-cli's version, which is the app's from the same build.",
            [
                "marqora-cli version",
                "marqora-cli --version",
            ]);

        shared.SetAction(command, "version", (context, _) => VersionVerb.RunAsync(context));

        return command;
    }

    private static Option<string?> ThemeOption() => new("--theme")
    {
        Description = "The color theme to wear, by id or name: see 'marqora-cli themes list'. Without it, the settings' theme or Default.",
        HelpName = "theme",
    };

    private static IEnumerable<Command> Walk(Command command)
    {
        yield return command;

        foreach (Command child in command.Subcommands)
        {
            foreach (Command descendant in Walk(child))
            {
                yield return descendant;
            }
        }
    }
}
