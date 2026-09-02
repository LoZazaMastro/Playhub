using System;

namespace Playhub.Services;

// Pure sampling logic: timestamps are monotonic seconds, never calendar time.
public sealed class SupportUsageClock
{
    public const double ReminderIntervalSeconds = 2 * 60 * 60;
    public const double IdleLimitSeconds = 5 * 60;
    public const double MaximumSampleGapSeconds = 90;

    private double? _lastTimestamp;
    private double _lastIdleSeconds;
    private bool _wasForegroundVisible;

    public SupportUsageClock(double savedUsageSeconds = 0)
    {
        UsageSeconds = double.IsFinite(savedUsageSeconds)
            ? Math.Clamp(savedUsageSeconds, 0, ReminderIntervalSeconds) : 0;
    }

    public double UsageSeconds { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsDue => UsageSeconds >= ReminderIntervalSeconds;

    public void Sample(double monotonicSeconds, bool foregroundVisible, double idleSeconds)
    {
        if (!double.IsFinite(monotonicSeconds) || monotonicSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(monotonicSeconds));
        if (_lastTimestamp is double previous && monotonicSeconds < previous) return;
        var validIdleSample = double.IsFinite(idleSeconds) && idleSeconds >= 0;
        if (!validIdleSample)
        {
            foregroundVisible = false;
            idleSeconds = IdleLimitSeconds;
        }

        if (_lastTimestamp is double last && _wasForegroundVisible && validIdleSample)
        {
            var elapsed = monotonicSeconds - last;
            // A suspended/blocked dispatcher must not turn hours away into usage.
            if (elapsed <= MaximumSampleGapSeconds)
            {
                var activeUntil = Math.Min(monotonicSeconds, last + Math.Max(0, IdleLimitSeconds - _lastIdleSeconds));
                var activeSeconds = Math.Max(0, activeUntil - last);
                var latestInput = monotonicSeconds - idleSeconds;
                if (latestInput > last)
                {
                    // Count resumed input within this sample without filling an
                    // intervening idle period or double-counting its active prefix.
                    activeSeconds += Math.Max(0, Math.Min(monotonicSeconds, latestInput + IdleLimitSeconds) -
                        Math.Max(activeUntil, latestInput));
                }
                UsageSeconds = Math.Min(ReminderIntervalSeconds, UsageSeconds + activeSeconds);
            }
        }

        _lastTimestamp = monotonicSeconds;
        _lastIdleSeconds = idleSeconds;
        _wasForegroundVisible = foregroundVisible;
        IsActive = foregroundVisible && idleSeconds < IdleLimitSeconds;
    }

    // Call only from the automatic native dialog's Opened event, never on a
    // failed/deferred show attempt or a forced preview.
    public bool MarkReminderOpened(double monotonicSeconds, bool foregroundVisible, double idleSeconds)
    {
        Sample(monotonicSeconds, foregroundVisible, idleSeconds);
        if (!IsDue) return false;
        UsageSeconds = 0;
        return true;
    }
}
