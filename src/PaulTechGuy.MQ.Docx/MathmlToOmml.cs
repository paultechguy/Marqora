// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using M = DocumentFormat.OpenXml.Math;

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
        ["‖"] = "‖",
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
    /// </summary>
    private static IEnumerable<XElement> Unwrap(XElement element)
    {
        foreach (XElement child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "semantics":
                case "mrow":
                case "mstyle":
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

        if (first.Name.LocalName != "mo" || last.Name.LocalName != "mo")
        {
            return null;
        }

        string open = first.Value.Trim();
        string close = last.Value.Trim();

        if (!Fences.TryGetValue(open, out string? expected)
            || expected != close
            || (string?)first.Attribute("stretchy") == "false")
        {
            return null;
        }

        var inner = new M.Base();

        foreach (XElement node in nodes[1..^1])
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

            // TeX's spacing classes. OMML does its own spacing, so carrying these across
            // would double it.
            case "mspace":
                return [];

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
                return [new M.Phantom(new M.PhantomProperties(), Argument<M.Base>(element))];

            case "mrow":
            case "mstyle":
            case "semantics":
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
    /// One argument of a construct - a numerator, a base, a superscript - built from a single
    /// MathML child.
    /// </summary>
    private static T Argument<T>(XElement source)
        where T : OpenXmlCompositeElement, new()
    {
        var argument = new T();

        foreach (OpenXmlElement element in Children(source))
        {
            argument.AppendChild(element);
        }

        return argument;
    }

    private static T ArgumentOf<T>(XElement node)
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
            ArgumentOf<M.Numerator>(parts[0]),
            ArgumentOf<M.Denominator>(parts[1]));
    }

    /// <summary>
    /// A square root. The degree element has to be there and has to be empty, with a flag
    /// saying so - an absent degree is not the same as a hidden one.
    /// </summary>
    private static M.Radical SquareRoot(XElement element) => new(
        new M.RadicalProperties(new M.HideDegree { Val = M.BooleanValues.One }, new M.ControlProperties()),
        new M.Degree(),
        Argument<M.Base>(element));

    /// <summary>
    /// An nth root. MathML puts the radicand first and the index second; OMML writes the index
    /// first, so the two are exchanged here rather than anywhere subtler.
    /// </summary>
    private static M.Radical Root(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        return new M.Radical(
            new M.RadicalProperties(new M.HideDegree { Val = M.BooleanValues.Zero }, new M.ControlProperties()),
            ArgumentOf<M.Degree>(parts[1]),
            ArgumentOf<M.Base>(parts[0]));
    }

    private static M.Subscript Sub(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        return new M.Subscript(
            new M.SubscriptProperties(new M.ControlProperties()),
            ArgumentOf<M.Base>(parts[0]),
            ArgumentOf<M.SubArgument>(parts[1]));
    }

    private static M.Superscript Sup(XElement element)
    {
        XElement[] parts = Parts(element, 2);

        return new M.Superscript(
            new M.SuperscriptProperties(new M.ControlProperties()),
            ArgumentOf<M.Base>(parts[0]),
            ArgumentOf<M.SuperArgument>(parts[1]));
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
            ArgumentOf<M.Base>(parts[0]),
            ArgumentOf<M.SubArgument>(parts[1]),
            ArgumentOf<M.SuperArgument>(parts[2]));
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
                ArgumentOf<M.Base>(parts[0]));
        }

        return new M.LimitLower(
            new M.LimitLowerProperties(new M.ControlProperties()),
            ArgumentOf<M.Base>(parts[0]),
            ArgumentOf<M.Limit>(parts[1]));
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
                ArgumentOf<M.Base>(parts[0]));
        }

        return new M.LimitUpper(
            new M.LimitUpperProperties(new M.ControlProperties()),
            ArgumentOf<M.Base>(parts[0]),
            ArgumentOf<M.Limit>(parts[1]));
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
                    ArgumentOf<M.Base>(parts[0]),
                    ArgumentOf<M.Limit>(parts[1]))),
            ArgumentOf<M.Limit>(parts[2]));
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
            ArgumentOf<M.SubArgument>(lower),
            ArgumentOf<M.SuperArgument>(upper),
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
                        new M.MatrixColumnCount { Val = columns },
                        new M.MatrixColumnJustification
                        {
                            Val = M.HorizontalAlignmentValues.Center,
                        })),
                new M.ControlProperties()));

        foreach (XElement row in rows)
        {
            var matrixRow = new M.MatrixRow();

            foreach (XElement cell in row.Elements().Where(e => e.Name.LocalName == "mtd"))
            {
                matrixRow.AppendChild(ArgumentOf<M.Base>(cell));
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
