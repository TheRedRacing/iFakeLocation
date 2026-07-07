using iFakeLocation.Contracts;
using iFakeLocation.Services.Location;
using iFakeLocation.Services.RouteSimulation;

namespace iFakeLocation.Tests.Services.RouteSimulation;

public class RouteSimulationSessionTests {
    private static readonly PointLatLng[] TwoPoints = [new(0, 0), new(0, 1)];

    [Fact]
    public void Constructor_FewerThanTwoPoints_Throws() {
        Assert.Throws<RouteSimulationInvalidException>(() =>
            new RouteSimulationSession("udid", [new PointLatLng(0, 0)], speedKmh: 10, loop: false));
    }

    [Fact]
    public void Constructor_NonPositiveSpeed_Throws() {
        Assert.Throws<RouteSimulationInvalidException>(() =>
            new RouteSimulationSession("udid", TwoPoints, speedKmh: 0, loop: false));
    }

    [Fact]
    public void AdvanceTick_WhileRunning_AdvancesElapsedAndProgress() {
        var session = new RouteSimulationSession("udid", TwoPoints, speedKmh: 36, loop: false);
        var total = session.TotalDurationSeconds;

        var result = session.AdvanceTick(total / 2);

        Assert.Equal(RouteSimulationState.Running, result.State);
        Assert.InRange(result.ProgressPercent, 49.0, 51.0);
    }

    [Fact]
    public void Pause_FreezesElapsedTimeAcrossSubsequentTicks() {
        var session = new RouteSimulationSession("udid", TwoPoints, speedKmh: 36, loop: false);
        var beforePause = session.AdvanceTick(session.TotalDurationSeconds / 4);

        session.Pause();
        var afterBigTickWhilePaused = session.AdvanceTick(1000);

        Assert.Equal(RouteSimulationState.Paused, afterBigTickWhilePaused.State);
        Assert.Equal(beforePause.ElapsedSeconds, afterBigTickWhilePaused.ElapsedSeconds, precision: 3);
    }

    [Fact]
    public void Resume_ContinuesFromPausedElapsed_RatherThanResetting() {
        var session = new RouteSimulationSession("udid", TwoPoints, speedKmh: 36, loop: false);
        var beforePause = session.AdvanceTick(session.TotalDurationSeconds / 4);

        session.Pause();
        session.AdvanceTick(1000); // no-op while paused
        session.Resume();
        var afterResume = session.AdvanceTick(session.TotalDurationSeconds / 4);

        Assert.Equal(RouteSimulationState.Running, afterResume.State);
        Assert.True(afterResume.ElapsedSeconds > beforePause.ElapsedSeconds);
    }

    [Fact]
    public void AdvanceTick_PastTotalDuration_NonLooping_CompletesAndClamps() {
        var session = new RouteSimulationSession("udid", TwoPoints, speedKmh: 36, loop: false);

        var result = session.AdvanceTick(session.TotalDurationSeconds * 2);

        Assert.Equal(RouteSimulationState.Completed, result.State);
        Assert.Equal(100.0, result.ProgressPercent, precision: 3);
        Assert.Equal(session.TotalDurationSeconds, result.ElapsedSeconds, precision: 3);
    }

    [Fact]
    public void AdvanceTick_PastTotalDuration_Looping_WrapsAroundAndKeepsRunning() {
        var session = new RouteSimulationSession("udid", TwoPoints, speedKmh: 36, loop: true);
        var total = session.TotalDurationSeconds;

        var result = session.AdvanceTick(total * 1.25);

        Assert.Equal(RouteSimulationState.Running, result.State);
        Assert.InRange(result.ElapsedSeconds, total * 0.24, total * 0.26);
    }

    [Fact]
    public void Snapshot_DoesNotAdvanceState() {
        var session = new RouteSimulationSession("udid", TwoPoints, speedKmh: 36, loop: false);
        session.AdvanceTick(session.TotalDurationSeconds / 4);

        var snapshot1 = session.Snapshot();
        var snapshot2 = session.Snapshot();

        Assert.Equal(snapshot1.ElapsedSeconds, snapshot2.ElapsedSeconds, precision: 6);
    }
}
