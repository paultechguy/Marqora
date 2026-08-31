// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using CommunityToolkit.Mvvm.ComponentModel;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// One row in the recent-files list, with the presentation details the list needs:
/// a friendly folder label and a relative timestamp.
///
/// Icon glyphs deliberately stay in XAML rather than here, so the view model carries state
/// and the view decides how to draw it.
/// </summary>
public sealed partial class RecentFileViewModel(RecentFile model) : ObservableObject
{
    public string Path => model.Path;

    public string FileName => model.FileName;

    /// <summary>Folder shown under the file name, with the user profile collapsed to a tilde.</summary>
    public string Location
    {
        get
        {
            string? directory = model.DirectoryName;

            if (string.IsNullOrEmpty(directory))
            {
                return string.Empty;
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return !string.IsNullOrEmpty(home)
                && directory.StartsWith(home, StringComparison.OrdinalIgnoreCase)
                    ? "~" + directory[home.Length..]
                    : directory;
        }
    }

    public bool IsPinned => model.IsPinned;

    public string PinTooltip => model.IsPinned ? "Unpin" : "Pin to top";

    /// <summary>Coarse relative time. Precision beyond this is noise in an MRU list.</summary>
    public string LastOpenedLabel
    {
        get
        {
            TimeSpan elapsed = DateTimeOffset.UtcNow - model.LastOpenedUtc;

            return elapsed switch
            {
                { TotalMinutes: < 1 } => "Just now",
                { TotalMinutes: < 60 } => $"{(int)elapsed.TotalMinutes} min ago",
                { TotalHours: < 24 } => $"{(int)elapsed.TotalHours} hr ago",
                { TotalDays: < 2 } => "Yesterday",
                { TotalDays: < 30 } => $"{(int)elapsed.TotalDays} days ago",
                _ => model.LastOpenedUtc.ToLocalTime()
                    .ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture),
            };
        }
    }

    /// <summary>False when the file has been deleted or moved since it was last opened.</summary>
    public bool Exists => File.Exists(model.Path);

    /// <summary>Dims the row and explains itself when the file is gone.</summary>
    public double RowOpacity => Exists ? 1.0 : 0.45;

    public string Tooltip => Exists ? model.Path : model.Path + "  (not found)";
}
