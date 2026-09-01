using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class EDEVICEM
    {
        protected override void Run(ICommand cmd)
        {
            if (!Enable) return;

            float dt = cmd.Dpu.Cycle; // 运算周期 (秒)

            // 1. 提取输入状态，避免装箱拆箱带来的 GC
            bool bFBOn = FBOn;
            bool bFBOff = FBOff;
            bool bLoc = Loc;
            bool bFBat = FBat;
            bool bFDev = FDev;
            bool bFSpr = FSpr;
            bool bToM = ToM;
            bool bReqA = ReqA;

            // 首次运行初始化
            if (firstRun)
            {
                MA[0] = true;
                oldFBOn = bFBOn;
                oldFBOff = bFBOff;
                firstRun = false;
            }

            // =========================================================================
            // 2. 消费 HMI 非动作类指令 (Ack / Forbid / Debug)
            // =========================================================================
            if (hmiCmdAck || hmiCmdReset)
            {
                Trip[0] = false;
                OpFlOn[0] = false;
                OpFlOff[0] = false;
            }
            // 复位 (Reset)：在 Ack 基础上额外清除指令活动状态与定时器，
            // 避免设备卡在行程中无法退出（与 FC_DEVICE_RUN 的 CRS 行为对齐）
            if (hmiCmdReset)
            {
                onCmdActive = false;
                offCmdActive = false;
                onTimer = 0; offTimer = 0;
                onToverTimer = 0; offToverTimer = 0;
                On[0] = false;
                Off[0] = false;
            }
            if (hmiCmdForbid) manualForbid = !manualForbid; // Toggle
            if (hmiCmdDebug) debugMode = !debugMode;        // Toggle

            // =========================================================================
            // 3. 计算综合限制与故障状态
            // =========================================================================
            bool forbid = manualForbid || (EnLoc && bLoc) || (EnFBat && bFBat) || (EnFDev && bFDev);
            Forbid[0] = forbid;

            FBFl[0] = (bFBOn && bFBOff); // 硬件反馈异常

            bool bOpFlOn = OpFlOn;
            bool bOpFlOff = OpFlOff;
            bool opFl = bOpFlOn || bOpFlOff;
            OpFl[0] = opFl;

            // 跳闸逻辑: 无分闸指令且不处于就地，合闸反馈由 1 变 0
            if (!offCmdActive && !bLoc && oldFBOn && !bFBOn)
            {
                Trip[0] = true;
            }
            bool bTrip = Trip;

            // 输出闭锁指示 NoCon
            // FLB=0: 操作失败、跳闸、禁操任一为 1 时闭锁； FLB=1: 仅禁操为 1 时闭锁
            bool noCon = FLB ? forbid : (opFl || bTrip || forbid);
            NoCon[0] = noCon;

            // =========================================================================
            // 4. 手动/自动模式仲裁
            // =========================================================================
            if (!bLoc)
            {
                if (bToM) MA[0] = true;
                else if (bReqA) MA[0] = false;
                else
                {
                    // 投自动/切手动 HMI 指令
                    if (hmiCmdManual) MA[0] = true;
                    if (hmiCmdAuto) MA[0] = false;
                }

                // MP=1: HMI 手动操作指令同时将设备切换到手动模式
                if (MP == 1 && (hmiCmdOn || hmiCmdOff))
                    MA[0] = true;
            }
            bool bMA = MA;

            // =========================================================================
            // 5. 操作触发仲裁
            // =========================================================================
            bool triggerOn = false;
            bool triggerOff = false;

            if (!noCon && !bLoc)
            {
                // HMI 手动操作指令: 按 MP 参数行为分支
                //   MP=0: 不切手动, 但执行
                //   MP=1: 切手动并执行 (MA 已在上方切换)
                //   MP=2: 不切手动, 也不执行
                if (MP != 2)
                {
                    if (hmiCmdOn & EnOn) triggerOn = true;
                    if (hmiCmdOff & EnOff) triggerOff = true;
                }

                // 自动模式下响应逻辑引脚指令
                if (!bMA)
                {
                    if (AOn & EnOn) triggerOn = true;
                    if (AOff & EnOff) triggerOff = true;
                }
            }

            // 弹簧未储能，闭锁合闸
            if (triggerOn && EnFSpr && bFSpr)
            {
                triggerOn = false;
            }

            // =========================================================================
            // 6. 脉冲输出与操作失败状态机
            // =========================================================================
            double effTover = Tover < SetT ? SetT : Tover; // 防呆设计

            if (triggerOn && !onCmdActive && !offCmdActive)
            {
                onCmdActive = true; onTimer = 0; onToverTimer = 0;
                OpFlOn[0] = false; Trip[0] = false;
                On[0] = true; Off[0] = false;
            }
            else if (triggerOff && !offCmdActive && !onCmdActive)
            {
                offCmdActive = true; offTimer = 0; offToverTimer = 0;
                OpFlOff[0] = false; Trip[0] = false;
                Off[0] = true; On[0] = false;
            }

            // 合闸状态机
            if (onCmdActive)
            {
                onTimer += dt;
                onToverTimer += dt;
                if (onTimer >= SetT) On[0] = false;

                if (bFBOn) // 反馈到位
                {
                    if (ResetM == 0 || ResetM == 1) On[0] = false;
                    onCmdActive = false;
                }
                else if (onToverTimer >= effTover) // 超时
                {
                    OpFlOn[0] = true;
                    if (ResetM == 0) On[0] = false;
                    onCmdActive = false;
                }
            }

            // 分闸状态机
            if (offCmdActive)
            {
                offTimer += dt;
                offToverTimer += dt;
                if (offTimer >= SetT) Off[0] = false;

                if (bFBOff) // 反馈到位
                {
                    if (ResetM == 0 || ResetM == 1) Off[0] = false;
                    offCmdActive = false;
                }
                else if (offToverTimer >= effTover) // 超时
                {
                    OpFlOff[0] = true;
                    if (ResetM == 0) Off[0] = false;
                    offCmdActive = false;
                }
            }

            // =========================================================================
            // 7. 组装 32 位 PACK 状态打包点 (供 HMI 全景调用，对应手册 EDEVICEM 映射位)
            // =========================================================================
            uint packStatus = 0;

            if (bToM) packStatus |= (1u << 0);
            if (bReqA) packStatus |= (1u << 1);
            if (EnOn) packStatus |= (1u << 2);
            if (EnOff) packStatus |= (1u << 3);
            // Bit 4 备用
            if (POpe) packStatus |= (1u << 5);
            if (bFSpr) packStatus |= (1u << 6);
            if (AOn) packStatus |= (1u << 7);
            if (AOff) packStatus |= (1u << 8);
            // Bit 9 备用
            if (bFBOn) packStatus |= (1u << 10);
            if (bFBOff) packStatus |= (1u << 11);
            // Bit 12 备用
            if (bLoc) packStatus |= (1u << 13);
            if (bFBat) packStatus |= (1u << 14);
            if (bFDev) packStatus |= (1u << 15);
            if (On) packStatus |= (1u << 16);
            if (Off) packStatus |= (1u << 17);
            // Bit 18 备用
            if (bMA) packStatus |= (1u << 19);
            if (debugMode) packStatus |= (1u << 20);
            if (FBFl) packStatus |= (1u << 21);
            if (bTrip) packStatus |= (1u << 22);
            if (opFl) packStatus |= (1u << 23);
            if (forbid) packStatus |= (1u << 24);

            // Bit 25: 自动请求指示 (逻辑有AOn/AOff指令，但设备处于手动状态)
            if ((AOn | AOff) && bMA) packStatus |= (1u << 25);

            if (manualForbid) packStatus |= (1u << 26);
            // Bit 27 备用
            if (onCmdActive || offCmdActive) packStatus |= (1u << 28);
            if (onCmdActive) packStatus |= (1u << 29);
            if (offCmdActive) packStatus |= (1u << 30);
            if (bFDev || opFl) packStatus |= (1u << 31);

            // 输出浮点型的 PACK，上位机再按 Bit 解析
            TAG.Value = (float)packStatus;

            // =========================================================================
            // 8. 历史状态与 HMI 指令自解扣 (Consumer Pattern 消费机制)
            // =========================================================================
            oldFBOn = bFBOn;
            oldFBOff = bFBOff;

            // 无论指令是否被接受，在此周期结束后统一清零，避免指令卡死
            hmiCmdOn = false;
            hmiCmdOff = false;
            hmiCmdAuto = false;
            hmiCmdManual = false;
            hmiCmdAck = false;
            hmiCmdReset = false;
            hmiCmdForbid = false;
            hmiCmdDebug = false;
        }
    }
}