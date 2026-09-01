using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class EDEVICEM
    {
        protected override void Run(ICommand cmd)
        {
            if (!Enable)
                return;

            double cycle = Math.Max(0.0, cmd.Dpu.Cycle);
            double pulseDuration = Math.Max(0.0, SetT);
            double effectiveTover = Math.Max(Math.Max(0.0, Tover), pulseDuration);

            bool curFBOn = FBOn;
            bool curFBOff = FBOff;
            bool curLoc = Loc;
            bool curFBat = FBat;
            bool curFDev = FDev;
            bool curFSpr = FSpr;
            bool curToM = ToM;
            bool curReqA = ReqA;

            bool curCON = CON;
            bool curCOF = COF;
            bool curCTA = CTA;
            bool curCTM = CTM;
            bool curCAK = CAK;
            bool curCFB = CFB;
            bool curCRS = CRS;
            bool curCDB = CDB;

            bool edgeCON = false;
            bool edgeCOF = false;
            bool edgeCTA = false;
            bool edgeCTM = false;
            bool edgeCAK = false;
            bool edgeCFB = false;
            bool edgeCRS = false;
            bool edgeCDB = false;

            if (firstRun)
            {
                MA[0] = true;
                ResetCommands();
                oldFBOn = curFBOn;
                oldFBOff = curFBOff;
                oldCON = curCON;
                oldCOF = curCOF;
                oldCTA = curCTA;
                oldCTM = curCTM;
                oldCAK = curCAK;
                oldCFB = curCFB;
                oldCRS = curCRS;
                oldCDB = curCDB;
                firstRun = false;
            }
            else
            {
                edgeCON = curCON && !oldCON;
                edgeCOF = curCOF && !oldCOF;
                edgeCTA = curCTA && !oldCTA;
                edgeCTM = curCTM && !oldCTM;
                edgeCAK = curCAK && !oldCAK;
                edgeCFB = curCFB && !oldCFB;
                edgeCRS = curCRS && !oldCRS;
                edgeCDB = curCDB && !oldCDB;
            }

            // Toggle 类 HMI 指令只消费上升沿，500 ms 高电平跨多个周期也只翻转一次。
            if (edgeCFB)
                manualForbid = !manualForbid;
            if (edgeCDB)
                debugMode = !debugMode;

            // 确认只清报警；复位还取消全部活动命令和控制输出。两路同周期到达时保持幂等。
            if (edgeCAK || edgeCRS)
            {
                Trip[0] = false;
                OpFlOn[0] = false;
                OpFlOff[0] = false;
            }
            if (edgeCRS)
                ResetCommands();

            bool locForbid = EnLoc && curLoc;
            bool fbatForbid = EnFBat && curFBat;
            bool fdevForbid = EnFDev && curFDev;
            bool forbid = manualForbid || locForbid || fbatForbid || fdevForbid;
            Forbid[0] = forbid;

            FBFl[0] = curFBOn && curFBOff;

            // 无授权分闸行程时合位反馈丢失，判为断路器异常跳闸。
            if (!edgeCAK && !edgeCRS
                && !offCmdActive
                && !locForbid
                && oldFBOn
                && !curFBOn)
            {
                Trip[0] = true;
            }

            // 禁操必须立即撤销物理输出；行程监视保留，以接收已发动作的反馈并避免误判 Trip。
            if (forbid)
                StopAllPulses();
            else
                UpdateOutputPulses(cycle, pulseDuration);

            UpdateOnStroke(curFBOn, cycle, effectiveTover);
            UpdateOffStroke(curFBOff, cycle, effectiveTover);

            bool opFl = (bool)OpFlOn || (bool)OpFlOff;
            bool noCon = FLB ? forbid : (opFl || (bool)Trip || forbid);

            // ToM 优先于 ReqA；两者都没有时才消费 HMI 模式切换沿。
            if (!locForbid)
            {
                if (curToM)
                    MA[0] = true;
                else if (curReqA)
                    MA[0] = false;
                else if (edgeCTM)
                    MA[0] = true;
                else if (edgeCTA)
                    MA[0] = false;

                if (MP == 1 && (edgeCON || edgeCOF))
                    MA[0] = true;
            }

            bool springBlocksOn = EnFSpr && curFSpr;
            bool requestOn = false;
            bool requestOff = false;

            if (!noCon && !onCmdActive && !offCmdActive)
            {
                if (MP != 2)
                {
                    requestOn = edgeCON && (bool)EnOn && !springBlocksOn && !curFBOn;
                    requestOff = edgeCOF && (bool)EnOff && !curFBOff;
                }

                if (!(bool)MA)
                {
                    requestOn |= (bool)AOn && (bool)EnOn && !springBlocksOn && !curFBOn;
                    requestOff |= (bool)AOff && (bool)EnOff && !curFBOff;
                }

                // 电气开关合、分同时请求时采用安全侧分闸优先。
                if (requestOff)
                    StartOffCommand();
                else if (requestOn)
                    StartOnCommand();
            }

            // 新命令、反馈或超时可能在本周期改变派生状态，必须在最后重新计算。
            opFl = (bool)OpFlOn || (bool)OpFlOff;
            noCon = FLB ? forbid : (opFl || (bool)Trip || forbid);
            OpFl[0] = opFl;
            NoCon[0] = noCon;

            WritePackedStatus(
                curToM,
                curReqA,
                curFBOn,
                curFBOff,
                curLoc,
                curFBat,
                curFDev,
                curFSpr,
                forbid,
                opFl);
            ApplyOutputQuality();

            oldFBOn = curFBOn;
            oldFBOff = curFBOff;
            oldCON = curCON;
            oldCOF = curCOF;
            oldCTA = curCTA;
            oldCTM = curCTM;
            oldCAK = curCAK;
            oldCFB = curCFB;
            oldCRS = curCRS;
            oldCDB = curCDB;
        }

        private void StartOnCommand()
        {
            StopOffPulse();
            Off[0] = false;
            offCmdActive = false;
            offToverTimer = 0.0;

            On[0] = true;
            onPulseActive = true;
            onTimer = 0.0;
            onCmdActive = true;
            onToverTimer = 0.0;
            OpFlOn[0] = false;
            Trip[0] = false;
        }

        private void StartOffCommand()
        {
            StopOnPulse();
            On[0] = false;
            onCmdActive = false;
            onToverTimer = 0.0;

            Off[0] = true;
            offPulseActive = true;
            offTimer = 0.0;
            offCmdActive = true;
            offToverTimer = 0.0;
            OpFlOff[0] = false;
            Trip[0] = false;
        }

        private void UpdateOutputPulses(double cycle, double pulseDuration)
        {
            if (onPulseActive)
            {
                onTimer += cycle;
                if (onTimer >= pulseDuration)
                    StopOnPulse();
            }

            if (offPulseActive)
            {
                offTimer += cycle;
                if (offTimer >= pulseDuration)
                    StopOffPulse();
            }
        }

        private void UpdateOnStroke(bool feedbackOn, double cycle, double effectiveTover)
        {
            if (!onCmdActive)
                return;

            onToverTimer += cycle;
            if (feedbackOn)
            {
                onCmdActive = false;
                onToverTimer = 0.0;
                OpFlOn[0] = false;
                if (ResetM != 2)
                    StopOnPulse();
                return;
            }

            if (onToverTimer >= effectiveTover)
            {
                onCmdActive = false;
                OpFlOn[0] = true;
                if (ResetM == 0)
                    StopOnPulse();
            }
        }

        private void UpdateOffStroke(bool feedbackOff, double cycle, double effectiveTover)
        {
            if (!offCmdActive)
                return;

            offToverTimer += cycle;
            if (feedbackOff)
            {
                offCmdActive = false;
                offToverTimer = 0.0;
                OpFlOff[0] = false;
                if (ResetM != 2)
                    StopOffPulse();
                return;
            }

            if (offToverTimer >= effectiveTover)
            {
                offCmdActive = false;
                OpFlOff[0] = true;
                if (ResetM == 0)
                    StopOffPulse();
            }
        }

        private void StopOnPulse()
        {
            On[0] = false;
            onPulseActive = false;
            onTimer = 0.0;
        }

        private void StopOffPulse()
        {
            Off[0] = false;
            offPulseActive = false;
            offTimer = 0.0;
        }

        private void StopAllPulses()
        {
            StopOnPulse();
            StopOffPulse();
        }

        private void ResetCommands()
        {
            StopAllPulses();
            onCmdActive = false;
            offCmdActive = false;
            onToverTimer = 0.0;
            offToverTimer = 0.0;
        }

        private void WritePackedStatus(
            bool curToM,
            bool curReqA,
            bool curFBOn,
            bool curFBOff,
            bool curLoc,
            bool curFBat,
            bool curFDev,
            bool curFSpr,
            bool forbid,
            bool opFl)
        {
            uint pack = 0;

            if (curToM) pack |= 1u << 0;
            if (curReqA) pack |= 1u << 1;
            if (EnOn) pack |= 1u << 2;
            if (EnOff) pack |= 1u << 3;
            if (POpe) pack |= 1u << 5;
            if (curFSpr) pack |= 1u << 6;
            if (AOn) pack |= 1u << 7;
            if (AOff) pack |= 1u << 8;

            bool inStroke = onCmdActive || offCmdActive;
            if (curFBOn && !inStroke) pack |= 1u << 10;
            if (curFBOff && !inStroke) pack |= 1u << 11;
            if (curLoc) pack |= 1u << 13;
            if (curFBat) pack |= 1u << 14;
            if (curFDev) pack |= 1u << 15;
            if (On) pack |= 1u << 16;
            if (Off) pack |= 1u << 17;
            if (MA) pack |= 1u << 19;
            if (debugMode) pack |= 1u << 20;
            if (FBFl) pack |= 1u << 21;
            if (Trip) pack |= 1u << 22;
            if (opFl) pack |= 1u << 23;
            if (forbid) pack |= 1u << 24;
            if (((bool)AOn || (bool)AOff) && (bool)MA) pack |= 1u << 25;
            if (manualForbid) pack |= 1u << 26;
            if (inStroke) pack |= 1u << 28;
            if (onCmdActive) pack |= 1u << 29;
            if (offCmdActive) pack |= 1u << 30;
            if (curFDev || opFl) pack |= 1u << 31;

            TAG.Value = pack;
        }

        private void ApplyOutputQuality()
        {
            QualityTypes quality = QualityTypes.Good;
            if (QualityT is 1 or 2)
            {
                Span<QualityTypes> inputQualities = stackalloc QualityTypes[]
                {
                    Enable.Quality,
                    EnOn.Quality,
                    EnOff.Quality,
                    ToM.Quality,
                    ReqA.Quality,
                    AOn.Quality,
                    AOff.Quality,
                    FBOn.Quality,
                    FBOff.Quality,
                    Loc.Quality,
                    FBat.Quality,
                    FDev.Quality,
                    POpe.Quality,
                    FSpr.Quality,
                };

                int badCount = 0;
                foreach (QualityTypes item in inputQualities)
                {
                    if (item != QualityTypes.Good)
                        badCount++;
                }

                bool bad = QualityT == 1
                    ? badCount > 0
                    : badCount == inputQualities.Length;
                if (bad)
                    quality = QualityTypes.Bad;
            }

            On.Quality = quality;
            Off.Quality = quality;
            MA.Quality = quality;
            NoCon.Quality = quality;
            FBFl.Quality = quality;
            Trip.Quality = quality;
            OpFl.Quality = quality;
            Forbid.Quality = quality;
            OpFlOn.Quality = quality;
            OpFlOff.Quality = quality;
            TAG.Quality = quality;
        }
    }
}
