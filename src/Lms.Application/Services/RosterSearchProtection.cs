using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lms.Application.Services;

public sealed class RosterSearchProtection(TimeProvider? clock = null, ILogger<RosterSearchProtection>? logger = null,
    int visitorLimit = 8, int globalLimit = 60)
{
    private sealed class Window
    {
        public DateTimeOffset Started;
        public int Count;
    }

    private readonly object _sync = new();
    private readonly ConditionalWeakTable<object, Window> _visitors = new();
    private readonly Window _global = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ILogger _logger = logger ?? NullLogger<RosterSearchProtection>.Instance;
    private int _active;

    public IDisposable Enter(object visitor)
    {
        lock (_sync)
        {
            var now = _clock.GetUtcNow();
            var window = _visitors.GetOrCreateValue(visitor);
            Refresh(window, now);
            Refresh(_global, now);
            if (_active >= 2 || window.Count >= visitorLimit || _global.Count >= globalLimit)
            {
                _logger.LogWarning("Roster search rejected by availability protection. ActiveSearches={ActiveSearches}", _active);
                throw new RosterRequestException("License search is busy. Please wait a minute and try again.");
            }
            window.Count++;
            _global.Count++;
            _active++;
            return new Lease(this, _clock.GetTimestamp());
        }
    }

    private static void Refresh(Window window, DateTimeOffset now)
    {
        if (now - window.Started >= TimeSpan.FromMinutes(1))
        {
            window.Started = now;
            window.Count = 0;
        }
    }

    private sealed class Lease(RosterSearchProtection owner, long started) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._sync) owner._active--;
            var milliseconds = owner._clock.GetElapsedTime(started).TotalMilliseconds;
            owner._logger.LogInformation("Roster search finished. DurationMs={DurationMs}", milliseconds);
            if (milliseconds >= 2000)
                owner._logger.LogWarning("Slow roster search. DurationMs={DurationMs}", milliseconds);
        }
    }
}
