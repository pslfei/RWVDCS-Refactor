using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class DEVICE
    {
        //// 声明与上位机 HMI 画面对接的状态打包点
        //// 严格遵循说明书中的 PACK 定义表 (Bit0 ~ Bit31)
        //public uint PACK = 0;

        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            float cycle = cmd.Dpu.Cycle;

            // 提取当前周期的输入状态，避免重复装箱/拆箱带来性能损耗
            bool curFBOn = FBOn;
            bool curFBOff = FBOff;
            bool curFBStp = FBStp;
            bool curLoc = Loc;
            bool curPOn = POn;
            bool curPOff = POff;

            // ================== 1. 首次运行初始化 ==================
            if (firstRun)
            {
                MA[0] = true; // 启动时默认进入手动方式
                if (!OutM)
                {
                    On[0] = false;
                    Off[0] = false;
                    Stp[0] = !StopR;
                }
                else
                {
                    On[0] = curFBOn;
                    Off[0] = curFBOff;
                    Stp[0] = StopR ? curFBStp : true;
                }
                firstRun = false;
            }

            double effectiveTover = Tover < SetT ? SetT : Tover;

            // ================== 1.5 HMI 命令上升沿采集 ==================
            bool curCON = CON, edgeCON = curCON && !oldCON;
            bool curCOF = COF, edgeCOF = curCOF && !oldCOF;
            bool curCSP = CSP, edgeCSP = curCSP && !oldCSP;
            bool curCTA = CTA, edgeCTA = curCTA && !oldCTA;
            bool curCTM = CTM, edgeCTM = curCTM && !oldCTM;
            bool curCAK = CAK, edgeCAK = curCAK && !oldCAK;
            bool curCFB = CFB, edgeCFB = curCFB && !oldCFB;
            bool curCRS = CRS, edgeCRS = curCRS && !oldCRS;
            bool curCDB = CDB, edgeCDB = curCDB && !oldCDB;

            // Topple 翻转类指令: 禁操 (CFB) / 调试 (CDB)
            if (edgeCFB) manualForbid = !manualForbid;
            if (edgeCDB) Debug[0] = !Debug;

            // 确认 (CAK) / 复位 (CRS): 清除 Trip 与 OpFl 标志
            // 禁操生效时拦截，避免画面边界场景（故障+禁操）下误触复位
            if ((edgeCAK || edgeCRS) && !manualForbid)
            {
                Trip[0] = false;
                OpFlOn[0] = false;
                OpFlOff[0] = false;
                OpFlStp[0] = false;
            }

            // 复位 (CRS): 额外清除指令活动状态与所有定时器
            if (edgeCRS && !manualForbid)
            {
                onCmdActive = false;
                offCmdActive = false;
                stpCmdActive = false;
                onTimer = 0; offTimer = 0; stpTimer = 0;
                onToverTimer = 0; offToverTimer = 0; stpToverTimer = 0;
            }

            // ================== 2. 禁操与异常状态检测 ==================
            bool locForbid = EnLoc && curLoc;
            bool fbatForbid = EnFBat & FBat;
            bool fdevForbid = EnFDev & FDev;
            Forbid[0] = manualForbid || locForbid || fbatForbid || fdevForbid;

            // 反馈异常 (FBFl): 开关两端同时反馈为 1
            FBFl[0] = curFBOn && curFBOff;

            // ================== 3. 跳闸检测 (TripM) ==================
            if (TripM == 4)
            {
                // 4=闭锁跳闸信号输出，跳闸检测无效
            }
            else if (TripM == 0)
            {
                bool fbOnRising = !oldFBOn && curFBOn;
                bool fbOffFalling = oldFBOff && !curFBOff;
                if (!onCmdActive && !curPOn && !curLoc && (fbOnRising || fbOffFalling) && !Trip)
                    Trip[0] = true;
            }
            else if (TripM == 1)
            {
                bool fbOffRising = !oldFBOff && curFBOff;
                bool fbOnFalling = oldFBOn && !curFBOn;
                if (!offCmdActive && !curPOff && !curLoc && (fbOffRising || fbOnFalling) && !Trip)
                    Trip[0] = true;
            }
            else if (TripM == 2)
            {
                bool fbStpRising = !oldFBStp && curFBStp;
                if (!stpCmdActive && !curLoc && fbStpRising && !Trip)
                    Trip[0] = true;
            }
            else if (TripM == 3)
            {
                bool fbChanged = (oldFBOn != curFBOn) || (oldFBOff != curFBOff) || (oldFBStp != curFBStp);
                bool noCmd = !onCmdActive && !offCmdActive && !stpCmdActive && !curPOn && !curPOff;
                if (noCmd && !curLoc && fbChanged && !Trip)
                    Trip[0] = true;
            }

            // ================== 4. 指令超时与操作失败检测 (OpFl) ==================
            // On 指令检测
            if (onCmdActive)
            {
                onTimer += cycle;
                onToverTimer += cycle;

                if (curFBOn)
                {
                    // 反馈到位：无条件结束"行程中"状态，驱动画面切到"已开"
                    // ResetM != 2 时同步复位 On 输出；ResetM == 2 时保留输出
                    onCmdActive = false;
                    OpFlOn[0] = false;
                    if (ResetM != 2) On[0] = false;
                }
                else
                {
                    if (!OutM && onTimer >= SetT) On[0] = false;
                    if (onToverTimer >= effectiveTover && !curFBOn)
                    {
                        OpFlOn[0] = true;
                        onCmdActive = false;
                        if (ResetM == 0) On[0] = false;
                    }
                }
            }

            // Off 指令检测
            if (offCmdActive)
            {
                offTimer += cycle;
                offToverTimer += cycle;

                if (curFBOff)
                {
                    offCmdActive = false;
                    OpFlOff[0] = false;
                    if (ResetM != 2) Off[0] = false;
                }
                else
                {
                    if (!OutM && offTimer >= SetT) Off[0] = false;
                    if (offToverTimer >= effectiveTover && !curFBOff)
                    {
                        OpFlOff[0] = true;
                        offCmdActive = false;
                        if (ResetM == 0) Off[0] = false;
                    }
                }
            }

            // Stp 指令检测
            if (stpCmdActive && StopR)
            {
                stpTimer += cycle;
                stpToverTimer += cycle;

                if (curFBStp)
                {
                    stpCmdActive = false;
                    OpFlStp[0] = false;
                    if (ResetM != 2) Stp[0] = false;
                }
                else
                {
                    if (!OutM && stpTimer >= SetT) Stp[0] = false;
                    if (stpToverTimer >= effectiveTover && !curFBStp)
                    {
                        OpFlStp[0] = true;
                        stpCmdActive = false;
                        if (ResetM == 0) Stp[0] = false;
                    }
                }
            }

            OpFl[0] = OpFlOn | OpFlOff | OpFlStp;

            // ================== 5. 输出闭锁计算 (NoCon) ==================
            if (!FLB)
                NoCon[0] = OpFl | Trip | Forbid;
            else
                NoCon[0] = Forbid;

            // ================== 6. 手/自动模式切换 ==================
            if (ToM & !curLoc)
                MA[0] = true;
            else if (ReqA & !ToM)
                MA[0] = false;

            // ================== 6.5 HMI 模式切换与手动命令 ==================
            // 投自动 (CTA) / 切手动 (CTM), 与自动逻辑同等约束
            if (edgeCTA && !ToM) MA[0] = false;
            if (edgeCTM && !curLoc) MA[0] = true;

            // 手动操作指令 CON / COF / CSP, 按 MP 参数行为分支:
            //   MP=0: 不切手动, 但允许执行
            //   MP=1: !Loc 时切换至手动, 并执行
            //   MP=2: 不切手动, 也不执行
            if (MP != 2)
            {
                if (MP == 1 && !curLoc && (edgeCON || edgeCOF || edgeCSP))
                    MA[0] = true;

                if (!NoCon)
                {
                    // 重入保护：行程中再次收到同方向指令会重置超时计时器，导致 OpFl 永不触发
                    if (edgeCON & EnOn && !onCmdActive)
                    {
                        middleStopActive = false;
                        StartOnCmd();
                    }
                    else if (edgeCOF & EnOff && !offCmdActive)
                    {
                        middleStopActive = false;
                        StartOffCmd();
                    }
                    else if (edgeCSP & EnStp && !stpCmdActive)
                    {
                        middleStopActive = onCmdActive || offCmdActive;
                        StartStpCmd();
                    }
                }
            }

            // ================== 7. 指令分发执行逻辑 ==================
            bool blocked = NoCon;

            if (!blocked)
            {
                // 保护指令优先 (无视手自动与允许条件)
                if (curPOn || curPOff)
                {
                    // 保护动作保持最高优先级，可以解除人工中停锁存。
                    middleStopActive = false;
                    if (curPOn && !curPOff) StartOnCmd();
                    else if (curPOff && !curPOn) StartOffCmd();
                    else
                    {
                        if (OutPri == 0) StartOnCmd();
                        else StartOffCmd();
                    }
                }
                // 自动指令 (处于自动模式下，并需满足允许条件)
                // 人工中停锁存期间不重复接受持续电平的自动请求，避免设备自行运行到终点。
                else if (!MA && !middleStopActive)
                {
                    bool wantOn = AOn & EnOn;
                    bool wantOff = AOff & EnOff;
                    bool wantStp = AStp & EnStp & StopR;

                    // 仅当没有指令正在活动时才触发新指令
                    if (!onCmdActive && !offCmdActive && !stpCmdActive)
                    {
                        if (wantOn || wantOff || wantStp)
                        {
                            if (OutPri == 0)
                            {
                                if (wantOn) StartOnCmd();
                                else if (wantOff) StartOffCmd();
                                else if (wantStp) StartStpCmd();
                            }
                            else if (OutPri == 2)
                            {
                                if (wantStp) StartStpCmd();
                                else if (wantOff) StartOffCmd();
                                else if (wantOn) StartOnCmd();
                            }
                            else
                            {
                                if (wantOff) StartOffCmd();
                                else if (wantOn) StartOnCmd();
                                else if (wantStp) StartStpCmd();
                            }
                        }
                    }
                }
            }

            if (!StopR) Stp[0] = true;

            // ================== 8. 综合状态标志输出 ==================
            Totp[0] = curPOn || curPOff | Trip;
            TRBL[0] = OpFl | Trip | FBFl | FDev;

            // 保存状态供下周期做沿检测
            oldFBOn = curFBOn;
            oldFBOff = curFBOff;
            oldFBStp = curFBStp;
            oldCON = curCON;
            oldCOF = curCOF;
            oldCSP = curCSP;
            oldCTA = curCTA;
            oldCTM = curCTM;
            oldCAK = curCAK;
            oldCFB = curCFB;
            oldCRS = curCRS;
            oldCDB = curCDB;

            // ================== 9. 状态打包点 (PACK) 组装 ==================
            // 为 HMI 面板交互提供底层数据支撑 (按位压缩运算)
            uint pack = 0;
            if (curPOn) pack |= (1u << 0);
            if (curPOff) pack |= (1u << 1);
            if (EnOn) pack |= (1u << 2);
            if (EnOff) pack |= (1u << 3);
            if (EnStp) pack |= (1u << 4);
            if (ToM) pack |= (1u << 5);
            if (ReqA) pack |= (1u << 6);
            if (AOn) pack |= (1u << 7);
            if (AOff) pack |= (1u << 8);
            if (AStp) pack |= (1u << 9);
            // 行程中或人工中停时不显示端点状态：前者等待新反馈到位，后者避免旧反馈
            // 在“正在开/关”消失后重新显示成“已开/已关”。
            bool suppressEndpointState = middleStopActive || onCmdActive || offCmdActive;
            if (curFBOn && !suppressEndpointState) pack |= (1u << 10);
            if (curFBOff && !suppressEndpointState) pack |= (1u << 11);
            if (curFBStp) pack |= (1u << 12);
            if (curLoc) pack |= (1u << 13);
            if (FBat) pack |= (1u << 14);
            if (FDev) pack |= (1u << 15);
            if (On) pack |= (1u << 16);
            if (Off) pack |= (1u << 17);
            // 人工中停状态独立于 Stp 的脉冲/长信号输出方式保持，直到新的人工开、关
            // 或保护动作解除；普通开、关行程中则抑制“已停”显示。
            if (middleStopActive || ((bool)Stp && !onCmdActive && !offCmdActive)) pack |= (1u << 18);
            if (MA) pack |= (1u << 19);
            if (Debug) pack |= (1u << 20);
            if (FBFl) pack |= (1u << 21);
            if (Trip) pack |= (1u << 22);
            if (OpFl) pack |= (1u << 23);
            if (Forbid) pack |= (1u << 24);

            // Bit25: 自动请求待处理 (有请求但处于手动)
            bool autoReqPending = (AOn | AOff | AStp) & MA;
            if (autoReqPending) pack |= (1u << 25);

            // Bit26: 手动禁操指示
            if (manualForbid) pack |= (1u << 26);
            if (Totp) pack |= (1u << 27);

            // Bit28~Bit30: 动作行程中状态
            // 与 Bit10/Bit11 (已开/已关) 互斥：反馈到位即抑制行程中位，避免 HMI 显示叠加
            bool strokeOn = onCmdActive && !curFBOn;
            bool strokeOff = offCmdActive && !curFBOff;
            bool inStroke = strokeOn || strokeOff;
            if (inStroke) pack |= (1u << 28);
            if (strokeOn) pack |= (1u << 29);
            if (strokeOff) pack |= (1u << 30);

            // Bit31: 综合故障
            if (TRBL) pack |= (1u << 31);

            TAG.Value = pack;
        }

        
        // ================== 辅助控制函数 ==================
        private void StartOnCmd()
        {
            On[0] = true;
            Off[0] = false;
            if (StopR) Stp[0] = false;

            onCmdActive = true;
            onTimer = 0;
            onToverTimer = 0;

            offCmdActive = false;
            stpCmdActive = false;
            stpTimer = 0;
            stpToverTimer = 0;
        }

        private void StartOffCmd()
        {
            Off[0] = true;
            On[0] = false;
            if (StopR) Stp[0] = false;

            offCmdActive = true;
            offTimer = 0;
            offToverTimer = 0;

            onCmdActive = false;
            stpCmdActive = false;
            stpTimer = 0;
            stpToverTimer = 0;
        }

        private void StartStpCmd()
        {
            Stp[0] = true;
            On[0] = false;
            Off[0] = false;

            // StopR=0 时 Stp 是常 1 接点，中停仍需作为状态指令立即终止开/关行程；
            // 仅 StopR=1 时才按普通输出指令等待 FBStp 并进行超时判断。
            stpCmdActive = StopR;
            stpTimer = 0;
            stpToverTimer = 0;

            onCmdActive = false;
            offCmdActive = false;
            onTimer = 0;
            offTimer = 0;
            onToverTimer = 0;
            offToverTimer = 0;
        }
    }
}
