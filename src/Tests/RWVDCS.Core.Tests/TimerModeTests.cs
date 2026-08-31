using RWVDCS.Blocks.RW;
using RWVDCS.Core.Blocks;

namespace RWVDCS.Core.Tests;

public sealed class TimerModeTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    public void IdleState_ClearsErroneousDefaultHighOutput(uint mode)
    {
        TIMER timer = CreateTimer(mode, time: 1.0f);
        timer.OUT[0] = true;

        Step(timer);

        Assert.False((bool)timer.OUT);
    }

    [Fact]
    public void Mode0_DelaysThenEmitsExactlyOneCyclePulse_AndIgnoresRetriggerWhileTiming()
    {
        TIMER timer = CreateTimer(mode: 0, time: 0.3f);

        SetX(timer, true);
        Step(timer); // 上升沿，仅启动延时
        Assert.False((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);

        SetX(timer, false);
        Step(timer); // 0.1 s
        Assert.False((bool)timer.OUT);

        SetX(timer, true);
        Step(timer); // 0.2 s；计时期间的新上升沿必须被忽略
        Assert.False((bool)timer.OUT);

        Step(timer); // 0.3 s，输出一个周期脉冲
        Assert.True((bool)timer.OUT);

        Step(timer); // 下一周期自动清零
        Assert.False((bool)timer.OUT);
    }

    [Fact]
    public void Mode1_OutputsImmediateRetriggerablePulse_WithAtLeastOneVisibleCycle()
    {
        TIMER timer = CreateTimer(mode: 1, time: 0.2f);

        SetX(timer, true);
        Step(timer);
        Assert.True((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);

        SetX(timer, false);
        Step(timer);
        Assert.True((bool)timer.OUT);
        Assert.Equal(0.1f, (float)timer.TRun, precision: 5);

        SetX(timer, true);
        Step(timer); // 重触发后重新从 0 计时
        Assert.True((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);

        Step(timer);
        Assert.True((bool)timer.OUT);
        Step(timer);
        Assert.False((bool)timer.OUT);

        TIMER shortPulse = CreateTimer(mode: 1, time: 0.01f);
        SetX(shortPulse, true);
        Step(shortPulse);
        Assert.True((bool)shortPulse.OUT); // TIME < Cycle 时仍至少可见一个周期
        Step(shortPulse);
        Assert.False((bool)shortPulse.OUT);
    }

    [Fact]
    public void Mode2_DelaysOnAndResetsImmediatelyWhenInputFalls()
    {
        TIMER timer = CreateTimer(mode: 2, time: 0.2f);

        SetX(timer, true);
        Step(timer);
        Assert.False((bool)timer.OUT);

        Step(timer);
        Assert.False((bool)timer.OUT);
        Assert.Equal(0.1f, (float)timer.TRun, precision: 5);

        Step(timer);
        Assert.True((bool)timer.OUT); // 达到 TIME 的当前周期立即置位

        SetX(timer, false);
        Step(timer);
        Assert.False((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);
    }

    [Fact]
    public void Mode3_DelaysOff_AndHighInputCancelsPendingOffDelay()
    {
        TIMER timer = CreateTimer(mode: 3, time: 0.2f);

        SetX(timer, true);
        Step(timer);
        Assert.True((bool)timer.OUT);

        SetX(timer, false);
        Step(timer);
        Assert.True((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);

        Step(timer);
        Assert.True((bool)timer.OUT);

        SetX(timer, true); // 延时期间重新接通，取消本次延时关
        Step(timer);
        Assert.True((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);

        SetX(timer, false);
        Step(timer);
        Step(timer);
        Assert.True((bool)timer.OUT);
        Step(timer);
        Assert.False((bool)timer.OUT);
    }

    [Fact]
    public void Mode4_DelaysOnThenHoldsUntilReset_AndRetriggersOnlyDuringDelay()
    {
        TIMER timer = CreateTimer(mode: 4, time: 0.2f);

        SetX(timer, true);
        Step(timer);
        Assert.False((bool)timer.OUT);

        SetX(timer, false);
        Step(timer);
        Assert.False((bool)timer.OUT);

        SetX(timer, true); // 延时期间重触发
        Step(timer);
        Assert.False((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);

        Step(timer);
        Assert.False((bool)timer.OUT);
        Step(timer);
        Assert.True((bool)timer.OUT);

        SetX(timer, false);
        Step(timer);
        Assert.True((bool)timer.OUT); // X 复位不影响保持输出

        timer.RST[0] = true;
        Step(timer);
        Assert.False((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);

        SetX(timer, true);
        Step(timer);
        Assert.False((bool)timer.OUT); // RST 持续为高时不能重新置位
    }

    [Fact]
    public void Mode4_PreservesCompletedStateFromPreviousImplementationOnFirstExecution()
    {
        TIMER timer = CreateTimer(mode: 4, time: 1.0f);
        timer.TRun[0] = 1.0f;
        timer.OUT[0] = true;

        Step(timer);

        Assert.True((bool)timer.OUT);
    }

    [Fact]
    public void ZeroTime_DoesNotCreateOutputWithoutTrigger_AndCompletesOnTrigger()
    {
        foreach (uint mode in new[] { 0u, 2u, 4u })
        {
            TIMER timer = CreateTimer(mode, time: 0.0f);
            timer.OUT[0] = true;

            Step(timer);
            Assert.False((bool)timer.OUT);

            SetX(timer, true);
            Step(timer);
            Assert.True((bool)timer.OUT);
        }
    }

    [Fact]
    public void ModeChangeAndInvalidMode_ClearPreviousState()
    {
        TIMER timer = CreateTimer(mode: 3, time: 1.0f);
        SetX(timer, true);
        Step(timer);
        Assert.True((bool)timer.OUT);

        SetX(timer, false);
        timer.MODE = 1;
        Step(timer);
        Assert.False((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);

        timer.OUT[0] = true;
        timer.MODE = 99;
        Step(timer);
        Assert.False((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    [InlineData(4u)]
    public void ResetHigh_ContinuouslyHoldsEveryModeInReset(uint mode)
    {
        TIMER timer = CreateTimer(mode, time: 1.0f);
        timer.OUT[0] = true;
        timer.TRun[0] = 0.5f;
        timer.RST[0] = true;
        SetX(timer, true);

        Step(timer);
        Step(timer);

        Assert.False((bool)timer.OUT);
        Assert.Equal(0.0f, (float)timer.TRun);
    }

    private static TIMER CreateTimer(uint mode, float time) => new()
    {
        MODE = mode,
        TIME = { [0] = time },
    };

    private static void SetX(TIMER timer, bool value) => timer.X[0] = value;

    private static void Step(TIMER timer) => timer.Implement(new TestCommand(0.1f));

    private sealed class TestCommand(float cycle) : ICommand
    {
        public string Name => "TIMER_TEST";
        public IDpu Dpu { get; } = new TestDpu(cycle);
    }

    private sealed class TestDpu(float cycle) : IDpu
    {
        public float Cycle { get; } = cycle;
        public uint CycleCount => 0;
        public string Name => "DPU_TEST";
    }
}
