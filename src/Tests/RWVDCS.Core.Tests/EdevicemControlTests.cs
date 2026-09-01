using System.Reflection;
using RWVDCS.Blocks.RW;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Core.Tests;

public sealed class EdevicemControlTests
{
    [Fact]
    public void FieldContract_MatchesEngineeringAndHmiPins()
    {
        Type type = typeof(EDEVICEM);
        string[] inputNames =
        [
            "Enable", "EnOn", "EnOff", "ToM", "ReqA", "AOn", "AOff",
            "FBOn", "FBOff", "Loc", "FBat", "FDev", "POpe", "FSpr",
            "CON", "COF", "CTA", "CTM", "CAK", "CFB", "CRS", "CDB",
        ];

        foreach (string name in inputNames)
        {
            FieldInfo field = type.GetField(name)!;
            Assert.NotNull(field);
            Assert.Equal(typeof(LD), field.FieldType);
            Assert.Equal(PinTypes.Input, field.GetCustomAttribute<PinTypeAttribute>()!.PinType);
        }

        FieldInfo tag = type.GetField("TAG")!;
        Assert.Equal(typeof(LP32), tag.FieldType);
        Assert.Equal(PinTypes.Output, tag.GetCustomAttribute<PinTypeAttribute>()!.PinType);

        foreach (string name in new[]
                 {
                     "On", "Off", "MA", "NoCon", "FBFl", "Trip", "OpFl",
                     "Forbid", "OpFlOn", "OpFlOff",
                 })
        {
            FieldInfo field = type.GetField(name)!;
            Assert.Equal(typeof(LD), field.FieldType);
            Assert.Equal(PinTypes.Output, field.GetCustomAttribute<PinTypeAttribute>()!.PinType);
        }

        FieldInfo quality = type.GetField("QualityT")!;
        Assert.Equal(typeof(uint), quality.FieldType);
        Assert.Equal(PinTypes.Constant, quality.GetCustomAttribute<PinTypeAttribute>()!.PinType);

        Assert.Null(type.GetField("hmiCmdOn"));
        Assert.Null(type.GetField("hmiCmdOff"));
    }

    [Fact]
    public void ModeCommandsAndManualPriority_FollowDocumentedPrecedence()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        Step(device, command);

        device.CTA[0] = true;
        Step(device, command);
        Assert.False((bool)device.MA);

        device.CTA[0] = false;
        device.CTM[0] = true;
        Step(device, command);
        Assert.True((bool)device.MA);

        device.ToM[0] = true;
        device.ReqA[0] = true;
        Step(device, command);
        Assert.True((bool)device.MA);

        device.MP = 2;
        device.ToM[0] = false;
        device.ReqA[0] = false;
        device.CON[0] = true;
        Step(device, command);
        Assert.False((bool)device.On);
        Assert.False(device.onCmdActive);
    }

    [Fact]
    public void AckClearsFaultsWhileResetAlsoCancelsActiveCommand()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        Step(device, command);
        PulseOn(device, command);
        device.OpFlOn[0] = true;
        device.Trip[0] = true;

        device.CAK[0] = true;
        Step(device, command);
        Assert.False((bool)device.OpFlOn);
        Assert.False((bool)device.Trip);
        Assert.True(device.onCmdActive);

        device.CAK[0] = false;
        device.CRS[0] = true;
        Step(device, command);
        Assert.False(device.onCmdActive);
        Assert.False(device.onPulseActive);
        Assert.False((bool)device.On);
        Assert.Equal(0.0, device.onToverTimer, 6);
    }

    [Fact]
    public void ZeroSetTime_StillProducesOneVisibleCommandCycle()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        device.SetT = 0;
        Step(device, command);

        PulseOn(device, command);
        Assert.True((bool)device.On);

        Step(device, command);
        Assert.False((bool)device.On);
    }

    [Fact]
    public void HeldHmiPulse_TriggersOnlyOnRisingEdgeAndDoesNotResetTravelTimer()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        Step(device, command);

        device.CON[0] = true;
        Step(device, command);
        Assert.True((bool)device.On);
        Assert.True(device.onCmdActive);
        Assert.Equal(0.0, device.onToverTimer, 6);

        Step(device, command);
        Assert.Equal(0.1, device.onToverTimer, 6);
        Step(device, command);
        Assert.Equal(0.2, device.onToverTimer, 6);
    }

    [Fact]
    public void ToggleCommands_OnlyToggleOnceWhileInputRemainsHigh()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        Step(device, command);

        device.CFB[0] = true;
        Step(device, command);
        Assert.True(device.manualForbid);
        Step(device, command);
        Assert.True(device.manualForbid);

        device.CFB[0] = false;
        Step(device, command);
        device.CFB[0] = true;
        Step(device, command);
        Assert.False(device.manualForbid);

        device.CDB[0] = true;
        Step(device, command);
        Assert.True(IsTagBitSet(device, 20));
        Step(device, command);
        Assert.True(IsTagBitSet(device, 20));
    }

    [Fact]
    public void ResetModeTwo_FeedbackEndsTravelButPulseStillExpiresAtSetTime()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        device.ResetM = 2;
        device.SetT = 0.3;
        Step(device, command);

        PulseOn(device, command);
        device.FBOn[0] = true;
        Step(device, command);

        Assert.False(device.onCmdActive);
        Assert.True(device.onPulseActive);
        Assert.True((bool)device.On);

        Step(device, command);
        Assert.True((bool)device.On);
        Step(device, command);
        Assert.False((bool)device.On);
        Assert.False(device.onPulseActive);
    }

    [Fact]
    public void Timeout_UpdatesOperationFailureNoConAndTagInSameCycle()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        device.SetT = 0.1;
        device.Tover = 0.2;
        Step(device, command);

        PulseOn(device, command);
        Step(device, command);
        Assert.False((bool)device.OpFl);

        Step(device, command);
        Assert.True((bool)device.OpFlOn);
        Assert.True((bool)device.OpFl);
        Assert.True((bool)device.NoCon);
        Assert.True(IsTagBitSet(device, 23));
        Assert.False(device.onCmdActive);
    }

    [Fact]
    public void EnLocFalse_AllowsOperationEvenWhenLocInputIsHigh()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        device.Loc[0] = true;
        Step(device, command);

        PulseOn(device, command);
        Assert.False((bool)device.On);

        device.CON[0] = false;
        Step(device, command);
        device.EnLoc = false;
        PulseOn(device, command);
        Assert.True((bool)device.On);
        Assert.True(device.onCmdActive);
    }

    [Fact]
    public void Forbid_ReleasesPhysicalOutputButKeepsTravelMonitoring()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        Step(device, command);
        PulseOn(device, command);

        device.FBat[0] = true;
        Step(device, command);

        Assert.True((bool)device.Forbid);
        Assert.False((bool)device.On);
        Assert.True(device.onCmdActive);

        device.FBOn[0] = true;
        Step(device, command);
        Assert.False(device.onCmdActive);
        Assert.False((bool)device.Trip);
    }

    [Fact]
    public void SpringBlocksOnlyOnAndSimultaneousAutomaticRequestsPreferOff()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        Step(device, command);

        device.FSpr[0] = true;
        PulseOn(device, command);
        Assert.False((bool)device.On);

        device.CON[0] = false;
        device.FSpr[0] = false;
        device.MA[0] = false;
        device.AOn[0] = true;
        device.AOff[0] = true;
        Step(device, command);

        Assert.False((bool)device.On);
        Assert.True((bool)device.Off);
        Assert.True(device.offCmdActive);
    }

    [Fact]
    public void TargetFeedback_PreventsRepeatedAutomaticOutput()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        Step(device, command);
        device.MA[0] = false;
        device.AOn[0] = true;

        Step(device, command);
        Assert.True(device.onCmdActive);

        device.FBOn[0] = true;
        Step(device, command);
        Assert.False(device.onCmdActive);

        for (int i = 0; i < 3; i++)
            Step(device, command);

        Assert.False(device.onCmdActive);
        Assert.False((bool)device.On);
    }

    [Fact]
    public void UnexpectedFeedbackLossTripsButAuthorizedOffDoesNot()
    {
        var command = new TestCommand(0.1f);
        EDEVICEM unexpected = CreateDevice();
        unexpected.FBOn[0] = true;
        Step(unexpected, command);
        unexpected.FBOn[0] = false;
        Step(unexpected, command);
        Assert.True((bool)unexpected.Trip);

        EDEVICEM authorized = CreateDevice();
        authorized.FBOn[0] = true;
        Step(authorized, command);
        authorized.COF[0] = true;
        Step(authorized, command);
        authorized.FBOn[0] = false;
        Step(authorized, command);
        Assert.False((bool)authorized.Trip);
    }

    [Fact]
    public void PackedStatus_PreservesBit31AndSuppressesOldEndpointDuringTravel()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        device.FBOff[0] = true;
        Step(device, command);
        Assert.True(IsTagBitSet(device, 11));

        PulseOn(device, command);
        Assert.True(IsTagBitSet(device, 29));
        Assert.False(IsTagBitSet(device, 10));
        Assert.False(IsTagBitSet(device, 11));

        device.FDev[0] = true;
        device.ToM[0] = true;
        Step(device, command);
        Assert.True(IsTagBitSet(device, 31));
        Assert.True(IsTagBitSet(device, 15));
        Assert.True(IsTagBitSet(device, 0));
        Assert.True(IsTagBitSet(device, 2));
    }

    [Fact]
    public void QualityTransfer_ImplementsNoOrAndModes()
    {
        EDEVICEM device = CreateDevice();
        var command = new TestCommand(0.1f);
        Step(device, command);

        device.FBOn.Quality = QualityTypes.Bad;
        device.QualityT = 0;
        Step(device, command);
        Assert.Equal(QualityTypes.Good, device.On.Quality);

        device.QualityT = 1;
        Step(device, command);
        Assert.Equal(QualityTypes.Bad, device.On.Quality);
        Assert.Equal(QualityTypes.Bad, device.TAG.Quality);

        device.QualityT = 2;
        Step(device, command);
        Assert.Equal(QualityTypes.Good, device.On.Quality);

        SetAllControlInputQualities(device, QualityTypes.Bad);
        Step(device, command);
        Assert.Equal(QualityTypes.Bad, device.On.Quality);
        Assert.Equal(QualityTypes.Bad, device.TAG.Quality);
    }

    private static EDEVICEM CreateDevice() => new()
    {
        SetT = 0.5,
        Tover = 1.0,
    };

    private static void PulseOn(EDEVICEM device, TestCommand command)
    {
        device.CON[0] = true;
        Step(device, command);
    }

    private static void Step(EDEVICEM device, TestCommand command) => device.Implement(command);

    private static bool IsTagBitSet(EDEVICEM device, int bit)
    {
        uint tag = (uint)device.TAG.Value;
        return (tag & (1u << bit)) != 0;
    }

    private static void SetAllControlInputQualities(EDEVICEM device, QualityTypes quality)
    {
        device.Enable.Quality = quality;
        device.EnOn.Quality = quality;
        device.EnOff.Quality = quality;
        device.ToM.Quality = quality;
        device.ReqA.Quality = quality;
        device.AOn.Quality = quality;
        device.AOff.Quality = quality;
        device.FBOn.Quality = quality;
        device.FBOff.Quality = quality;
        device.Loc.Quality = quality;
        device.FBat.Quality = quality;
        device.FDev.Quality = quality;
        device.POpe.Quality = quality;
        device.FSpr.Quality = quality;
    }

    private sealed class TestCommand(float cycle) : ICommand
    {
        public string Name => "EDEVICEM_TEST";
        public IDpu Dpu { get; } = new TestDpu(cycle);
    }

    private sealed class TestDpu(float cycle) : IDpu
    {
        public float Cycle { get; } = cycle;
        public uint CycleCount => 0;
        public string Name => "DPU_TEST";
    }
}
