namespace SoundPartyCompanion.Core.Devices;

public sealed record LowBatteryReminderState(
    int? LastObservedPercent,
    int NotifiedThresholds)
{
    public static LowBatteryReminderState Empty { get; } = new(null, 0);
}

public sealed record LowBatteryReminderDecision(
    LowBatteryReminderState State,
    int? Threshold);

public static class LowBatteryReminderRules
{
    public const int WarningThreshold = 20;
    public const int CriticalThreshold = 10;

    private const int WarningMask = 1;
    private const int CriticalMask = 2;
    private const int WarningRearmPercent = 25;
    private const int CriticalRearmPercent = 15;

    public static LowBatteryReminderDecision Evaluate(
        LowBatteryReminderState state,
        int currentPercent,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (currentPercent is < 0 or > 100)
        {
            return new LowBatteryReminderDecision(state, null);
        }

        var notified = state.NotifiedThresholds & (WarningMask | CriticalMask);
        if (currentPercent > WarningRearmPercent)
        {
            notified &= ~WarningMask;
        }
        if (currentPercent > CriticalRearmPercent)
        {
            notified &= ~CriticalMask;
        }

        int? threshold = null;
        if (enabled)
        {
            var warningDue = currentPercent <= WarningThreshold &&
                (notified & WarningMask) == 0;
            var criticalDue = currentPercent <= CriticalThreshold &&
                (notified & CriticalMask) == 0;

            if (criticalDue)
            {
                threshold = CriticalThreshold;
                notified |= WarningMask | CriticalMask;
            }
            else if (warningDue)
            {
                threshold = WarningThreshold;
                notified |= WarningMask;
            }
        }

        return new LowBatteryReminderDecision(
            new LowBatteryReminderState(currentPercent, notified),
            threshold);
    }
}
