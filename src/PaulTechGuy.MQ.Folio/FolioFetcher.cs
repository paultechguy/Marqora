// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folio;

/// <summary>What came back, and what did not.</summary>
/// <param name="Files">The address the author wrote, mapped to a local file holding its bytes.</param>
/// <param name="Failures">
/// One per picture that was asked for and did not arrive, ready to be merged into the plan.
/// </param>
public sealed record FolioFetchResult(
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<FolioWarning> Failures);

/// <summary>
/// Fetches the pictures a Folio references by web address, into a scratch folder the caller owns.
///
/// This is the only place in Marqora that asks the network for anything, and it exists because a
/// Folio that leaves web addresses in it has not avoided the fetch - it has moved the fetch onto
/// whoever opens the file, who was never asked and cannot see it coming. A picture carried inside
/// the Folio is one the reader's browser never reaches for.
///
/// Two things about the shape of this are deliberate and should survive tidying:
///
/// The <see cref="HttpClient"/> is built inside <see cref="FetchAsync"/> and nowhere else. Not a
/// field, not a singleton, not registered with the container. When nobody has asked for this, the
/// type is never touched and no handler exists - so "off" is the absence of the machinery rather
/// than a flag somebody remembered to read. It costs the usual socket lecture and is worth it:
/// this runs once per share, by hand, not in a loop.
///
/// And nothing here throws for a network reason. The Folio build has no cancellation and catches
/// only IO exceptions, so a timeout escaping this method would take the whole share down with it.
/// Everything that can go wrong comes back in <see cref="FolioFetchResult.Failures"/> instead, and
/// the planner treats an address that failed exactly as it treats one nobody asked for - which is
/// to leave it alone. That is not leniency; it is the same code path, so it cannot rot separately.
///
/// There is no state on this class on purpose. Every call carries its own names and its own
/// reasons, because two shares in one session must not be able to see each other's.
/// </summary>
/// <param name="version">
/// What to say Marqora is, in the User-Agent. Passed in rather than read here, because the
/// running build's version is the application's business and this has no reason to know how it
/// is discovered - and a test wants to state it rather than inherit it.
/// </param>
public sealed class FolioFetcher(ILogger<FolioFetcher> logger, string version)
{
    /// <summary>How long any one picture may take before it is given up on.</summary>
    private static readonly TimeSpan PerRequest = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long the whole fetch may take, however many pictures there are.
    ///
    /// The share cannot be cancelled once it starts, so this is not a refinement - it is the only
    /// thing standing between the author and a window that does not come back. Forty pictures
    /// behind a host that accepts connections and then says nothing would otherwise be a very
    /// long silence.
    /// </summary>
    private static readonly TimeSpan Overall = TimeSpan.FromMinutes(2);

    /// <summary>Past this, a picture is not a picture and something has gone wrong.</summary>
    private const long MaxBytes = 24 * 1024 * 1024;

    /// <summary>
    /// Enough to find a format signature, and enough for an SVG that opens with a declaration,
    /// a doctype and a comment before its first tag.
    ///
    /// Not enough to measure a JPEG, and it does not need to be: measuring is the planner's job,
    /// from the file, with <see cref="ImageDimensions.HeaderBytes"/> to work with.
    /// </summary>
    private const int SniffBytes = 2048;

    /// <summary>How far a chain of redirects may go before it is somebody playing a game.</summary>
    private const int MaxHops = 5;

    /// <summary>One attempt's outcome: where the bytes landed, or why they did not.</summary>
    private readonly record struct Attempt(string? Path, string? Reason)
    {
        public static Attempt Failed(string reason) => new(null, reason);

        public static Attempt Saved(string path) => new(path, null);
    }

    public async Task<FolioFetchResult> FetchAsync(
        IReadOnlyList<FolioRemoteImage> images,
        string scratch,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratch);

        Dictionary<string, string> files = new(StringComparer.Ordinal);
        List<FolioWarning> failures = [];

        if (images.Count == 0)
        {
            return new FolioFetchResult(files, failures);
        }

        // The caller computes this path but does not create it - the shrinker, which used to be
        // the only thing writing here, makes it lazily when it has something to write. This runs
        // first now, so it is the one that has to.
        Directory.CreateDirectory(scratch);

        // Names claimed inside the scratch folder, for this call and no other.
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Overall);

        // Redirects are followed by hand so every hop can be checked. The handler's own redirect
        // following would walk from https: to somewhere else without asking.
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            Credentials = null,
        };

        using var client = new HttpClient(handler) { Timeout = PerRequest };
        // Honest about who is calling. A site that would rather not serve a desktop editor should
        // be able to tell that it is one.
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Marqora/{version}");

        // One address is fetched once, however many documents name it. The first reference is the
        // one a failure is reported against, which is enough to find it.
        foreach (FolioRemoteImage image in images
            .GroupBy(i => i.Url, StringComparer.Ordinal)
            .Select(g => g.First()))
        {
            if (budget.IsCancellationRequested)
            {
                failures.Add(Failed(image, "there was no time left to fetch it."));
                continue;
            }

            progress?.Report($"Fetching from {image.Host}...");

            Attempt attempt = await TryFetchAsync(client, image, scratch, taken, budget.Token)
                .ConfigureAwait(false);

            if (attempt.Path is null)
            {
                failures.Add(Failed(image, attempt.Reason ?? "it could not be fetched."));
                continue;
            }

            files[image.Url] = attempt.Path;
        }

        return new FolioFetchResult(files, failures);
    }

    private async Task<Attempt> TryFetchAsync(
        HttpClient client,
        FolioRemoteImage image,
        string scratch,
        HashSet<string> taken,
        CancellationToken cancellationToken)
    {
        // A protocol-relative address inherits the scheme of the page it came from, and there is
        // no page here. https is the only sane assumption and the safe one.
        string address = image.Url.StartsWith("//", StringComparison.Ordinal)
            ? "https:" + image.Url
            : image.Url;

        HttpResponseMessage? response = null;

        try
        {
            for (int hop = 0; ; hop++)
            {
                if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    // The obvious game is a redirect into file:. Checked every hop, not just the
                    // first, because the first one is the only one the author ever saw.
                    return Attempt.Failed("its address does not point at the web.");
                }

                response?.Dispose();
                response = await client
                    .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!IsRedirect(response.StatusCode))
                {
                    break;
                }

                if (hop == MaxHops)
                {
                    return Attempt.Failed("it redirected too many times.");
                }

                string? next = response.Headers.Location?.ToString();

                if (string.IsNullOrWhiteSpace(next))
                {
                    return Attempt.Failed("the site redirected without saying where.");
                }

                address = new Uri(uri, next).ToString();
            }

            if (!response.IsSuccessStatusCode)
            {
                return Attempt.Failed($"the site answered {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return Attempt.Failed("it is larger than a picture has any reason to be.");
            }

            return await SaveAsync(response, image, scratch, taken, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Covers the per-request timeout and the overall budget alike: HttpClient surfaces a
            // timeout as a cancellation, and the difference is not worth explaining to an author.
            logger.LogDebug("Timed out fetching {Url} for a Folio.", image.Url);

            return Attempt.Failed("it took too long to answer.");
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Nothing may escape. The share that called this has no cancellation and catches only
            // IO, so anything thrown from here takes the whole export down with it.
            logger.LogDebug(ex, "Could not fetch {Url} for a Folio.", image.Url);

            return Attempt.Failed("the site could not be reached.");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static async Task<Attempt> SaveAsync(
        HttpResponseMessage response,
        FolioRemoteImage image,
        string scratch,
        HashSet<string> taken,
        CancellationToken cancellationToken)
    {
        // Named from the address rather than from a counter, so the picture arrives in the Folio
        // as "media/build.svg" and not as a hex string nobody can place.
        string name = DocumentAssets.NextFreeName(
            DocumentAssets.Slug(NameFrom(image.Url)),
            ExtensionFrom(image.Url),
            taken);

        taken.Add(name);

        string path = Path.Combine(scratch, name);

        await using (Stream source = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            await using FileStream target = File.Create(path);

            byte[] buffer = new byte[81920];
            long total = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;

                // Checked while reading rather than afterwards: a response with no length, or one
                // that lies about it, must not be able to fill the disk before anybody notices.
                if (total > MaxBytes)
                {
                    return Attempt.Failed("it is larger than a picture has any reason to be.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // Sniffed from the bytes, never trusted from the header. A site that serves a sign-in page
        // where a picture was asked for should not be able to put HTML inside somebody's Folio.
        byte[] head = new byte[SniffBytes];
        int got;

        await using (FileStream check = File.OpenRead(path))
        {
            got = await check.ReadAsync(head, cancellationToken).ConfigureAwait(false);
        }

        return LooksLikeAPicture(head.AsSpan(0, got))
            ? Attempt.Saved(path)
            : Attempt.Failed("what came back was not a picture.");
    }

    /// <summary>
    /// Whether the bytes open the way a picture opens.
    ///
    /// A signature test, deliberately, and not a dimension test. The first version of this asked
    /// <see cref="ImageDimensions"/> for a size and called anything it could not measure "not a
    /// picture" - which rejected a perfectly good JPEG from Wikimedia, because a JPEG keeps its
    /// dimensions in a marker that can sit many kilobytes in and this only ever read the first
    /// few dozen bytes. <see cref="ImageDimensions.HeaderBytes"/> is 64 KB and says as much.
    ///
    /// The deeper mistake was using a measurement as a check at all. A picture whose size cannot
    /// be read is still a picture - the planner already treats an unknown width as "leave this
    /// one alone" - so the question here is only what format the bytes are in.
    /// </summary>
    private static bool LooksLikeAPicture(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return true; // JPEG
        }

        if (head.Length >= 8 && head[0] == 0x89 && head[1] == (byte)'P'
            && head[2] == (byte)'N' && head[3] == (byte)'G')
        {
            return true; // PNG
        }

        if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
        {
            return true; // GIF
        }

        if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return true; // WebP
        }

        if (head.StartsWith("BM"u8))
        {
            return true; // BMP
        }

        if (head.StartsWith("II\x2A\x00"u8) || head.StartsWith("MM\x00\x2A"u8))
        {
            return true; // TIFF, either byte order
        }

        if (head.Length >= 4 && head[0] == 0x00 && head[1] == 0x00 && head[2] == 0x01 && head[3] == 0x00)
        {
            return true; // ICO
        }

        return LooksLikeSvg(head);
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static FolioWarning Failed(FolioRemoteImage image, string reason) => new()
    {
        Kind = FolioWarningKind.RemoteImageFailed,
        DocumentPath = image.DocumentPath,
        Line = image.Line,
        Url = image.Url,
        Message = $"\"{image.Url}\" was not fetched: {reason}",
    };

    /// <summary>
    /// SVG has no binary header to read, so it is recognized by finding its opening tag.
    ///
    /// Searched for within the sniffed window rather than required at the very start, because a
    /// real SVG often begins with a byte order mark, an XML declaration, a doctype and a comment
    /// before it gets to the point. Accepting anything starting "&lt;?xml" would have let every
    /// XML document through as a picture, which is the opposite mistake.
    /// </summary>
    private static bool LooksLikeSvg(ReadOnlySpan<byte> head) =>
        Encoding.UTF8.GetString(head).Contains("<svg", StringComparison.OrdinalIgnoreCase);

    private static string NameFrom(string url)
    {
        string path = url.Split(['?', '#'], 2)[0].TrimEnd('/');
        string last = path[(path.LastIndexOf('/') + 1)..];
        string bare = Path.GetFileNameWithoutExtension(last);

        return string.IsNullOrWhiteSpace(bare) ? "picture" : bare;
    }

    private static string ExtensionFrom(string url)
    {
        string path = url.Split(['?', '#'], 2)[0];
        string extension = Path.GetExtension(path);

        // A picture served from an address with nothing on the end is still a picture. Every
        // writer takes its bytes from the file, so the name only has to be plausible and unique.
        return string.IsNullOrWhiteSpace(extension) || extension.Length > 6 ? ".img" : extension;
    }
}
