// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

#if DEBUG
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.App.Services;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// File > Export > Paged PDF engine (spike), in Debug builds only.
///
/// The week-one test of the PDF engine the alignment plan chose (docs/Export-Alignment-Plan.md,
/// §4, S1 to S4). There were two items, one per way of keeping the print view out of sight;
/// the hidden one stalled, so only the off-screen one remains. Built here rather than
/// in the markup so a Release build has nothing to hide. Goes when phase 2 replaces Export to
/// PDF, or when the spike says the engine cannot.
/// </summary>
public sealed partial class MainWindow
{
    private void AddPagedPdfSpike()
    {
        var engine = new MenuFlyoutSubItem { Text = "Paged PDF engine (spike)" };

        // The hidden view is not offered: it stalls (see PagedHostMode.Hidden).
        engine.Items.Add(SpikeItem("Export with an off-screen view...", PagedHostMode.OffScreen, "off-screen"));
        engine.Items.Add(PrintSpikeItem());

        // Spike S6: the light drawings Word would be given, for Word itself to try.
        var svgs = new MenuFlyoutItem { Text = "Save diagram SVGs for Word..." };

        svgs.Click += async (_, _) => await ViewModel.SaveDiagramSvgsSpikeAsync();
        engine.Items.Add(svgs);

        ExportMenu.Items.Add(new MenuFlyoutSeparator());
        ExportMenu.Items.Add(engine);
    }

    private MenuFlyoutItem SpikeItem(string text, PagedHostMode mode, string variant)
    {
        var item = new MenuFlyoutItem { Text = text };

        item.Click += async (_, _) =>
        {
            if (_previewHost is not { } host)
            {
                return;
            }

            IntPtr window = WinRT.Interop.WindowNative.GetWindowHandle(this);

            await ViewModel.ExportPagedPdfSpikeAsync(
                (path, setup, title, furniture) => host.ExportPagedPdfAsync(path, setup, title, furniture, mode, window),
                variant);
        };

        return item;
    }

    /// <summary>Spike S5: the paged pages to a real printer.</summary>
    private MenuFlyoutItem PrintSpikeItem()
    {
        var item = new MenuFlyoutItem { Text = "Print with the paged engine..." };

        item.Click += async (_, _) =>
        {
            if (_previewHost is not { } host)
            {
                return;
            }

            IntPtr window = WinRT.Interop.WindowNative.GetWindowHandle(this);

            await ViewModel.PrintPagedSpikeAsync(
                (job, title, furniture) => host.PrintPagedAsync(job, title, furniture, PagedHostMode.OffScreen, window));
        };

        return item;
    }
}
#endif
