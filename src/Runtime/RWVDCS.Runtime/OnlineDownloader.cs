using RWVDCS.Core.Blocks;
using RWVDCS.Core.PointStore;

namespace RWVDCS.Runtime;

/// <summary>在线下装执行结果。</summary>
public sealed class DownloadResult
{
    public bool Success { get; internal set; }
    public int PointsPreserved { get; internal set; }
    public int PointsNew { get; internal set; }
    public int PointsDropped { get; internal set; }
    public int PointsTypeChanged { get; internal set; }
    public int BlocksPreserved { get; internal set; }
    public int BlocksNew { get; internal set; }
    public int BlocksDropped { get; internal set; }
    public int BlocksTypeChanged { get; internal set; }
    public int FieldsTransferred { get; internal set; }
    public int ForcesCarried { get; internal set; }
    public int DpusNew { get; internal set; }
    public int DpusDropped { get; internal set; }
    public double TransferMs { get; internal set; }
    public List<string> Messages { get; } = [];
}

/// <summary>
/// 在线下装的状态迁移：旧运行时 → 新运行时（新工程重建后），按名保留一切能保留的运行状态。
///
/// 设计对标成熟 DCS/PLC 的 online download / online change 语义：
/// <list type="bullet">
/// <item>未变更实体保值：点按名+类别整槽保留（含质量/强制/报警字段）；块按名+功能码做字段级状态转移。</item>
/// <item>工程参数以新库为准：规格数（Constant 管脚）不从旧状态转移，取新工程装配值（DeltaV 下装语义：
/// 在线改过但未回填工程库的参数会被下装覆盖，diff 报告予以提示）。</item>
/// <item>新增块执行一次 FirstRun（初始化），已有块不重新初始化（"继续跑"）。</item>
/// <item>删除的点/块随旧运行时废弃；引用它们的连线在新装配中自然成为死绑定（兜底语义与整装一致）。</item>
/// <item>功能码变更 = 删旧建新（状态无法对应）。</item>
/// <item>管脚强制状态随块搬运（同名块 + 同功能码时）。</item>
/// <item>周期计数/扫描周期按 DPU 保留（新 DPU 用工程默认）。</item>
/// </list>
/// 调用方（宿主）负责：新运行时构建、周期边界串行化、切换后重建调度器/历史站/索引。
/// </summary>
public static class OnlineDownloader
{
    public static DownloadResult Transfer(DcsRuntime oldRt, DcsRuntime newRt)
    {
        var result = new DownloadResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 旧块 live 状态先刷进 Arena（保证 fc 字段与槽一致；转移走 fc 字段反射）
        oldRt.FlushBlockStates();

        var oldDpus = oldRt.Dpus.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var newDpus = newRt.Dpus.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

        result.DpusNew = newRt.Dpus.Count(d => !oldDpus.ContainsKey(d.Name));
        result.DpusDropped = oldRt.Dpus.Count(d => !newDpus.ContainsKey(d.Name));

        var newBlocksToInit = new List<(DpuRuntime Dpu, BlockCommand Cmd)>();

        foreach (var newDpu in newRt.Dpus)
        {
            oldDpus.TryGetValue(newDpu.Name, out var oldDpu);

            // ---- 1. 点整槽迁移（含中间 pin-point：同为真点即可迁）
            foreach (var (name, newSlot) in newDpu.LocalSlots)
            {
                if (!newSlot.IsRealPoint)
                    continue;

                PointSlotRef oldSlot = default;
                bool found = oldDpu != null && oldDpu.LocalSlots.TryGetValue(name, out oldSlot);
                if (!found && oldRt.TryGetSlot(name, out oldSlot))
                    found = true;

                if (!found || !oldSlot.IsRealPoint)
                {
                    result.PointsNew++;
                    continue;
                }

                if (oldSlot.Kind != newSlot.Kind)
                {
                    result.PointsTypeChanged++;
                    continue; // 类型变了：保留新工程初值
                }

                int sourceLength = oldSlot.Arena.GetByteLength(oldSlot.Sid);
                int destinationLength = newSlot.Arena.GetByteLength(newSlot.Sid);
                if (sourceLength == destinationLength)
                {
                    PointArena.CopySlotBetween(oldSlot.Arena, oldSlot.Sid,
                        newSlot.Arena, newSlot.Sid, sourceLength);
                    result.PointsPreserved++;
                }
            }

            // ---- 2. 块状态迁移
            Dictionary<string, BlockCommand>? oldCommands = null;
            if (oldDpu != null)
            {
                oldCommands = new Dictionary<string, BlockCommand>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in oldDpu.Commands)
                    oldCommands.TryAdd(c.Name, c);
            }

            foreach (var newCmd in newDpu.Commands)
            {
                BlockCommand? oldCmd = null;
                oldCommands?.TryGetValue(newCmd.Name, out oldCmd);

                if (oldCmd == null)
                {
                    newBlocksToInit.Add((newDpu, newCmd));
                    result.BlocksNew++;
                    continue;
                }

                if (!string.Equals(oldCmd.FcName, newCmd.FcName, StringComparison.OrdinalIgnoreCase))
                {
                    // 功能码变了：删旧建新，走初始化
                    newBlocksToInit.Add((newDpu, newCmd));
                    result.BlocksTypeChanged++;
                    continue;
                }

                // 同名同功能码：字段级状态转移（跳过规格数——新工程参数生效）
                result.FieldsTransferred += BlockHotSwapper.TransferState(oldCmd.Fc, newCmd.Fc, skipConstants: true);
                // 转移的是"继续跑"状态：装配期的一次性默认值不再生效
                foreach (var b in newCmd.InputBindings)
                    b.PendingBufferValue = null;

                if (oldCmd.ForceStates is { Count: > 0 })
                {
                    newCmd.CopyForceStateFrom(oldCmd);
                    result.ForcesCarried += oldCmd.ForceStates.Count;
                }

                result.BlocksPreserved++;
            }

            // ---- 3. 周期与计数保留
            if (oldDpu != null)
            {
                newDpu.Cycle = oldDpu.Cycle;
                newDpu.CycleCount = oldDpu.CycleCount;
            }
        }

        // 丢弃统计（信息性）
        foreach (var oldDpu in oldRt.Dpus)
        {
            if (!newDpus.TryGetValue(oldDpu.Name, out var newDpu))
            {
                result.PointsDropped += oldDpu.LocalSlots.Count(kv => kv.Value.IsRealPoint);
                result.BlocksDropped += oldDpu.Commands.Count;
                continue;
            }
            foreach (var (name, slot) in oldDpu.LocalSlots)
            {
                if (slot.IsRealPoint && !newDpu.LocalSlots.ContainsKey(name))
                    result.PointsDropped++;
            }
            foreach (var cmd in oldDpu.Commands)
            {
                if (newDpu.FindCommand(cmd.Name) == null)
                    result.BlocksDropped++;
            }
        }

        // ---- 4. 新增块初始化：命令序 FirstRun 一次（老系统下装后 InitCommand+FirstRun 的等价物，
        //         只对新块执行——已有块"继续跑"不重初始化）
        foreach (var (dpu, cmd) in newBlocksToInit)
        {
            try
            {
                cmd.FirstRun();
            }
            catch (Exception ex)
            {
                result.Messages.Add($"[{dpu.Name}] 新块 {cmd.Name}({cmd.FcName}) FirstRun 异常：{ex.Message}");
            }
        }

        // ---- 5. 块状态整体刷回新 Arena（保证槽与 live 一致，快照立即可用）
        newRt.FlushBlockStates();

        sw.Stop();
        result.TransferMs = sw.Elapsed.TotalMilliseconds;
        result.Success = true;
        return result;
    }
}
