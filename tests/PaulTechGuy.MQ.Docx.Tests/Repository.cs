// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Docx.Tests;

/// <summary>Files beside the code that the Word export's tests read: sources, the webshell, docs.</summary>
internal static class Repository
{
    /// <summary>
    /// The repository root, found by walking up from the test binary until the solution file
    /// turns up. Tests run from bin/Debug/net10.0, and the depth from there is not something
    /// worth hard-coding.
    /// </summary>
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PaulTechGuy.MQ.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found.");
    }

    public static string Webshell(string file) => Path.Combine(Root(), "webshell", file);

    public static string Source(string project, string file) => Path.Combine(Root(), "src", project, file);
}
