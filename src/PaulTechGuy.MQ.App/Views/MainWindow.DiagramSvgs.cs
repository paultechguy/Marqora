// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

#if DEBUG
using Microsoft.UI.Xaml.Controls;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// File > Export > Save diagram SVGs for Word, in Debug builds only.
///
/// What is left of the week-one engine spike's menu (docs/Export-Alignment-Plan.md, §4). Its
/// paged PDF and print items went when Export to PDF and Print moved to the paged engine; this
/// one stays for phase 3, which tries Word on each diagram type's light drawing (§7.2). Built
/// here rather than in the markup so a Release build has nothing to hide.
/// </summary>
public sealed partial class MainWindow
{
    private void AddDiagramSvgsItem()
    {
        var svgs = new MenuFlyoutItem { Text = "Save diagram SVGs for Word..." };

        svgs.Click += async (_, _) => await ViewModel.SaveDiagramSvgsAsync();

        ExportMenu.Items.Add(new MenuFlyoutSeparator());
        ExportMenu.Items.Add(svgs);
    }
}
#endif
