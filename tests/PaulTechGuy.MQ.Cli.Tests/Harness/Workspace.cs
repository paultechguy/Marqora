// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Cli.Tests.Harness;

/// <summary>
/// A throwaway folder of real files for one test, removed when the test ends. Every test that
/// lets the CLI write works in one of these, on copies: a fixture in the repository is only ever
/// read (CLI-TST-03).
/// </summary>
internal sealed class Workspace : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public Workspace()
    {
        Directory.CreateDirectory(Root);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "marqora-cli-tests", Guid.NewGuid().ToString("n"));

    /// <summary>A path inside the workspace.</summary>
    public string At(string relative) => Path.Combine(Root, relative);

    /// <summary>A text file, UTF-8 without a byte order mark, exactly as given: no line endings are changed.</summary>
    public string Text(string relative, string contents) => Bytes(relative, Utf8NoBom.GetBytes(contents));

    public string Bytes(string relative, byte[] contents)
    {
        string path = At(relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);

        return path;
    }

    /// <summary>A copy of a file from the repository, so a test may let the CLI rewrite it.</summary>
    public string Copy(string repoRelative, string? relative = null)
    {
        string path = At(relative ?? Path.GetFileName(repoRelative));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(Repo.At(repoRelative), path, overwrite: true);

        return path;
    }

    public string Read(string relative) => File.ReadAllText(At(relative));

    public byte[] ReadBytes(string relative) => File.ReadAllBytes(At(relative));

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder outliving its test is not a failed test.
        }
    }
}
