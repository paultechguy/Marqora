// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders.Tests;

/// <summary>A scratch folder that cleans itself up, for the tests that need a real disk.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "marqora-tests",
            Guid.NewGuid().ToString("n"));

        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Writes a file at a relative path, making its folders. Answers the full path.</summary>
    public string File(string relative, string text = "")
    {
        string full = Full(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, text);
        return full;
    }

    /// <summary>Makes a folder at a relative path. Answers the full path.</summary>
    public string Folder(string relative)
    {
        string full = Full(relative);
        Directory.CreateDirectory(full);
        return full;
    }

    public string Full(string relative) =>
        System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A watcher test can leave a handle for a moment; the temp folder is cleaned eventually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
