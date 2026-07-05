using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class STEP
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // 提取扫描周期及参数边界
            float cycle = cmd.Dpu.Cycle;
            int maxSteps = (int)MaxS;
            if (maxSteps < 1) maxSteps = 1;
            if (maxSteps > 8) maxSteps = 8;

            int curStep = (int)Step;
            uint bitDis = (uint)BitDis;
            bool en = EN;

            // 硬引脚一次性读入本地变量，edge 计算与 PACK 段共用
            bool curSTART = START;
            bool curPAUSE = PAUSE;
            bool curSKIP = SKIP;
            bool curRST = RST;
            bool curTRACK = TRACK;

            // 状态字也一次性读入，主循环判断/PACK 段共用；写 LD 时同步本地副本，
            // 保留原始写入时序以避免破坏 LD setter 的潜在副作用 (IOMap / HMI 通知)
            bool stRUN = RUN;
            bool stFAIL = FAIL;
            bool stEND = END;

            // ================== 1. HMI 命令上升沿采集 (照搬 DEVICE) ==================
            bool curCST = CST, edgeCST = curCST && !oldCST;
            bool curCPS = CPS, edgeCPS = curCPS && !oldCPS;
            bool curCSK = CSK, edgeCSK = curCSK && !oldCSK;
            bool curCRS = CRS, edgeCRS = curCRS && !oldCRS;
            bool curCTM = CTM, edgeCTM = curCTM && !oldCTM;
            bool curCTA = CTA, edgeCTA = curCTA && !oldCTA;

            // 硬引脚沿 OR HMI 沿
            bool edgeStart = (curSTART & !oldSTART) || edgeCST;
            bool edgePause = (curPAUSE & !oldPAUSE) || edgeCPS;
            bool edgeSkip = (curSKIP & !oldSKIP) || edgeCSK;
            bool edgeRst = (curRST & !oldRST) || edgeCRS;
            bool edgeTrack = (curTRACK & !oldTRACK);

            // ================== 2. 手/自动状态切换 ==================
            bool isManual = MA;
            if (edgeCTM) isManual = true;
            if (edgeCTA) isManual = false;
            MA[0] = isManual;

            // ================== 3. 主状态机：复位/置步/启动/暂停/跳步 ==================

            // [指令 A] 复位 (最高优先级，无视 EN)
            if (edgeRst)
            {
                curStep = 0;
                stepTimer = 0f;
                paused = false;
                FAIL[0] = false; stFAIL = false;
                END[0] = false;  stEND = false;
                RUN[0] = false;  stRUN = false;
                ClearAllOutputs();
            }

            // [指令 B] 置步 (需判断 EN)
            if (edgeTrack && en && curStep == 0)
            {
                int tno = (int)TNO;
                if (tno >= 1 && tno <= maxSteps)
                {
                    curStep = tno;
                    stepTimer = 0f;
                    paused = false;
                    FAIL[0] = false; stFAIL = false;
                    END[0] = false;  stEND = false;
                    RUN[0] = true;   stRUN = true;
                    ClearAllOutputs();
                    SetOutput(curStep, true);
                }
            }

            // [指令 C] 启动 (需判断 EN)
            if (edgeStart && en)
            {
                if (curStep == 0 && !stEND)
                {
                    // 首次启动跳过被 BitDis 屏蔽的步
                    curStep = FindNextStep(0, bitDis, maxSteps);
                    if (curStep > 0 && curStep <= maxSteps)
                    {
                        stepTimer = 0f;
                        paused = false;
                        FAIL[0] = false; stFAIL = false;
                        END[0] = false;  stEND = false;
                        RUN[0] = true;   stRUN = true;
                        ClearAllOutputs();
                        SetOutput(curStep, true);
                    }
                }
                else if (stFAIL)
                {
                    // 故障恢复后再次启动，当前步重新计时执行
                    stepTimer = 0f;
                    paused = false;
                    FAIL[0] = false; stFAIL = false;
                    RUN[0] = true;   stRUN = true;
                }
                else if (paused)
                {
                    // 暂停态下启动 → 解除暂停继续执行
                    paused = false;
                }
            }

            // [指令 D] 暂停 (toggle)
            if (edgePause & curStep > 0 & stRUN && !stFAIL)
            {
                paused = !paused;
            }

            // [指令 E] 跳步 (需判断 EN)
            if (edgeSkip & en & curStep > 0 & stRUN && !paused && !stFAIL)
            {
                AdvanceStep(ref curStep, bitDis, maxSteps);
                stRUN = RUN; stEND = END; // AdvanceStep 内可能改写 RUN[0]/END[0]
            }

            // ================== 4. 步序时序与超时检测 ==================
            if (curStep > 0 & stRUN & !paused && !stFAIL)
            {
                stepTimer += cycle;

                float tim = GetTIM(curStep);
                float tlmt = GetTLmt(curStep);
                bool fb = GetFB(curStep);
                bool advance = false;

                // 条件 1: 反馈到位
                if (fb) advance = true;

                // 条件 2: TIM 到达 (TIM > TLmt 时该功能失效)
                if (!advance && (tim <= tlmt) && (stepTimer >= tim))
                {
                    advance = true;
                }

                // 条件 3: 超过 TLmt → 故障
                if (!advance && (stepTimer > tlmt))
                {
                    FAIL[0] = true; stFAIL = true;
                    paused = true;
                }

                if (advance)
                {
                    AdvanceStep(ref curStep, bitDis, maxSteps);
                    stRUN = RUN; stEND = END;
                }
            }

            // ================== 5. 输出当前步号与计时 ==================
            Step[0] = curStep;
            TRun[0] = stepTimer;

            if (curStep > 0)
            {
                float tlmt = GetTLmt(curStep);
                float remaining = tlmt - stepTimer;
                TRst[0] = remaining > 0 ? remaining : 0f;
            }
            else
            {
                TRst[0] = 0f;
            }

            // ================== 6. 保存沿历史，供下周期使用 ==================
            oldSTART = curSTART;
            oldPAUSE = curPAUSE;
            oldSKIP = curSKIP;
            oldTRACK = curTRACK;
            oldRST = curRST;
            oldCST = curCST;
            oldCPS = curCPS;
            oldCSK = curCSK;
            oldCRS = curCRS;
            oldCTM = curCTM;
            oldCTA = curCTA;

            // ================== 7. 状态打包点 (PACK) → TAG.Value ==================
            uint pack = 0;
            // Bit0~7: FB1~FB8 反馈输入指示
            if (FB1) pack |= (1u << 0);
            if (FB2) pack |= (1u << 1);
            if (FB3) pack |= (1u << 2);
            if (FB4) pack |= (1u << 3);
            if (FB5) pack |= (1u << 4);
            if (FB6) pack |= (1u << 5);
            if (FB7) pack |= (1u << 6);
            if (FB8) pack |= (1u << 7);

            // Bit8~15: OUT1~OUT8 输出指令指示
            if (OUT1) pack |= (1u << 8);
            if (OUT2) pack |= (1u << 9);
            if (OUT3) pack |= (1u << 10);
            if (OUT4) pack |= (1u << 11);
            if (OUT5) pack |= (1u << 12);
            if (OUT6) pack |= (1u << 13);
            if (OUT7) pack |= (1u << 14);
            if (OUT8) pack |= (1u << 15);

            // Bit16~25: 综合控制状态 (硬引脚 OR HMI 命令)
            if (stRUN) pack |= (1u << 16);
            if (stFAIL) pack |= (1u << 17);
            if (stEND) pack |= (1u << 18);
            if (curSTART | curCST) pack |= (1u << 19);
            if (curPAUSE | curCPS) pack |= (1u << 20);
            if (curSKIP | curCSK) pack |= (1u << 21);
            if (curTRACK) pack |= (1u << 22);
            if (curRST | curCRS) pack |= (1u << 23);
            if (en) pack |= (1u << 24);
            if (paused) pack |= (1u << 25);

            TAG.Value = pack;
        }

        // ================== 辅助函数群 ==================

        private void AdvanceStep(ref int curStep, uint bitDis, int maxSteps)
        {
            SetOutput(curStep, false);
            int next = FindNextStep(curStep, bitDis, maxSteps);

            if (next > maxSteps)
            {
                curStep = 0;
                stepTimer = 0f;
                RUN[0] = false;
                END[0] = true;
            }
            else
            {
                curStep = next;
                stepTimer = 0f;
                SetOutput(curStep, true);
            }
        }

        private int FindNextStep(int from, uint bitDis, int maxSteps)
        {
            for (int i = from + 1; i <= maxSteps; i++)
            {
                // bitDis 第 i-1 位为 0 则允许执行，为 1 则被屏蔽
                if (((bitDis >> (i - 1)) & 1u) == 0u)
                    return i;
            }
            return maxSteps + 1;
        }

        private bool GetFB(int step)
        {
            switch (step)
            {
                case 1: return FB1;
                case 2: return FB2;
                case 3: return FB3;
                case 4: return FB4;
                case 5: return FB5;
                case 6: return FB6;
                case 7: return FB7;
                case 8: return FB8;
                default: return false;
            }
        }

        private float GetTIM(int step)
        {
            switch (step)
            {
                case 1: return TIM1;
                case 2: return TIM2;
                case 3: return TIM3;
                case 4: return TIM4;
                case 5: return TIM5;
                case 6: return TIM6;
                case 7: return TIM7;
                case 8: return TIM8;
                default: return 999999f;
            }
        }

        private float GetTLmt(int step)
        {
            switch (step)
            {
                case 1: return TLmt1;
                case 2: return TLmt2;
                case 3: return TLmt3;
                case 4: return TLmt4;
                case 5: return TLmt5;
                case 6: return TLmt6;
                case 7: return TLmt7;
                case 8: return TLmt8;
                default: return 60f;
            }
        }

        private void SetOutput(int step, bool value)
        {
            switch (step)
            {
                case 1: OUT1[0] = value; break;
                case 2: OUT2[0] = value; break;
                case 3: OUT3[0] = value; break;
                case 4: OUT4[0] = value; break;
                case 5: OUT5[0] = value; break;
                case 6: OUT6[0] = value; break;
                case 7: OUT7[0] = value; break;
                case 8: OUT8[0] = value; break;
            }
        }

        private void ClearAllOutputs()
        {
            OUT1[0] = false; OUT2[0] = false; OUT3[0] = false; OUT4[0] = false;
            OUT5[0] = false; OUT6[0] = false; OUT7[0] = false; OUT8[0] = false;
        }
    }
}
