// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// What the app could find out about the volume a delete is on. Each setting is null when it
/// could not be read.
/// </summary>
/// <param name="DriveType">The drive's kind, as <see cref="DriveInfo.DriveType"/> reports it.</param>
/// <param name="NukeOnDelete">The volume's Recycle Bin is set to delete files immediately.</param>
/// <param name="MaxCapacityMegabytes">The most the volume's Recycle Bin holds, in megabytes.</param>
public sealed record VolumeFacts(DriveType DriveType, bool? NukeOnDelete, long? MaxCapacityMegabytes);

/// <summary>
/// Whether a delete will really reach the Recycle Bin (P7).
///
/// The shell's "allow undo" is a request, not a promise: on a network drive, a USB stick, a
/// volume whose bin is set to delete immediately, or for an item bigger than the bin, it deletes
/// outright and says nothing. The exploration says a delete is never outright without saying so
/// first, so the confirmation asks this before it is shown and says "permanently" when the answer
/// is no. When a setting cannot be read, the shell's default (recycle) is assumed.
/// </summary>
public static class RecyclePolicy
{
    public static bool WillRecycle(VolumeFacts facts, long totalBytes)
    {
        if (facts.DriveType != DriveType.Fixed)
        {
            return false;
        }

        if (facts.NukeOnDelete == true)
        {
            return false;
        }

        if (facts.MaxCapacityMegabytes is { } megabytes && totalBytes > megabytes * 1024L * 1024L)
        {
            return false;
        }

        return true;
    }
}
