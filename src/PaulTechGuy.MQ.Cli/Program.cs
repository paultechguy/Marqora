// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli;

internal static class Program
{
    /// <summary>
    /// STA, as a console entry point that hosts COM should be. The browser an export needs lives
    /// on a render thread of its own (WebRender's Win32/RenderThread.cs), which pumps messages; this
    /// thread never does.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        return CliApp.Run(args);
    }
}
