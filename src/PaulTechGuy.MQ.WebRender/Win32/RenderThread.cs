// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Threading.Channels;

namespace PaulTechGuy.MQ.WebRender.Win32;

/// <summary>
/// The one thread a browser lives on, where there is no UI thread to host it: STA, pumping
/// messages through a <see cref="StaPump"/>, and running the work handed to it.
///
/// WebView2 belongs to the thread that made it, and that thread has to pump. marqora-cli awaits
/// without a context and so wanders between pool threads, which suits files and text and would
/// break a browser; a run starts one of these with its first export, so a run of text verbs never
/// makes it. The export tests start one for theirs.
/// </summary>
public sealed class RenderThread : IDisposable
{
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });

    private readonly Thread _thread;

    /// <param name="name">The thread's name, for a debugger.</param>
    public RenderThread(string name)
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Runs <paramref name="work"/> on this thread, after anything handed over before it.</summary>
    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        bool queued = _work.Writer.TryWrite(async () =>
        {
            try
            {
                done.SetResult(await work().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        });

        if (!queued)
        {
            done.SetException(new InvalidOperationException("The render thread has stopped."));
        }

        return done.Task;
    }

    /// <summary>Ends the thread. Work still running is abandoned: by then the run has ended its browser.</summary>
    public void Dispose()
    {
        _work.Writer.TryComplete();
        _thread.Join(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Each piece of work is started in turn and not waited for: they share the thread at their
    /// awaits, as the app's UI thread shares itself. Waiting would put the run's cleanup at Ctrl+C
    /// behind a print still laying out, which is exactly the work the cleanup has to end.
    /// </summary>
    private void Pump() =>
        StaPump.Run(async () =>
        {
            await foreach (Func<Task> item in _work.Reader.ReadAllAsync().ConfigureAwait(true))
            {
                // Each item settles its own task, failures included.
                _ = item();
            }

            return 0;
        });
}
