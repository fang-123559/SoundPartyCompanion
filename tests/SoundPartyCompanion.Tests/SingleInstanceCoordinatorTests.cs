using SoundPartyCompanion.App.Infrastructure;
using Xunit;

namespace SoundPartyCompanion.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    [Fact]
    public void SecondAcquireSignalsExistingInstanceAndReturnsNull()
    {
        var instanceId = $"SoundPartyCompanion.Tests.{Guid.NewGuid():N}";
        using var activationReceived = new ManualResetEventSlim();
        using var first = Assert.IsType<SingleInstanceCoordinator>(
            SingleInstanceCoordinator.TryAcquire(instanceId, activationReceived.Set));

        var second = SingleInstanceCoordinator.TryAcquire(instanceId, () => { });

        Assert.Null(second);
        Assert.True(
            activationReceived.Wait(TimeSpan.FromSeconds(2)),
            "The existing instance did not receive the activation signal.");
    }

    [Fact]
    public void DisposedCoordinatorAllowsNewInstanceToAcquire()
    {
        var instanceId = $"SoundPartyCompanion.Tests.{Guid.NewGuid():N}";
        var first = Assert.IsType<SingleInstanceCoordinator>(
            SingleInstanceCoordinator.TryAcquire(instanceId, () => { }));

        first.Dispose();

        using var replacement = Assert.IsType<SingleInstanceCoordinator>(
            SingleInstanceCoordinator.TryAcquire(instanceId, () => { }));
    }
}
