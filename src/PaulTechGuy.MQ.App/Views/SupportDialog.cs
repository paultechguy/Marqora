// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Help, Support the Project.
///
/// Built in code like the About box and the shortcuts list, and for the same reason: fixed
/// text with no bindings.
///
/// The dialog says its piece and offers two ways out to GitHub. It does not know or ask
/// whether anyone took them: nothing here is recorded, and the About box's claim that
/// Marqora makes no network calls and carries no telemetry has to stay true.
///
/// Which button was pressed is reported through <see cref="ContentDialogResult"/> rather
/// than handled here, so the launching - and the message when a launch fails - lives with
/// the window that has the logger and the dialog service.
/// </summary>
internal sealed class SupportDialog : ContentDialog
{
    /// <summary>
    /// Narrower than the About box's 420. There is no data laid out in columns here, only
    /// three short paragraphs, and a compact dialog reads as a note rather than an appeal.
    /// </summary>
    private const double ContentWidth = 380;

    public SupportDialog()
    {
        Title = "Support the Project";
        CloseButtonText = "Close";

        // Close, not Primary. Enter dismisses this dialog; it does not open a browser at a
        // payment page. Someone hitting Enter on reflex should end up where they started.
        DefaultButton = ContentDialogButton.Close;

        // Each action is offered only when there is somewhere to send the reader. An empty
        // or malformed constant drops the button rather than launching nothing, so the
        // dialog still opens and still says what it has to say.
        if (ProjectLinks.IsUsable(ProjectLinks.SponsorsUrl))
        {
            PrimaryButtonText = "Sponsor on GitHub";
        }

        if (ProjectLinks.IsUsable(ProjectLinks.RepositoryUrl))
        {
            SecondaryButtonText = "View on GitHub";
        }

        Content = BuildContent();
    }

    /// <summary>
    /// Three paragraphs and nothing else. No logo, no artwork, no animation: this should
    /// read as part of the app rather than as an advertisement inside it.
    /// </summary>
    private static StackPanel BuildContent()
    {
        var panel = new StackPanel { Spacing = 12, Width = ContentWidth };

        panel.Children.Add(new TextBlock
        {
            Text = "Marqora is free and open source.",
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "If you find it useful, consider supporting its continued development. "
                + "Your contribution helps with development, maintenance and new features.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "You can also support Marqora by starring it on GitHub.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });

        return panel;
    }
}
