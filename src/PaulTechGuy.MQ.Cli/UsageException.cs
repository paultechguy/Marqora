// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli;

/// <summary>
/// The command line asked for something that cannot be done as asked, found once the verb had
/// begun: an unknown theme, a settings file that will not read. Ends the run with exit 2 before
/// any file is touched, like a parse error does.
/// </summary>
internal sealed class UsageException : Exception
{
    public UsageException()
        : base("The command line could not be used as given.")
    {
    }

    public UsageException(string message)
        : base(message)
    {
    }

    public UsageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
