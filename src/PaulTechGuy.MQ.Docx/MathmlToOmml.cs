// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using M = DocumentFormat.OpenXml.Math;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Turns the MathML KaTeX produced into the equations Word understands.
///
/// Word does not read MathML. Its equations are OMML, a different vocabulary with the same
/// job, and the difference between converting and not is the difference between an equation a
/// reader can edit, select, search and restyle, and a picture of one.
///
/// KaTeX is asked for nothing special: with no output option set it emits both its own layout
/// and a MathML copy of the same expression, and the MathML copy is what this reads. Three of
/// its habits matter. Everything arrives wrapped in <c>semantics</c> with an
/// <c>annotation</c> holding the original TeX, so there are two layers to unwrap and one child
/// to ignore. An identifier of more than one character is upright rather than italic, and
/// KaTeX does not always say so. And a bracket is a pair of stretchy operators inside a row
/// rather than a fenced element, so the pair has to be recognized or every parenthesis comes
/// out at the height of a full stop next to a fraction that is three lines tall.
///
/// What it cannot map, it declines to map: <see cref="Convert"/> returns null and the caller
/// writes the TeX source instead. The TeX comes from the parsed document rather than from the
/// annotation KaTeX supplies, and that is on purpose - the tree always has it, including when
/// there is no preview to have asked, which is the case the fallback most needs to cover.
/// </summary>
internal static class MathmlToOmml
{
    /// <summary>
    /// The operators that take limits above and below rather than as scripts - a sum, a
    /// product, an integral. Word models these as one element with its own properties, which
    /// is what makes an integral's bounds sit where a reader expects rather than beside it.
    /// </summary>
    private static readonly HashSet<string> BigOperators =
    [
        "∑", "∏", "∐", "∫", "∬", "∭", "∮", "∯", "∰", "⋃", "⋂", "⋁", "⋀", "⨁", "⨂", "⨀",
    ];

    /// <summary>Operators that end an n-ary's expression rather than belonging to it.</summary>
    private static readonly HashSet<string> Relations =
    [
        "=", "≠", "<", ">", "≤", "≥", "≈", "≡", "∼", "≃", "≅", "∝",
        "→", "←", "↔", "⇒", "⇐", "⇔", "∈", "∉", "⊂", "⊆", "⊃", "⊇",
    ];

    /// <summary>Brackets that should grow to fit what they surround.</summary>
    private static readonly Dictionary<string, string> Fences = new(StringComparer.Ordinal)
    {
        ["("] = ")",
        ["["] = "]",
        ["{"] = "}",
        ["|"] = "|",
        ["∣"] = "∣",
        ["‖"] = "‖",
        ["∥"] = "∥",
        ["⟨"] = "⟩",
        ["⌈"] = "⌉",
        ["⌊"] = "⌋",
    };

    /// <summary>
    /// The equation, or null when something in it has no Word equivalent.
    /// </summary>
    /// <param name="unsupported">
    /// The MathML element that stopped the conversion, when one did.
    ///
    /// Worth handing back rather than swallowing. No converter covers everything TeX can
    /// express, so the way this one improves is by learning which constructs real documents
    /// use - and that only happens if a miss says what it was instead of quietly becoming a
    /// line of source.
    /// </param>
    public static M.OfficeMath? Convert(string? mathml, out string? unsupported)
    {
        unsupported = null;

        if (string.IsNullOrWhiteSpace(mathml))
        {
            return null;
        }

        try
        {
            XElement root = Parse(mathml);
            var math = new M.OfficeMath();

            // The same path an argument takes, so that an expression wrapped in brackets is
            // recognized as bracketed at the top level too and not only when nested.
            foreach (OpenXmlElement element in Children(root))
            {
                math.AppendChild(element);
            }

            return math.HasChildren ? math : null;
        }
        catch (NotSupportedException ex)
        {
            unsupported = ex.Message;
            return null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the slice out of the preview's markup.
    ///
    /// The one entity worth naming is the non-breaking space: the browser's serializer writes
    /// it as a name XML does not define, so it would make the parse fail on the very equations
    /// that use spacing. The other four it writes are XML's own and need nothing.
    /// </summary>
    private static XElement Parse(string mathml) =>
        XElement.Parse(
            mathml.Replace("&nbsp;", " ", StringComparison.Ordinal),
            LoadOptions.None);

    /// <summary>
    /// Steps past the wrappers KaTeX puts round everything, and past the annotation that
    /// records the TeX - which is metadata about the equation rather than part of it, and
    /// would otherwise be written into the document as text.
    ///
    /// <c>mpadded</c> is in that list for the same reason <c>mspace</c> is dropped: it says
    /// nothing about what the expression means, only how much room to leave round it or how
    /// far to shift it, and OMML does its own spacing. It is what KaTeX reaches for on
    /// <c>\raisebox</c>, <c>\smash</c> and the overlaps inside an mhchem reaction - so before
    /// it was stepped past, one padded arrow declined an entire equation to its TeX source.
    /// </summary>
    private static IEnumerable<XElement> Unwrap(XElement element)
    {
        foreach (XElement child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                // A \left ... \right group stays whole. Flattened into the run around it, its
                // brackets were no longer the ends of anything, so Fence never saw a pair:
                // "A = (matrix)" wrote the parentheses one line tall beside a three-line
                // matrix, and the cases brace came out a single short "{".
                case "mrow" when IsFenced(child):
                    yield return child;
                    break;

                // \color and \colorbox stay whole too, so their color reaches what they hold
                // (Painted). Stepped through like any other style, the color was lost.
                case "mstyle" or "mpadded" when IsColored(child):
                    yield return child;
                    break;

                case "semantics":
                case "mrow":
                case "mstyle":
                case "mpadded":
                    foreach (XElement inner in Unwrap(child))
                    {
                        yield return inner;
                    }

                    break;

                case "annotation":
                case "annotation-xml":
                    break;

                default:
                    yield return child;
                    break;
            }
        }
    }

    /// <summary>
    /// The children of an element as OMML, with the transparent wrappers stepped past and any
    /// bracket pair folded into a delimiter.
    /// </summary>
    private static List<OpenXmlElement> Children(XElement element)
    {
        List<XElement> nodes = [.. Unwrap(element)];

        if (Fence(nodes) is { } delimiter)
        {
            return [delimiter];
        }

        var result = new List<OpenXmlElement>();

        for (int i = 0; i < nodes.Count; i++)
        {
            List<OpenXmlElement> converted = Node(nodes[i]);

            // An n-ary built from a sub-superscripted operator has an empty base, because
            // the markup put the expression beside it rather than inside it. Fill it from
            // what follows.
            if (converted is [M.Nary nary]
                && nary.GetFirstChild<M.Base>() is { HasChildren: false } body)
            {
                i = Integrand(nodes, i + 1, body) - 1;
            }

            result.AddRange(converted);
        }

        return result;
    }

    /// <summary>
    /// Fills an n-ary operator's base with the expression it applies to, and says where that
    /// expression ended.
    ///
    /// MathML does not group an integrand with its integral. KaTeX writes
    /// <c>int_0^infty e^{-x^2},dx</c> as a sub-superscripted operator followed by four
    /// unrelated siblings, so the n-ary built from that operator has nothing to put in its
    /// base - and Word draws an empty base as a dotted box. That box is what a reader saw
    /// sitting between the integral sign and its own integrand.
    ///
    /// Where the expression ends has to be guessed, because nothing in the markup says. Two
    /// rules between them cover nearly everything anyone writes:
    ///
    /// A differential ends it and belongs to it - <c>d</c>, then whatever the integration is
    /// with respect to. That is what keeps <c>int f,dx + C</c> from swallowing the
    /// constant of integration.
    ///
    /// A relation ends it and does not belong to it. Everything after the <c>=</c> in
    /// <c>int_0^infty e^{-x^2},dx = rac{sqrtpi}{2}</c> is a statement about the
    /// integral rather than a part of it, and the same holds for a sum that equals something.
    ///
    /// Failing both, the rest of the row is the expression, which is what <c>sum a_i</c>
    /// wants. An operator with nothing after it at all keeps its empty base, and Word draws
    /// the box - which is the right answer, because there really is nothing there.
    /// </summary>
    private static int Integrand(List<XElement> nodes, int from, M.Base body)
    {
        int end = nodes.Count;

        for (int i = from; i < nodes.Count; i++)
        {
            XElement node = nodes[i];
            string text = node.Value.Trim();

            if (node.Name.LocalName == "mo" && Relations.Contains(text))
            {
                end = i;
                break;
            }

            if (node.Name.LocalName == "mi"
                && text == "d"
                && i + 1 < nodes.Count
                && nodes[i + 1].Name.LocalName == "mi")
            {
                end = i + 2;
                break;
            }
        }

        for (int i = from; i < end; i++)
        {
            foreach (OpenXmlElement part in Node(nodes[i]))
            {
                body.AppendChild(part);
            }
        }

        return end;
    }

    /// <summary>
    /// A bracket pair, when the run begins and ends with matching stretchy operators.
    ///
    /// This is what makes a parenthesis grow around a fraction. KaTeX writes
    /// <c>\left( x \right)</c> as two ordinary operators either side of the contents, and
    /// written out as operators they stay one line tall whatever they surround. An operator
    /// that says <c>stretchy="false"</c> is left alone - that is the difference between
    /// <c>\left(</c> and <c>\lparen</c>, and the author meant it.
    /// </summary>
    private static M.Delimiter? Fence(List<XElement> nodes)
    {
        if (nodes.Count < 2)
        {
            return null;
        }

        XElement first = nodes[0];
        XElement last = nodes[^1];

        if (first.Name.LocalName != "mo")
        {
            return null;
        }

        string open = first.Value.Trim();
        string close;
        int end;

        if (IsFence(first))
        {
            // KaTeX's own \left ... \right: the pair is whatever the author wrote, matched or
            // not - \left( ... \right] is legal - and an empty or missing close is \right.,
            // which is how cases opens a brace and closes nothing.
            bool closed = IsFence(last);

            close = closed ? last.Value.Trim() : string.Empty;
            end = closed ? nodes.Count - 1 : nodes.Count;
        }
        else
        {
            if (last.Name.LocalName != "mo")
            {
                return null;
            }

            close = last.Value.Trim();
            end = nodes.Count - 1;

            if (!Fences.TryGetValue(open, out string? expected)
                || expected != close
                || (string?)first.Attribute("stretchy") == "false")
            {
                return null;
            }
        }

        var inner = new M.Base();

        foreach (XElement node in nodes[1..end])
        {
            foreach (OpenXmlElement element in Node(node))
            {
                inner.AppendChild(element);
            }
        }

        return new M.Delimiter(
            new M.DelimiterProperties(
                new M.BeginChar { Val = open },
                new M.EndChar { Val = close },
                new M.ControlProperties()),
            inner);
    }

    /// <summary>A style that carries \color (mathcolor) or \colorbox (mathbackground).</summary>
    private static bool IsColored(XElement element) =>
        element.Attribute("mathcolor") is not null || element.Attribute("mathbackground") is not null;

    /// <summary>
    /// Every run in what a \color or \colorbox held, in its color or on its fill.
    ///
    /// The innermost color wins, as it does in TeX: the inner style is converted first and
    /// paints its runs, and an outer one leaves a run that already has a color alone. A color
    /// this cannot name in hex is left off rather than guessed - the run keeps the document's
    /// ink, which is what a reader would see if the color had not been there.
    /// </summary>
    private static List<OpenXmlElement> Painted(List<OpenXmlElement> parts, XElement style)
    {
        string? ink = HexOf((string?)style.Attribute("mathcolor"));
        string? fill = HexOf((string?)style.Attribute("mathbackground"));

        if (ink is null && fill is null)
        {
            return parts;
        }

        IEnumerable<M.Run> runs = parts.SelectMany(part =>
            part is M.Run run ? [run] : part.Descendants<M.Run>());

        foreach (M.Run run in runs)
        {
            // In m:r the Word run properties follow the math ones and precede the text.
            W.RunProperties properties = run.GetFirstChild<W.RunProperties>()
                ?? run.InsertAfter(new W.RunProperties(), run.GetFirstChild<M.RunProperties>())
                ?? run.PrependChild(new W.RunProperties());

            if (ink is not null && properties.Color is null)
            {
                properties.Color = new W.Color { Val = ink };
            }

            if (fill is not null && properties.Shading is null)
            {
                properties.Shading = new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = fill };
            }
        }

        return parts;
    }

    /// <summary>
    /// A color as KaTeX hands it on - a hex value, or a name it passed to the browser - as the
    /// six hex digits Word writes. The names are CSS's values for them, since that is what
    /// the preview drew: CSS green is #008000, not xcolor's #00FF00.
    /// </summary>
    private static string? HexOf(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        string value = color.Trim();

        if (value.StartsWith('#'))
        {
            string digits = value[1..];

            return digits.Length switch
            {
                3 => string.Concat(digits.Select(c => new string(c, 2))).ToUpperInvariant(),
                6 => digits.ToUpperInvariant(),
                _ => null,
            };
        }

        return NamedColors.GetValueOrDefault(value);
    }

    /// <summary>The CSS names xcolor's base set shares, plus the common extras.</summary>
    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "000000",
        ["white"] = "FFFFFF",
        ["red"] = "FF0000",
        ["green"] = "008000",
        ["blue"] = "0000FF",
        ["cyan"] = "00FFFF",
        ["magenta"] = "FF00FF",
        ["yellow"] = "FFFF00",
        ["gray"] = "808080",
        ["darkgray"] = "A9A9A9",
        ["lightgray"] = "D3D3D3",
        ["brown"] = "A52A2A",
        ["lime"] = "00FF00",
        ["olive"] = "808000",
        ["orange"] = "FFA500",
        ["pink"] = "FFC0CB",
        ["purple"] = "800080",
        ["teal"] = "008080",
        ["violet"] = "EE82EE",
        ["navy"] = "000080",
        ["maroon"] = "800000",
        ["silver"] = "C0C0C0",
        ["gold"] = "FFD700",
    };

    /// <summary>An operator KaTeX marks as one end of a \left ... \right pair.</summary>
    private static bool IsFence(XElement node) =>
        node.Name.LocalName == "mo" && (string?)node.Attribute("fence") == "true";

    /// <summary>A row that opens with a \left: KaTeX's whole delimited group.</summary>
    private static bool IsFenced(XElement row) =>
        row.Elements().FirstOrDefault() is { } first && IsFence(first);

    /// <summary>
    /// One MathML element as OMML. Several, in the case of a wrapper that has no equivalent
    /// and simply contributes its contents.
    /// </summary>
    private static List<OpenXmlElement> Node(XElement element)
    {
        switch (element.Name.LocalName)
        {
            case "mi":
                return [Identifier(element)];

            case "mn":
                return [Literal(element.Value, upright: true)];

            case "mo":
                return [Literal(element.Value, upright: true)];

            case "mtext":
                return [NormalText(element.Value)];

            case "mspace":
                return Space(element);

            case "mfrac":
                return [Fraction(element)];

            case "msqrt":
                return [SquareRoot(element)];

            case "mroot":
                return [Root(element)];

            case "msub":
                return [Sub(element)];

            case "msup":
                return [Sup(element)];

            case "msubsup":
                return [SubSup(element)];

            case "munder":
                return [Under(element)];

            case "mover":
                return [Over(element)];

            case "munderover":
                return [UnderOver(element)];

            case "mtable":
                return [Table(element)];

            case "mphantom":
                return [new M.Phantom(new M.PhantomProperties(), ContentsAs<M.Base>(element))];

            case "mstyle" or "mpadded" when IsColored(element):
                return Painted(Children(element), element);

            // Spacing and position only - see Unwrap for why mpadded is one of these.
            case "mrow":
            case "mstyle":
            case "semantics":
            case "mpadded":
                return Children(element);

            case "annotation":
            case "annotation-xml":
                return [];

            default:
                // Something this does not know. Declining here is what sends the whole
                // equation to the TeX fallback rather than writing a document with a hole in
                // the middle of an expression.
                throw new NotSupportedException(element.Name.LocalName);
        }
    }

    /// <summary>
    /// An identifier, italic or upright.
    ///
    /// A single letter is a variable and is italic, which is OMML's default and needs saying
    /// only when it is wrong. More than one letter is a function name - sin, log, lim - and is
    /// upright. KaTeX does not always mark those with a variant, so the length decides.
    /// </summary>
    private static M.Run Identifier(XElement element)
    {
        string text = element.Value;
        string? variant = (string?)element.Attribute("mathvariant");

        bool upright = variant switch
        {
            "normal" => true,
            "italic" or "bold-italic" => false,
            _ => text.Length > 1,
        };

        return Literal(text, upright, Bold(variant), Script(variant));
    }

    private static M.Run Literal(
        string text,
        bool upright,
        bool bold = false,
        string? script = null)
    {
        var run = new M.Run();

        if (upright || bold || script is not null)
        {
            // The order is the schema's: script before style, and both after the literal and
            // normal-text flags.
            var properties = new M.RunProperties();

            if (script is not null)
            {
                properties.AppendChild(new M.Script { Val = ScriptValue(script) });
            }

            properties.AppendChild(new M.Style
            {
                Val = (upright, bold) switch
                {
                    (true, true) => M.StyleValues.Bold,
                    (true, false) => M.StyleValues.Plain,
                    (false, true) => M.StyleValues.BoldItalic,
                    _ => M.StyleValues.Italic,
                },
            });

            run.AppendChild(properties);
        }

        run.AppendChild(new M.Text(XmlSafeText.Clean(text)));

        return run;
    }

    /// <summary>
    /// Text that is prose rather than mathematics - what <c>\text{...}</c> produces. The
    /// normal-text flag is what stops Word setting it in the italic math face.
    /// </summary>
    /// <summary>
    /// A space the author asked for.
    ///
    /// Below half an em it is TeX's spacing between symbols - <c>\,</c>, <c>\;</c> - which
    /// OMML does for itself, so carrying it across would double it. From half an em up it is
    /// a gap the author put there: <c>\quad</c> and <c>\qquad</c> between equations on one
    /// line. Dropped, those ran "= e ∏ k = n! ⋃ Aᵢ" together into one expression. Word keeps
    /// em and en spaces in an equation where it would discard an ordinary one.
    /// </summary>
    private static List<OpenXmlElement> Space(XElement element)
    {
        string width = ((string?)element.Attribute("width") ?? string.Empty).Trim();

        if (!width.EndsWith("em", StringComparison.Ordinal)
            || !double.TryParse(width[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out double ems)
            || ems < 0.5)
        {
            return [];
        }

        int whole = (int)Math.Floor(ems);
        string gap = new string('\u2003', whole) + (ems - whole >= 0.5 ? "\u2002" : string.Empty);

        return [NormalText(gap)];
    }

    private static M.Run NormalText(string text) => new(
        new M.RunProperties(new M.NormalText()),
        new M.Text(XmlSafeText.Clean(text)) { Space = SpaceProcessingModeValues.Preserve });

    private static bool Bold(string? variant) =>
        variant is "bold" or "bold-italic" or "bold-script" or "bold-fraktur";

    private static string? Script(string? variant) => variant switch
    {
        "double-struck" => "double-struck",
        "script" or "bold-script" => "script",
        "fraktur" or "bold-fraktur" => "fraktur",
        "sans-serif" => "sans-serif",
        "monospace" => "monospace",
        _ => null,
    };

    private static M.ScriptValues ScriptValue(string script) => script switch
    {
        "double-struck" => M.ScriptValues.DoubleStruck,
        "script" => M.ScriptValues.Script,
        "fraktur" => M.ScriptValues.Fraktur,
        "sans-serif" => M.ScriptValues.SansSerif,
        _ => M.ScriptValues.Monospace,
    };

    /// <summary>
    /// An argument built from what is <b>inside</b> an element, the element itself
    /// contributing nothing but its containment: the cell of a matrix, the body of a phantom.
    ///
    /// The pair with <see cref="NodeAs{T}"/> is the sharpest edge in this file. They differ by
    /// one word at the call site and by everything in what they mean, and picking the wrong
    /// one is silent: a matrix cell handed to <see cref="NodeAs{T}"/> asks for an
    /// <c>mtd</c> to be converted, nothing converts an <c>mtd</c>, and the whole equation
    /// declines to its TeX source. That is what happened to every matrix, aligned system and
    /// set of cases until a test finally covered one.
    ///
    /// The rule: a <b>container</b> - <c>mtd</c>, <c>mphantom</c>, anything whose children are
    /// the expression - takes this one. A <b>node</b> that is itself the expression takes the
    /// other.
    /// </summary>
    private static T ContentsAs<T>(XElement source)
        where T : OpenXmlCompositeElement, new()
    {
        var argument = new T();

        foreach (OpenXmlElement element in Children(source))
        {
            argument.AppendChild(element);
        }

        return argument;
    }

    /// <summary>
    /// An argument built from the element <b>itself</b>: a numerator, a base, a superscript,
    /// each of which is one MathML node to convert rather than a wrapper to open. See
    /// <see cref="ContentsAs{T}"/> for the difference and what mixing them up costs.
    /// </summary>
    private static T NodeAs<T>(XElement node)
        where T : OpenXmlCompositeElement, new()
    {
        var argument = new T();

        foreach (OpenXmlElement element in Node(node))
        {
            argument.AppendChild(element);
        }

        return argument;
    }

    /// <summary>
    /// The children of an element, each as its own argument. Every construct below takes a
    /// fixed number of them, and a MathML element with the wrong count is malformed.
    /// </summary>
    private static XElement[] Parts(XElement element, int expected)
    {
        XElement[] parts = [.. element.Elements()];

        return parts.Length == expected
            ? parts
            : throw new NotSupportedException($"{element.Name.LocalName} has {parts.Length} parts");
    }

    private static M.Fraction Fraction(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        var properties = new M.FractionProperties();

        // A fraction with no rule is how TeX writes a binomial coefficient.
        if ((string?)element.Attribute("linethickness") is "0" or "0px" or "0em")
        {
            properties.AppendChild(new M.FractionType { Val = M.FractionTypeValues.NoBar });
        }

        properties.AppendChild(new M.ControlProperties());

        return new M.Fraction(
            properties,
            NodeAs<M.Numerator>(parts[0]),
            NodeAs<M.Denominator>(parts[1]));
    }

    /// <summary>
    /// A square root. The degree element has to be there and has to be empty, with a flag
    /// saying so - an absent degree is not the same as a hidden one.
    /// </summary>
    private static M.Radical SquareRoot(XElement element) => new(
        new M.RadicalProperties(new M.HideDegree { Val = M.BooleanValues.One }, new M.ControlProperties()),
        new M.Degree(),
        ContentsAs<M.Base>(element));

    /// <summary>
    /// An nth root. MathML puts the radicand first and the index second; OMML writes the index
    /// first, so the two are exchanged here rather than anywhere subtler.
    /// </summary>
    private static M.Radical Root(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        return new M.Radical(
            new M.RadicalProperties(new M.HideDegree { Val = M.BooleanValues.Zero }, new M.ControlProperties()),
            NodeAs<M.Degree>(parts[1]),
            NodeAs<M.Base>(parts[0]));
    }

    private static M.Subscript Sub(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        return new M.Subscript(
            new M.SubscriptProperties(new M.ControlProperties()),
            NodeAs<M.Base>(parts[0]),
            NodeAs<M.SubArgument>(parts[1]));
    }

    private static M.Superscript Sup(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        return new M.Superscript(
            new M.SuperscriptProperties(new M.ControlProperties()),
            NodeAs<M.Base>(parts[0]),
            NodeAs<M.SuperArgument>(parts[1]));
    }

    private static OpenXmlElement SubSup(XElement element)
    {
        XElement[] parts = Parts(element, 3);

        if (Nary(parts[0], parts[1], parts[2], M.LimitLocationValues.SubscriptSuperscript) is { } nary)
        {
            return nary;
        }

        return new M.SubSuperscript(
            new M.SubSuperscriptProperties(new M.ControlProperties()),
            NodeAs<M.Base>(parts[0]),
            NodeAs<M.SubArgument>(parts[1]),
            NodeAs<M.SuperArgument>(parts[2]));
    }

    /// <summary>
    /// Something written under something else: a limit under "lim", or an accent under a
    /// letter. The accent case is the one with its own element in OMML.
    /// </summary>
    private static OpenXmlElement Under(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        if ((string?)element.Attribute("accentunder") == "true")
        {
            return new M.GroupChar(
                new M.GroupCharProperties(
                    new M.AccentChar { Val = parts[1].Value.Trim() },
                    new M.Position { Val = M.VerticalJustificationValues.Bottom },
                    new M.ControlProperties()),
                NodeAs<M.Base>(parts[0]));
        }

        return new M.LimitLower(
            new M.LimitLowerProperties(new M.ControlProperties()),
            NodeAs<M.Base>(parts[0]),
            NodeAs<M.Limit>(parts[1]));
    }

    /// <summary>
    /// Something written over something else. A hat, a bar or a vector arrow is an accent -
    /// one element in OMML, with the mark as an attribute rather than as content.
    /// </summary>
    private static OpenXmlElement Over(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        if ((string?)element.Attribute("accent") == "true")
        {
            return new M.Accent(
                new M.AccentProperties(
                    new M.AccentChar { Val = parts[1].Value.Trim() },
                    new M.ControlProperties()),
                NodeAs<M.Base>(parts[0]));
        }

        return new M.LimitUpper(
            new M.LimitUpperProperties(new M.ControlProperties()),
            NodeAs<M.Base>(parts[0]),
            NodeAs<M.Limit>(parts[1]));
    }

    private static OpenXmlElement UnderOver(XElement element)
    {
        XElement[] parts = Parts(element, 3);

        if (Nary(parts[0], parts[1], parts[2], M.LimitLocationValues.UnderOver) is { } nary)
        {
            return nary;
        }

        // Not an operator that takes limits, so it is one thing under another under a third.
        return new M.LimitUpper(
            new M.LimitUpperProperties(new M.ControlProperties()),
            new M.Base(
                new M.LimitLower(
                    new M.LimitLowerProperties(new M.ControlProperties()),
                    NodeAs<M.Base>(parts[0]),
                    NodeAs<M.Limit>(parts[1]))),
            NodeAs<M.Limit>(parts[2]));
    }

    /// <summary>
    /// A sum, product or integral with its bounds, when the base is one of those operators.
    ///
    /// This is the difference between an integral sign with a small number beside it and an
    /// integral Word knows is an integral - which is what makes the bounds sit in the right
    /// place and grow with the expression.
    /// </summary>
    private static M.Nary? Nary(
        XElement baseNode,
        XElement lower,
        XElement upper,
        M.LimitLocationValues location)
    {
        string symbol = baseNode.Value.Trim();

        if (baseNode.Name.LocalName != "mo" || !BigOperators.Contains(symbol))
        {
            return null;
        }

        return new M.Nary(
            new M.NaryProperties(
                new M.AccentChar { Val = symbol },
                new M.LimitLocation { Val = location },
                new M.HideSubArgument { Val = M.BooleanValues.Zero },
                new M.HideSuperArgument { Val = M.BooleanValues.Zero },
                new M.ControlProperties()),
            NodeAs<M.SubArgument>(lower),
            NodeAs<M.SuperArgument>(upper),
            new M.Base());
    }

    /// <summary>
    /// A matrix, which is also how a set of cases and an aligned system arrive.
    /// </summary>
    private static M.Matrix Table(XElement element)
    {
        List<XElement> rows = [.. element.Elements().Where(e => e.Name.LocalName == "mtr")];

        int columns = rows.Count == 0
            ? 0
            : rows.Max(r => r.Elements().Count(e => e.Name.LocalName == "mtd"));

        if (columns == 0)
        {
            throw new NotSupportedException("an empty table");
        }

        var matrix = new M.Matrix(
            new M.MatrixProperties(
                new M.MatrixColumns(
                    new M.MatrixColumn(
                        // The count and the justification belong to the column's own
                        // properties element, not to the column. Word answers the flatter
                        // shape the way it answers every other schema mistake - by offering
                        // to repair the file - and no matrix had ever reached a document to
                        // find out.
                        new M.MatrixColumnProperties(
                            new M.MatrixColumnCount { Val = columns },
                            new M.MatrixColumnJustification
                            {
                                Val = M.HorizontalAlignmentValues.Center,
                            }))),
                new M.ControlProperties()));

        foreach (XElement row in rows)
        {
            var matrixRow = new M.MatrixRow();

            foreach (XElement cell in row.Elements().Where(e => e.Name.LocalName == "mtd"))
            {
                // The cell's contents, not the cell: an mtd is a container and nothing
                // converts one. See ContentsAs.
                matrixRow.AppendChild(ContentsAs<M.Base>(cell));
            }

            // A short row would leave the matrix ragged, and Word expects every row to be the
            // width the properties claim.
            for (int i = matrixRow.ChildElements.Count; i < columns; i++)
            {
                matrixRow.AppendChild(new M.Base());
            }

            matrix.AppendChild(matrixRow);
        }

        return matrix;
    }
}
