// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace PaulTechGuy.MQ.Cli.Rendering;

/// <summary>
/// The browser's data folder for one run (CLI-EX-02): under %TEMP%\Marqora-cli, apart from the
/// app's preview and print folders, named for the process that made it, and deleted when the run
/// ends. A folder left by a run that could not clean up - killed, or crashed - is swept by the next
/// run that starts a browser, once its process is known to be gone.
///
/// The name is the process id, the process's start time (an id is reused once its process ends) and
/// a few letters of its own, so two runs in one process - a test's - never share a folder either.
/// </summary>
internal static class RunFolder
{
    private static readonly long ProcessStart = Process.GetCurrentProcess().StartTime.Ticks;

    public static string Root => Path.Combine(Path.GetTempPath(), "Marqora-cli");

    /// <summary>
    /// A new, empty folder for this run, with the stale ones swept first. Every WEBVIEW2_ variable
    /// inherited from outside is cleared, so the browser does not depend on the shell the run was
    /// started from.
    /// </summary>
    public static string Create(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        foreach (string name in Environment.GetEnvironmentVariables().Keys.Cast<string>())
        {
            if (name.StartsWith("WEBVIEW2_", StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        SweepStale(logger);

        string folder = Path.Combine(
            Root,
            string.Create(CultureInfo.InvariantCulture, $"{Environment.ProcessId}-{ProcessStart}-{Guid.NewGuid().ToString("n")[..8]}"));

        Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>
    /// Deletes a run's folder, retrying while a browser that has only just exited still holds a
    /// file in it. Says so, rather than failing the run, when it cannot: the next run sweeps it.
    /// </summary>
    public static void Remove(string folder, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(250);
            }
        }

        logger.LogWarning("Could not remove {Folder}; a later run removes it.", folder);
    }

    /// <summary>Removes the run folders whose process is no longer running.</summary>
    private static void SweepStale(ILogger logger)
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (string folder in Directory.EnumerateDirectories(Root))
        {
            string[] parts = Path.GetFileName(folder).Split('-');

            if (parts.Length < 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                || IsRunning(id, ticks))
            {
                continue;
            }

            logger.LogDebug("Removing {Folder}, left by a run that did not finish.", folder);
            Remove(folder, logger);
        }
    }

    /// <summary>The same process, not merely the same id: an id is reused once its process ends.</summary>
    private static bool IsRunning(int id, long startTicks)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return process.StartTime.Ticks == startTicks;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
