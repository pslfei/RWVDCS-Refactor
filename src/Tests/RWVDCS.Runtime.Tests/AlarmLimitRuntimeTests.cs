using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

public sealed class AlarmLimitRuntimeTests
{
    [Fact]
    public void RuntimeBuilder_injects_alarm_limits_and_exposes_dynamic_state()
    {
        using var runtime = RuntimeBuilder.Build(
            BuildEngineeringModel(),
            new BlockCatalog(typeof(AlarmLimitRuntimeTests).Assembly));

        PointSlotRef slot = runtime.Dpus[0].LocalSlots["AI001"];
        ref readonly LA la = ref slot.Arena.GetRef<LA>(slot.Sid);

        Assert.Equal(90d, la.HighAlarmLimit3Value);
        Assert.Equal(80d, la.HighAlarmLimit2Value);
        Assert.Equal(70d, la.HighAlarmLimit1Value);
        Assert.Equal(-90d, la.LowAlarmLimit3Value);
        Assert.Equal(-80d, la.LowAlarmLimit2Value);
        Assert.Equal(-70d, la.LowAlarmLimit1Value);
        Assert.Equal(6, la.CurOverState);

        Assert.True(PointFieldAccess.TryRead(
            slot,
            nameof(LA.CurOverState),
            out object? state,
            out Type? stateType));
        Assert.Equal(typeof(int), stateType);
        Assert.Equal(6, state);

        slot.WriteBoxedBuffer(-95f);
        Assert.Equal(7, slot.Arena.GetRef<LA>(slot.Sid).CurOverState);
    }

    [Fact]
    public void PointFieldAccess_hides_engineering_limits_and_computed_state_is_read_only()
    {
        using var runtime = RuntimeBuilder.Build(
            BuildEngineeringModel(),
            new BlockCatalog(typeof(AlarmLimitRuntimeTests).Assembly));

        PointSlotRef slot = runtime.Dpus[0].LocalSlots["AI001"];

        Assert.False(PointFieldAccess.TryRead(
            slot,
            nameof(LA.HighAlarmLimit3Value),
            out _,
            out _));

        Assert.False(PointFieldAccess.WriteObject(slot, nameof(LA.HighAlarmLimit3Value), 120d));
        Assert.Equal(90d, slot.Arena.GetRef<LA>(slot.Sid).HighAlarmLimit3Value);
        Assert.False(PointFieldAccess.WriteObject(slot, nameof(LA.CurOverState), 3));

        List<PointFieldAccess.PointField> fields = PointFieldAccess.ReadAll(slot);
        Assert.DoesNotContain(
            fields,
            field => field.Name == "highAlarmLimit3Value");
        Assert.Contains(fields, field => field.Name == nameof(LA.CurOverState) && Equals(field.Value, 6));
    }

    private static EngineeringModel BuildEngineeringModel() => new()
    {
        ProjectPath = "alarm-limit-runtime-test",
        Controllers =
        [
            new ControllerModel
            {
                Id = 1,
                Address = "1",
                Name = "DPU1",
                Points =
                [
                    new PointModel
                    {
                        Name = "AI001",
                        DataType = "LA",
                        DefaultValue = 95f,
                        MaxValue = 1000f,
                        MinValue = -1000f,
                        HighAlarmLimit3Value = 90d,
                        HighAlarmLimit2Value = 80d,
                        HighAlarmLimit1Value = 70d,
                        LowAlarmLimit3Value = -90d,
                        LowAlarmLimit2Value = -80d,
                        LowAlarmLimit1Value = -70d,
                    },
                ],
                Blocks = [],
            },
        ],
    };
}
