namespace SoundPartyCompanion.Core.Devices;

public enum SpeakerChargingState
{
    Unknown,
    NotCharging,
    Charging,
    Full
}

public sealed record SpeakerBatteryReading(
    int Percent,
    DateTimeOffset ObservedAt);

public sealed record SpeakerBatterySnapshot(
    int? Percent,
    SpeakerChargingState Charging,
    DateTimeOffset? ObservedAt,
    bool IsStale)
{
    public static SpeakerBatterySnapshot Unavailable { get; } = new(
        null,
        SpeakerChargingState.Unknown,
        null,
        IsStale: false);
}

public static class SpeakerBatteryStateRules
{
    public static SpeakerBatterySnapshot Resolve(
        SpeakerBatteryReading? reading,
        bool isConnected,
        DateTimeOffset now,
        TimeSpan staleAfter,
        TimeSpan offlineHideAfter)
    {
        if (staleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(staleAfter));
        }

        if (offlineHideAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(offlineHideAfter));
        }

        if (reading is null || reading.Percent is < 0 or > 100)
        {
            return SpeakerBatterySnapshot.Unavailable;
        }

        var age = now <= reading.ObservedAt
            ? TimeSpan.Zero
            : now - reading.ObservedAt;
        if (!isConnected && age > offlineHideAfter)
        {
            return SpeakerBatterySnapshot.Unavailable with
            {
                ObservedAt = reading.ObservedAt,
                IsStale = true
            };
        }

        return new SpeakerBatterySnapshot(
            reading.Percent,
            SpeakerChargingState.Unknown,
            reading.ObservedAt,
            !isConnected || age > staleAfter);
    }
}
