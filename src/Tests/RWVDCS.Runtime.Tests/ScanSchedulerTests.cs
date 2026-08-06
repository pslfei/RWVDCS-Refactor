using RWVDCS.Core.Blocks;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

public sealed class ScanSchedulerTests
{
    [Fact]
    public async Task Stop_WaitsUntilInFlightCycleHasReallyExited()
    {
        using var runtime = BuildRuntime();
        using var scheduler = new ScanScheduler(runtime);
        using var cycleEntered = new ManualResetEventSlim();
        using var releaseCycle = new ManualResetEventSlim();

        scheduler.AfterDpuStep = _ =>
        {
            cycleEntered.Set();
            releaseCycle.Wait(TimeSpan.FromSeconds(10));
        };
        scheduler.Start();
        Assert.True(cycleEntered.Wait(TimeSpan.FromSeconds(5)), "扫描线程没有进入测试周期。");

        Task stopTask = Task.Run(scheduler.Stop);
        try
        {
            // 旧实现 Join(2000) 会在扫描周期尚未结束时错误返回；RuntimeHost 随后便会
            // 释放 Arena。等待略大于旧超时，确保这个生命周期回归能稳定暴露。
            await Task.Delay(TimeSpan.FromMilliseconds(2200));
            Assert.False(stopTask.IsCompleted, "扫描周期仍在执行时 Stop 不应返回。");
        }
        finally
        {
            releaseCycle.Set();
        }

        await stopTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ScanState.Stopped, scheduler.State);
    }

    private static DcsRuntime BuildRuntime()
    {
        var model = new EngineeringModel
        {
            ProjectPath = "scheduler-lifecycle-test",
            Controllers =
            [
                new ControllerModel
                {
                    Id = 1,
                    Address = "1",
                    Name = "DPU1",
                    Points = [],
                    Blocks = [],
                },
            ],
        };
        return RuntimeBuilder.Build(model, new BlockCatalog(typeof(ScanSchedulerTests).Assembly));
    }
}
