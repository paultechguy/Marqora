// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// How a per-path mark decides what counts as the same file, and what counts as a file that has
/// gone for good.
///
/// Shared by <see cref="DocumentLocksService"/> and <see cref="DocumentPinsService"/>, which are
/// the same store twice over: a set of paths, written straight through, pruned on load. These two
/// methods are the only subtle part of either, so they live in one place — two copies would mean
/// the next correction to link resolution lands in whichever service the person happened to be
/// reading.
///
/// What differs between the two callers is what a wrong answer costs, and that belongs with each
/// of them rather than here.
/// </summary>
internal static class DocumentPathKeys
{
    /// <summary>
    /// The one spelling of a path that every caller has to agree on.
    ///
    /// <see cref="Path.GetFullPath(string)"/> settles relative paths and casing, but it resolves
    /// no links: a junction, a symlink and a <c>subst</c> drive each reach the same file under a
    /// different name, and a mark put on one spelling would not be found by the other. Resolving
    /// to the final target collapses them.
    ///
    /// A path that cannot be resolved - it does not exist yet, it lives on a share that is not
    /// mounted, the link is broken - keeps its full form. That is the honest answer: an
    /// unresolvable path is still a perfectly good key, it just cannot be unified with its
    /// aliases until the file is reachable again.
    /// </summary>
    public static string Normalize(string path)
    {
        string full;

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }

        try
        {
            if (File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target)
            {
                return target.FullName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not reachable right now. The full path is still a usable key.
        }

        return full;
    }

    /// <summary>
    /// Whether this file is really gone, as opposed to merely out of reach.
    ///
    /// The obvious test - does the file exist - is wrong on its own. A document on a detached
    /// drive or a share the machine is not currently on does not exist either, and dropping its
    /// mark would quietly clear a whole shareful of reference documents the first time Marqora
    /// opened away from the network. Nothing is dropped unless the volume holding it answers.
    ///
    /// Anything unclear is kept. Both callers would rather carry a stale entry than drop a live
    /// one: for a read-only mark the cost of being wrong is a document written without being
    /// asked, and for a pin it is a document that quietly leaves the front of the strip.
    /// </summary>
    public static bool HasBeenDeleted(string path)
    {
        try
        {
            string? root = Path.GetPathRoot(path);

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return false;
            }

            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
