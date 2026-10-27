// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.WebRender.Win32;

namespace PaulTechGuy.MQ.Cli;

internal static class Program
{
    /// <summary>
    /// STA, because WebView2 belongs to the thread that creates it and that thread must pump
    /// messages. The text verbs never need it, and do no harm on it.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
#if MQ_SPIKE
        if (args.Length > 0 && args[0] == "__spike-pdf")
        {
            return StaPump.Run(() => Spike.RunAsync(args[1..]));
        }
#endif

        return CliApp.Run(args);
    }
}
