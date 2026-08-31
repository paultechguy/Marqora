// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Holds settings in memory and writes them back on a short delay.
///
/// Window resizes, splitter drags and zoom steps all mutate settings many times a second.
/// Writing on every change would keep the disk busy for no benefit, so changes coalesce
/// into a single write once the user pauses.
/// </summary>
public sealed class SettingsService(ISettingsRepository repository, ILogger<SettingsService> logger)
    : ISettingsService, IAsyncDisposable
{
    private static readonly TimeSpan WriteDelay = TimeSpan.FromMilliseconds(750);

    private readonly Lock _sync = new();

    private CancellationTokenSource? _pendingWrite;
    private AppSettings _current = AppSettings.Default;
    private bool _isDirty;

    public AppSettings Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        AppSettings loaded = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _current = loaded;
            _isDirty = false;
        }

        logger.LogInformation("Settings loaded: theme {Theme}, view {View}.", loaded.Theme, loaded.ViewMode);
        SettingsChanged?.Invoke(this, loaded);
    }

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        AppSettings updated;

        lock (_sync)
        {
            updated = mutate(_current);

            // Records compare structurally, so a change that alters nothing costs nothing.
            if (updated == _current)
            {
                return;
            }

            _current = updated;
            _isDirty = true;
        }

        SettingsChanged?.Invoke(this, updated);
        ScheduleWrite();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        AppSettings snapshot;

        lock (_sync)
        {
            CancelPendingWrite();

            if (!_isDirty)
            {
                return;
            }

            snapshot = _current;
            _isDirty = false;
        }

        await repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        logger.LogDebug("Settings flushed to disk.");
    }

    private void ScheduleWrite()
    {
        CancellationToken token;

        lock (_sync)
        {
            CancelPendingWrite();
            _pendingWrite = new CancellationTokenSource();
            token = _pendingWrite.Token;
        }

        _ = WriteAfterDelayAsync(token);
    }

    private async Task WriteAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(WriteDelay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change, which scheduled its own write.
            return;
        }

        try
        {
            // Deliberately not the delay token: it is cancelled and disposed by FlushAsync.
            await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deferred settings write failed.");
        }
    }

    private void CancelPendingWrite()
    {
        if (_pendingWrite is null)
        {
            return;
        }

        _pendingWrite.Cancel();
        _pendingWrite.Dispose();
        _pendingWrite = null;
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync(CancellationToken.None).ConfigureAwait(false);

        lock (_sync)
        {
            CancelPendingWrite();
        }
    }
}
