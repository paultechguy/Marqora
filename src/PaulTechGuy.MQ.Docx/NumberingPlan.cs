// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PaulTechGuy.MQ.Domain;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Docx;

/// <summary>Where a list paragraph sits: which numbering instance, and how deep.</summary>
internal readonly record struct ListPlacement(int NumberId, int Level, bool Tight);

/// <summary>
/// Works out the numbering every list in the document needs, before any of it is written.
///
/// Word separates the definition of a list from the running count, and confusing the two is
/// the classic way to produce a document that looks right until the second list:
///
/// <list type="bullet">
///   <item>An <em>abstract</em> numbering is a shape - nine levels, each with a format, a
///   pattern and an indent. It holds no counter and can be shared by every list in the
///   document that looks the same.</item>
///   <item>A numbering <em>instance</em> points at an abstract one and owns the count. It is
///   what a paragraph refers to.</item>
/// </list>
///
/// Two separate markdown lists that name the same instance are, to Word, one list interrupted
/// by other content: the second carries on 4, 5, 6 rather than starting again. So every list
/// gets its own instance, and they share the abstract definitions between them.
///
/// A separate instance is not enough on its own, though it reads as if it should be. Word
/// keeps the running count per abstract definition, so a second instance of a shared
/// definition still carries on from the first. What restarts it is a start override on the
/// instance, and every instance writes one for every level - see <see cref="BuildInstance"/>.
///
/// A pre-pass rather than work done during the walk, because the numbering part has to be
/// written as a whole and because a list's shape is only known once its deepest branch has
/// been seen.
/// </summary>
internal sealed class NumberingPlan
{
    /// <summary>Word allows nine levels. Deeper markdown is clamped rather than refused.</summary>
    private const int MaximumLevel = 8;

    /// <summary>
    /// A twip indent per level.
    ///
    /// Word's own step is 720 - half an inch - and that is what this was. The preview
    /// indents a list by 1.6em, which is nearer a quarter of an inch, so a document read
    /// beside its own PDF had every list sitting visibly further in. A quarter inch is close
    /// enough to the preview to stop the eye, and is still a step Word is happy with.
    /// </summary>
    public const int IndentPerLevel = 360;

    private readonly List<LevelShape[]> _abstracts = [];
    private readonly List<Instance> _instances = [];
    private readonly Dictionary<ListBlock, ListPlacement> _placements = [];

    /// <summary>
    /// The heading numbering, once it has been planned: which abstract definition describes it,
    /// which instance the styles point at, and the heading level the count starts from.
    /// </summary>
    private (int AbstractId, int NumberId, int StartLevel)? _headings;

    /// <summary>Whether anything in the document needs a numbering part at all.</summary>
    public bool IsEmpty => _instances.Count == 0 && _headings is null;

    /// <summary>
    /// Sets up section numbering for the heading styles, and returns the instance they should
    /// reference.
    ///
    /// This is what makes the numbers real. Written as text - which is what an earlier version
    /// did, because it is what the preview does - they are correct the moment the file is
    /// written and wrong the moment anybody edits it: insert a section in Word and every
    /// number after it still reads as it did before. A multilevel list linked to the heading
    /// styles is the same thing Word's own "Multilevel List, link to Heading styles" builds,
    /// and Word maintains it from then on.
    ///
    /// The start level is the reader's preference: numbering from heading two means a level-two
    /// heading is the outermost number and a level-one heading carries none at all. It is still
    /// in the list, though, as an unnumbered level: a heading above the start begins a new
    /// section and every level beneath it counts from one again, which is the documented rule
    /// on <see cref="HeadingNumbering"/> and what the preview and the outline do. Left out of
    /// the list, as it once was, a level-one heading restarted nothing, and Word numbered the
    /// level-two headings of a three-chapter document straight through while the PDF of the
    /// same document numbered each chapter from one.
    ///
    /// Word counts for itself from here, which is the trade. A document that skips a level -
    /// a level-three heading directly under a level-one - will read differently from the
    /// preview, because Marqora drops the missing level and Word counts it. That only happens
    /// where the outline genuinely has a gap in it, and a Word document that renumbers
    /// correctly forever is worth more than one that agrees with the preview once.
    /// </summary>
    /// <remarks>
    /// Called after <see cref="Plan"/>, so the lists have taken their ids and the headings can
    /// simply take the next ones.
    /// </remarks>
    public int? PlanHeadings(HeadingNumbering numbering)
    {
        if (numbering == HeadingNumbering.Off)
        {
            return null;
        }

        // The enum's members are the heading levels themselves.
        _headings = (_abstracts.Count, _instances.Count + 1, (int)numbering);

        return _headings.Value.NumberId;
    }

    public void Plan(MarkdigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Only the outermost list of each tree starts a new instance; the rest of the tree
        // shares it so that nesting reads as one list rather than several.
        foreach (ListBlock list in document.Descendants<ListBlock>())
        {
            if (list.Parent is not ListItemBlock && !_placements.ContainsKey(list))
            {
                PlanTree(list);
            }
        }
    }

    public bool TryGet(ListBlock list, out ListPlacement placement) =>
        _placements.TryGetValue(list, out placement);

    /// <summary>
    /// An item carrying a checkbox, which cannot take Word numbering.
    ///
    /// Word defines a list's marker once per level, not per item, so there is no way to say
    /// that this bullet is ticked and the next is not. A task item is written as an indented
    /// paragraph with a literal box character instead - see the inline walker.
    ///
    /// The question is asked of the item rather than of the list, and that is the whole
    /// point. A blank line between two items does not begin a second list, so
    ///
    ///     - Fruit
    ///       - Apple
    ///     - Vegetables
    ///
    ///     - [x] Ship it
    ///
    /// is one list of four items, two of them ticked. Asking the list gave a yes, and that
    /// yes took the bullets off Fruit and Vegetables, flattened Apple up to their level, and
    /// did the same to every list nested underneath - because the walk that hands out
    /// numbering returned at the top instead of stepping over the two items that could not
    /// use it.
    /// </summary>
    public static bool IsTaskItem(ListItemBlock item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return item.FirstOrDefault() is ParagraphBlock { Inline: { } inline }
            && inline.FirstChild is TaskList;
    }

    private void PlanTree(ListBlock root)
    {
        var shapes = new List<LevelShape>();

        Collect(root, depth: 0, shapes);

        if (shapes.Count == 0)
        {
            return;
        }

        int abstractId = InternAbstract([.. shapes]);
        int numberId = _instances.Count + 1;

        _instances.Add(new Instance(numberId, abstractId, [.. shapes]));

        Assign(root, depth: 0, numberId);
    }

    /// <summary>
    /// Records the shape of each depth, and separates out any sublist that disagrees.
    ///
    /// A document can put an alphabetic sublist under one item and a numeric one under the
    /// next. They cannot share a level definition, so the second is given a tree of its own
    /// and indented to look as though it were still nested.
    /// </summary>
    private void Collect(ListBlock list, int depth, List<LevelShape> shapes)
    {
        if (depth > MaximumLevel)
        {
            return;
        }

        LevelShape shape = LevelShape.Of(list, depth);

        if (depth == shapes.Count)
        {
            shapes.Add(shape);
        }
        else if (!shapes[depth].SameKindAs(shape))
        {
            PlanTree(list);
            return;
        }

        foreach (Block child in list)
        {
            if (child is not ListItemBlock item)
            {
                continue;
            }

            foreach (Block grandchild in item)
            {
                if (grandchild is ListBlock nested)
                {
                    Collect(nested, depth + 1, shapes);
                }
            }
        }
    }

    private void Assign(ListBlock list, int depth, int numberId)
    {
        if (depth > MaximumLevel || _placements.ContainsKey(list))
        {
            return;
        }

        _placements[list] = new ListPlacement(numberId, depth, !list.IsLoose);

        foreach (Block child in list)
        {
            if (child is not ListItemBlock item)
            {
                continue;
            }

            foreach (Block grandchild in item)
            {
                if (grandchild is ListBlock nested)
                {
                    Assign(nested, depth + 1, numberId);
                }
            }
        }
    }

    /// <summary>
    /// The index of an abstract definition with this shape, adding one if none matches.
    /// A document of thirty plain bullet lists ends up with one definition and thirty
    /// instances rather than thirty of each.
    /// </summary>
    private int InternAbstract(LevelShape[] shape)
    {
        for (int i = 0; i < _abstracts.Count; i++)
        {
            if (_abstracts[i].Length == shape.Length
                && _abstracts[i].Zip(shape).All(pair => pair.First.SameKindAs(pair.Second)))
            {
                return i;
            }
        }

        _abstracts.Add(shape);

        return _abstracts.Count - 1;
    }

    /// <summary>
    /// Writes the numbering part.
    ///
    /// The order of the children is a schema sequence and an unusual one: every abstract
    /// definition has to come before every instance, not paired with the instance that uses
    /// it. Interleaving them resolves perfectly well and still makes Word call the file
    /// damaged.
    /// </summary>
    /// <param name="markerColor">
    /// The color theme's list-marker color, which the bullets and numbers of every list wear,
    /// as the preview's ::marker does. Heading numbers are not lists and keep their heading's.
    /// </param>
    public void Write(NumberingDefinitionsPart part, string markerColor)
    {
        ArgumentNullException.ThrowIfNull(part);

        var numbering = new Numbering();

        for (int i = 0; i < _abstracts.Count; i++)
        {
            numbering.AppendChild(BuildAbstract(i, _abstracts[i], markerColor));
        }

        if (_headings is { } headings)
        {
            numbering.AppendChild(BuildHeadingAbstract(headings.AbstractId, headings.StartLevel));
        }

        foreach (Instance instance in _instances)
        {
            numbering.AppendChild(BuildInstance(instance));
        }

        if (_headings is { } forStyles)
        {
            numbering.AppendChild(new NumberingInstance(
                new AbstractNumId { Val = forStyles.AbstractId })
            {
                NumberID = forStyles.NumberId,
            });
        }

        part.Numbering = numbering;
    }

    /// <summary>
    /// Section numbering for the heading styles: 1, then 1.1, then 1.1.1, each level naming the
    /// heading style it belongs to.
    ///
    /// Naming the style in the level is what ties the two together. It is how Word's own
    /// "Multilevel List, link to Heading styles" is stored, and it means a paragraph gets its
    /// number by being a Heading rather than by having one typed in front of it - so inserting
    /// a section renumbers everything after it, and deleting one closes the gap.
    ///
    /// The separator is a space rather than a tab, and the levels carry no indent. Word's
    /// default for a numbered list is to hang the text off a tab stop, which is right for a
    /// list and wrong for a heading: it would step every heading in by half an inch and leave
    /// the deeper ones adrift from the margin. A space after the number reads the way the
    /// preview does.
    /// </summary>
    private static AbstractNum BuildHeadingAbstract(int id, int startLevel)
    {
        // No style link of any kind here. The tie to the heading styles is the pStyle inside
        // each level below; numStyleLink means something else entirely - "this definition
        // lives in another numbering style" - and pointing it at a paragraph style would be
        // a claim that is not true.
        var abstractNum = new AbstractNum(
            new Nsid { Val = $"1A2B3CF{id:X1}" },
            new MultiLevelType { Val = MultiLevelValues.Multilevel })
        {
            AbstractNumberId = id,
        };

        // Level n is heading n + 1, always: level zero is Heading 1 whatever the start. The
        // levels above the start are in the list but print nothing - no number and no space
        // after it - so they restart the levels beneath them without showing. Six heading
        // levels exist; the nine a definition must describe run past them, and the extra ones
        // have no style to name.
        for (int level = 0; level <= MaximumLevel; level++)
        {
            int headingLevel = level + 1;
            bool numbered = headingLevel >= startLevel;

            var element = new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = numbered ? NumberFormatValues.Decimal : NumberFormatValues.None })
            {
                LevelIndex = level,
            };

            if (headingLevel <= 6)
            {
                element.AppendChild(new ParagraphStyleIdInLevel { Val = StyleIds.Heading(headingLevel) });
            }

            element.AppendChild(new LevelSuffix
            {
                Val = numbered ? LevelSuffixValues.Space : LevelSuffixValues.Nothing,
            });
            element.AppendChild(new LevelText
            {
                Val = numbered ? CumulativePattern(startLevel, headingLevel) : string.Empty,
            });
            element.AppendChild(new LevelJustification { Val = LevelJustificationValues.Left });
            element.AppendChild(new PreviousParagraphProperties(
                new Indentation { Left = "0", FirstLine = "0" }));

            abstractNum.AppendChild(element);
        }

        return abstractNum;
    }

    /// <summary>
    /// The number a heading shows: the start level's placeholder through its own, so numbering
    /// from heading two gives "%2", then "%2.%3", and so on. A placeholder names a level
    /// one-based, and level n is heading n + 1, so each heading's placeholder is its own level
    /// number - which is the detail that keeps the unnumbered levels above the start out of
    /// the pattern.
    /// </summary>
    private static string CumulativePattern(int startLevel, int headingLevel) =>
        string.Join('.', Enumerable.Range(startLevel, headingLevel - startLevel + 1).Select(n => $"%{n}"));

    private static AbstractNum BuildAbstract(int id, LevelShape[] shapes, string markerColor)
    {
        var abstractNum = new AbstractNum(
            new Nsid { Val = $"1A2B3C{id:X2}" },
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        {
            AbstractNumberId = id,
        };

        // Nine levels always, even when the document only nests three deep: a level Word is
        // not told about is one it invents, and its invention is not this scheme.
        for (int level = 0; level <= MaximumLevel; level++)
        {
            LevelShape shape = level < shapes.Length
                ? shapes[level]
                : shapes[^1].AtDepth(level);

            abstractNum.AppendChild(BuildLevel(level, shape, markerColor));
        }

        return abstractNum;
    }

    /// <summary>
    /// One level of a definition.
    ///
    /// The child order here catches people out: the justification comes <em>after</em> the
    /// text pattern, not before it, which is the opposite of how the two read.
    /// </summary>
    private static Level BuildLevel(int level, LevelShape shape, string markerColor)
    {
        var element = new Level(
            new StartNumberingValue { Val = shape.Start },
            new NumberingFormat { Val = shape.Format },
            new LevelText { Val = shape.Pattern(level) },
            new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(
                new Indentation
                {
                    Left = ((level + 1) * IndentPerLevel).ToString(CultureInfo.InvariantCulture),
                    Hanging = "360",
                }))
        {
            LevelIndex = level,
        };

        // The marker's own run properties: its face, for a bullet that needs one, then its color.
        // w:rPr runs rFonts before color.
        var marker = new NumberingSymbolRunProperties();

        if (shape.MarkerFont is { } font)
        {
            marker.AppendChild(new RunFonts { Ascii = font, HighAnsi = font, Hint = FontTypeHintValues.Default });
        }

        marker.AppendChild(new Color { Val = markerColor });
        element.AppendChild(marker);

        return element;
    }

    private static NumberingInstance BuildInstance(Instance instance)
    {
        var element = new NumberingInstance(
            new AbstractNumId { Val = instance.AbstractId })
        {
            NumberID = instance.NumberId,
        };

        // Every level states its start, one included. Word keeps one count per abstract
        // definition, not per instance: a second instance of a shared definition carries on
        // where the first stopped unless it overrides the start, so the fixture's "Ordered
        // lists" came out 17, 18, 19. Every level rather than only the first, because a list
        // whose opening item is a task item has no numbered paragraph at level 0 to restart
        // the levels below it. A list that starts at five - "5." in the source - is the same
        // override with a different number, which is what keeps the definitions shareable.
        for (int level = 0; level < instance.Shapes.Length; level++)
        {
            element.AppendChild(new LevelOverride(
                new StartOverrideNumberingValue { Val = instance.Shapes[level].Start })
            {
                LevelIndex = level,
            });
        }

        return element;
    }

    private sealed record Instance(int NumberId, int AbstractId, LevelShape[] Shapes);

    /// <summary>What one level of a list looks like: its format, its punctuation and its start.</summary>
    private readonly record struct LevelShape(
        NumberFormatValues Format,
        char Delimiter,
        int Start,
        string? MarkerFont,
        bool Ordered)
    {
        /// <summary>
        /// Word's bullet cycle: a filled round, a hollow round, a filled square, repeating.
        /// The glyphs are private-use code points in Symbol and Wingdings, which is how Word
        /// itself writes them.
        /// </summary>
        private static readonly (string Glyph, string Font)[] Bullets =
        [
            ("", "Symbol"),
            ("o", "Courier New"),
            ("", "Wingdings"),
        ];

        public static LevelShape Of(ListBlock list, int depth)
        {
            if (!list.IsOrdered)
            {
                (string glyph, string font) = Bullets[depth % Bullets.Length];

                return new LevelShape(NumberFormatValues.Bullet, glyph[0], 1, font, Ordered: false);
            }

            return new LevelShape(
                FormatFor(list.BulletType),
                list.OrderedDelimiter == ')' ? ')' : '.',
                StartFor(list),
                MarkerFont: null,
                Ordered: true);
        }

        /// <summary>
        /// The shape a level deeper than the document actually goes, so the definition can
        /// still describe all nine.
        /// </summary>
        public LevelShape AtDepth(int depth) =>
            Ordered ? this with { Start = 1 } : Of(depth);

        private static LevelShape Of(int depth)
        {
            (string glyph, string font) = Bullets[depth % Bullets.Length];

            return new LevelShape(NumberFormatValues.Bullet, glyph[0], 1, font, Ordered: false);
        }

        /// <summary>
        /// Whether two levels can share a definition. The start is deliberately not part of
        /// it: that is what the per-instance override exists for.
        /// </summary>
        public bool SameKindAs(LevelShape other) =>
            Format.Equals(other.Format)
            && Delimiter == other.Delimiter
            && Ordered == other.Ordered
            && MarkerFont == other.MarkerFont;

        /// <summary>
        /// What Word prints in front of an item. The placeholder is one-based on the level,
        /// so level zero is "%1"; a bullet has no placeholder at all, only its glyph.
        /// </summary>
        public string Pattern(int level) =>
            Ordered
                ? $"%{level + 1}{Delimiter}"
                : Delimiter.ToString();

        /// <summary>
        /// Markdig reports the alphabetic and roman lists from the ListExtra extension with
        /// the letter in BulletType and the ordinal in OrderedStart, so one switch covers
        /// every ordered kind.
        /// </summary>
        private static NumberFormatValues FormatFor(char bulletType) => bulletType switch
        {
            'a' => NumberFormatValues.LowerLetter,
            'A' => NumberFormatValues.UpperLetter,
            'i' => NumberFormatValues.LowerRoman,
            'I' => NumberFormatValues.UpperRoman,
            _ => NumberFormatValues.Decimal,
        };

        private static int StartFor(ListBlock list) =>
            int.TryParse(
                list.OrderedStart,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int start) && start > 0
                ? start
                : 1;
    }
}
