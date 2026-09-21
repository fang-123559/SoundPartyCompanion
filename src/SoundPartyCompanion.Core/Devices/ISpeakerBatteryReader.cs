namespace SoundPartyCompanion.Core.Devices;

public interface ISpeakerBatteryReader
{
    ValueTask<SpeakerBatteryReading?> ReadAsync(
        SpeakerDevice device,
        CancellationToken cancellationToken = default);
}
