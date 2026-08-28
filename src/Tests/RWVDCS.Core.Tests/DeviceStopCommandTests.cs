using RWVDCS.Blocks.RW;
using RWVDCS.Core.Blocks;

namespace RWVDCS.Core.Tests;

public sealed class DeviceStopCommandTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void MiddleStop_CancelsMovementAndRestoresStoppedIndication(bool opening, bool stopResetMode)
    {
        var device = CreateDevice(stopResetMode);
        var command = new TestCommand(0.1f);

        device.Implement(command);
        SetMovementCommand(device, opening, true);
        device.Implement(command);
        SetMovementCommand(device, opening, false);
        device.Implement(command);

        Assert.Equal(opening, (bool)device.On);
        Assert.Equal(!opening, (bool)device.Off);
        Assert.False((bool)device.Stp);
        Assert.False(IsTagBitSet(device, 18));
        Assert.True(IsTagBitSet(device, opening ? 29 : 30));

        device.CSP[0] = true;
        device.Implement(command);

        Assert.False((bool)device.On);
        Assert.False((bool)device.Off);
        Assert.True((bool)device.Stp);
        Assert.True(device.middleStopActive);
        Assert.False(device.onCmdActive);
        Assert.False(device.offCmdActive);
        Assert.Equal(0, device.onTimer);
        Assert.Equal(0, device.offTimer);
        Assert.Equal(0, device.onToverTimer);
        Assert.Equal(0, device.offToverTimer);
        Assert.True(IsTagBitSet(device, 18));
        Assert.False(IsTagBitSet(device, 28));
        Assert.False(IsTagBitSet(device, 29));
        Assert.False(IsTagBitSet(device, 30));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MiddleStop_DoesNotReportCancelledMovementAsOperationFailure(bool opening)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.25f);

        device.Implement(command);
        SetMovementCommand(device, opening, true);
        device.Implement(command);
        SetMovementCommand(device, opening, false);
        device.Implement(command);

        device.CSP[0] = true;
        device.Implement(command);
        device.CSP[0] = false;

        for (int i = 0; i < 10; i++)
            device.Implement(command);

        Assert.False((bool)device.OpFlOn);
        Assert.False((bool)device.OpFlOff);
        Assert.False((bool)device.OpFl);
        Assert.True(IsTagBitSet(device, 18));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MiddleStop_SuppressesPreviousEndpointFeedback(bool opening)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);

        if (opening)
            device.FBOff[0] = true;
        else
            device.FBOn[0] = true;

        device.Implement(command);
        Assert.True(IsTagBitSet(device, opening ? 11 : 10));

        SetMovementCommand(device, opening, true);
        device.Implement(command);
        SetMovementCommand(device, opening, false);
        device.Implement(command);

        device.CSP[0] = true;
        device.Implement(command);

        Assert.True(device.middleStopActive);
        Assert.False(IsTagBitSet(device, 10));
        Assert.False(IsTagBitSet(device, 11));
        Assert.True(IsTagBitSet(device, 18));
        Assert.False(IsTagBitSet(device, 29));
        Assert.False(IsTagBitSet(device, 30));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MiddleStop_BlocksPersistentAutomaticMovementRequest(bool opening)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);

        if (opening)
            device.AOn[0] = true;
        else
            device.AOff[0] = true;

        device.Implement(command);
        Assert.Equal(opening, device.onCmdActive);
        Assert.Equal(!opening, device.offCmdActive);

        device.CSP[0] = true;
        device.Implement(command);
        device.CSP[0] = false;

        for (int i = 0; i < 5; i++)
            device.Implement(command);

        Assert.True(device.middleStopActive);
        Assert.False(device.onCmdActive);
        Assert.False(device.offCmdActive);
        Assert.False((bool)device.On);
        Assert.False((bool)device.Off);
        Assert.True((bool)device.Stp);
        Assert.True(IsTagBitSet(device, 18));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ManualMovementCommand_ResumesFromMiddleStop(bool opening)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);

        device.Implement(command);
        device.CON[0] = true;
        device.Implement(command);
        device.CON[0] = false;
        device.Implement(command);
        device.CSP[0] = true;
        device.Implement(command);
        device.CSP[0] = false;
        device.Implement(command);

        Assert.True(device.middleStopActive);

        SetMovementCommand(device, opening, true);
        device.Implement(command);

        Assert.False(device.middleStopActive);
        Assert.Equal(opening, device.onCmdActive);
        Assert.Equal(!opening, device.offCmdActive);
        Assert.False((bool)device.Stp);
        Assert.False(IsTagBitSet(device, 18));
        Assert.True(IsTagBitSet(device, opening ? 29 : 30));
    }

    [Fact]
    public void ProtectionCommand_OverridesMiddleStop()
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);

        device.Implement(command);
        device.CON[0] = true;
        device.Implement(command);
        device.CON[0] = false;
        device.Implement(command);
        device.CSP[0] = true;
        device.Implement(command);

        Assert.True(device.middleStopActive);

        device.CSP[0] = false;
        device.POff[0] = true;
        device.Implement(command);

        Assert.False(device.middleStopActive);
        Assert.True(device.offCmdActive);
        Assert.True((bool)device.Off);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeldProtection_FeedbackClearsOutputAndDoesNotRetrigger(bool protectionOn)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);
        device.ResetM = 2;
        device.OutM = true;

        SetProtection(device, protectionOn, true);
        device.Implement(command);

        Assert.Equal(protectionOn, (bool)device.On);
        Assert.Equal(!protectionOn, (bool)device.Off);
        Assert.Equal(protectionOn, device.onCmdActive);
        Assert.Equal(!protectionOn, device.offCmdActive);

        SetFeedback(device, protectionOn, true);
        device.Implement(command);

        Assert.False((bool)device.On);
        Assert.False((bool)device.Off);
        Assert.False(device.onCmdActive);
        Assert.False(device.offCmdActive);
        Assert.True((bool)device.Stp);
        Assert.True(IsTagBitSet(device, 18));

        for (int i = 0; i < 5; i++)
            device.Implement(command);

        Assert.False((bool)device.On);
        Assert.False((bool)device.Off);
        Assert.False(device.onCmdActive);
        Assert.False(device.offCmdActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeldProtection_DoesNotResetTimersAndCanTimeout(bool protectionOn)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);
        device.OutM = false;
        device.SetT = 0.2;
        device.Tover = 0.5;
        device.FLB = false;

        SetProtection(device, protectionOn, true);
        device.Implement(command);
        device.Implement(command);

        double firstTimer = protectionOn ? device.onToverTimer : device.offToverTimer;
        Assert.True(firstTimer > 0);

        device.Implement(command);
        double secondTimer = protectionOn ? device.onToverTimer : device.offToverTimer;
        Assert.True(secondTimer > firstTimer);
        Assert.False(protectionOn ? (bool)device.On : (bool)device.Off);
        Assert.True(protectionOn ? device.onCmdActive : device.offCmdActive);

        for (int i = 0; i < 4; i++)
            device.Implement(command);

        Assert.True(protectionOn ? (bool)device.OpFlOn : (bool)device.OpFlOff);
        Assert.False(protectionOn ? device.onCmdActive : device.offCmdActive);
        Assert.True((bool)device.NoCon);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HeldProtection_FeedbackLossRestartsDirectionOnce(bool protectionOn)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);

        SetProtection(device, protectionOn, true);
        SetFeedback(device, protectionOn, true);
        device.Implement(command);

        Assert.False((bool)device.On);
        Assert.False((bool)device.Off);

        SetFeedback(device, protectionOn, false);
        device.Implement(command);

        Assert.Equal(protectionOn, (bool)device.On);
        Assert.Equal(!protectionOn, (bool)device.Off);
        Assert.Equal(protectionOn, device.onCmdActive);
        Assert.Equal(!protectionOn, device.offCmdActive);
        Assert.False((bool)device.Stp);

        device.Implement(command);
        Assert.Equal(0.1, protectionOn ? device.onToverTimer : device.offToverTimer, 6);
    }

    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, false)]
    [InlineData(2u, false)]
    public void SimultaneousProtection_UsesExistingPriority(uint outPriority, bool expectOn)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);
        device.OutPri = outPriority;
        device.POn[0] = true;
        device.POff[0] = true;

        device.Implement(command);

        Assert.Equal(expectOn, (bool)device.On);
        Assert.Equal(!expectOn, (bool)device.Off);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Forbid_BlocksProtection(bool protectionOn)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);
        device.FDev[0] = true;
        SetProtection(device, protectionOn, true);

        device.Implement(command);

        Assert.True((bool)device.Forbid);
        Assert.True((bool)device.NoCon);
        Assert.False((bool)device.On);
        Assert.False((bool)device.Off);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OrdinaryCommand_FeedbackStillHonorsResetModeTwo(bool opening)
    {
        var device = CreateDevice(stopResetMode: false);
        var command = new TestCommand(0.1f);
        device.ResetM = 2;
        device.OutM = true;

        SetMovementCommand(device, opening, true);
        device.Implement(command);
        SetMovementCommand(device, opening, false);
        device.Implement(command);
        SetFeedback(device, opening, true);
        device.Implement(command);

        Assert.Equal(opening, (bool)device.On);
        Assert.Equal(!opening, (bool)device.Off);
        Assert.False(device.onCmdActive);
        Assert.False(device.offCmdActive);
        Assert.True((bool)device.Stp);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void EndpointFeedback_EndsMovementAndSetsStoppedOutput(
        bool opening, bool stopResetMode)
    {
        var device = CreateDevice(stopResetMode);
        var command = new TestCommand(0.1f);

        device.Implement(command);
        SetMovementCommand(device, opening, true);
        device.Implement(command);
        SetMovementCommand(device, opening, false);
        device.Implement(command);

        Assert.False((bool)device.Stp);
        Assert.False(IsTagBitSet(device, 18));

        if (opening)
            device.FBOn[0] = true;
        else
            device.FBOff[0] = true;
        device.Implement(command);

        Assert.False(device.onCmdActive);
        Assert.False(device.offCmdActive);
        Assert.True((bool)device.Stp);
        Assert.True(IsTagBitSet(device, 18));
        Assert.True(IsTagBitSet(device, opening ? 10 : 11));
    }

    private static DEVICE CreateDevice(bool stopResetMode) => new()
    {
        StopR = stopResetMode,
        EnStp = { [0] = true },
        SetT = 0.5,
        Tover = 1.0,
        TripM = 4,
    };

    private static void SetMovementCommand(DEVICE device, bool opening, bool value)
    {
        if (opening)
            device.CON[0] = value;
        else
            device.COF[0] = value;
    }

    private static void SetProtection(DEVICE device, bool protectionOn, bool value)
    {
        if (protectionOn)
            device.POn[0] = value;
        else
            device.POff[0] = value;
    }

    private static void SetFeedback(DEVICE device, bool feedbackOn, bool value)
    {
        if (feedbackOn)
            device.FBOn[0] = value;
        else
            device.FBOff[0] = value;
    }

    private static bool IsTagBitSet(DEVICE device, int bit)
    {
        uint tag = (uint)device.TAG.Value;
        return (tag & (1u << bit)) != 0;
    }

    private sealed class TestCommand : ICommand
    {
        public TestCommand(float cycle)
        {
            Dpu = new TestDpu(cycle);
        }

        public string Name => "DEVICE_TEST";

        public IDpu Dpu { get; }
    }

    private sealed class TestDpu : IDpu
    {
        public TestDpu(float cycle)
        {
            Cycle = cycle;
        }

        public float Cycle { get; }

        public uint CycleCount => 0;

        public string Name => "DPU_TEST";
    }
}
