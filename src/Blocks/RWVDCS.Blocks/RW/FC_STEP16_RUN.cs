using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class STEP16
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // 提取扫描周期及参数边界
            float cycle = cmd.Dpu.Cycle;
            int maxSteps = (int)MaxS;
            if (maxSteps < 1) maxSteps = 1;
            if (maxSteps > 16) maxSteps = 16;

            int curStep = (int)STEP;
            uint bitDis = (uint)BitDis;
            bool en = EN;

            // 硬引脚一次性读入本地变量，edge 计算与 PACK 段共用
            bool curSTART = START;
            bool curPAUSE = PAUSE;
            bool curSKIP = SKIP;
            bool curRST = RST;
            bool curTRACK = TRACK;

            // 状态字也一次性读入；写 LD 时同步本地副本，保留原始写入时序
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

            if (edgeStart && en)
            {
                if (curStep == 0 && !stEND)
                {
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
                    stepTimer = 0f;
                    paused = false;
                    FAIL[0] = false; stFAIL = false;
                    RUN[0] = true;   stRUN = true;
                }
                else if (paused)
                {
                    paused = false;
                }
            }

            if (edgePause & curStep > 0 & stRUN && !stFAIL)
            {
                paused = !paused;
            }

            if (edgeSkip & en && curStep > 0 & stRUN && !paused && !stFAIL)
            {
                AdvanceStep(ref curStep, bitDis, maxSteps);
                stRUN = RUN; stEND = END;
            }

            // ================== 4. 步序时序与超时检测 ==================
            if (curStep > 0 & stRUN && !paused && !stFAIL)
            {
                stepTimer += cycle;

                float tim = GetTIM(curStep);
                float tlmt = GetTLmt(curStep);
                bool fb = GetFB(curStep);
                bool advance = false;

                if (fb) advance = true;

                if (!advance && (tim <= tlmt) && (stepTimer >= tim))
                {
                    advance = true;
                }

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
            STEP[0] = (float)curStep;
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

            // ================== 6. 保存沿历史 ==================
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

            // ================== 7. 双 PACK 装配 → TAG.Value / PK2.Value ==================

            // --- PACK1 (TAG) ---
            uint pack1 = 0;
            if (FB1) pack1 |= (1u << 0);
            if (FB2) pack1 |= (1u << 1);
            if (FB3) pack1 |= (1u << 2);
            if (FB4) pack1 |= (1u << 3);
            if (FB5) pack1 |= (1u << 4);
            if (FB6) pack1 |= (1u << 5);
            if (FB7) pack1 |= (1u << 6);
            if (FB8) pack1 |= (1u << 7);

            if (OUT1) pack1 |= (1u << 8);
            if (OUT2) pack1 |= (1u << 9);
            if (OUT3) pack1 |= (1u << 10);
            if (OUT4) pack1 |= (1u << 11);
            if (OUT5) pack1 |= (1u << 12);
            if (OUT6) pack1 |= (1u << 13);
            if (OUT7) pack1 |= (1u << 14);
            if (OUT8) pack1 |= (1u << 15);

            if (stRUN) pack1 |= (1u << 16);
            if (stFAIL) pack1 |= (1u << 17);
            if (stEND) pack1 |= (1u << 18);
            if (curSTART | curCST) pack1 |= (1u << 19);
            if (curPAUSE | curCPS) pack1 |= (1u << 20);
            if (curSKIP | curCSK) pack1 |= (1u << 21);
            if (curTRACK) pack1 |= (1u << 22);
            if (curRST | curCRS) pack1 |= (1u << 23);
            if (en) pack1 |= (1u << 24);
            if (paused) pack1 |= (1u << 25);

            TAG.Value = pack1;

            // --- PACK2 (PK2) ---
            uint pack2 = 0;
            if (FB9) pack2 |= (1u << 0);
            if (FB10) pack2 |= (1u << 1);
            if (FB11) pack2 |= (1u << 2);
            if (FB12) pack2 |= (1u << 3);
            if (FB13) pack2 |= (1u << 4);
            if (FB14) pack2 |= (1u << 5);
            if (FB15) pack2 |= (1u << 6);
            if (FB16) pack2 |= (1u << 7);

            if (OUT9) pack2 |= (1u << 8);
            if (OUT10) pack2 |= (1u << 9);
            if (OUT11) pack2 |= (1u << 10);
            if (OUT12) pack2 |= (1u << 11);
            if (OUT13) pack2 |= (1u << 12);
            if (OUT14) pack2 |= (1u << 13);
            if (OUT15) pack2 |= (1u << 14);
            if (OUT16) pack2 |= (1u << 15);

            PK2.Value = pack2;
        }

        // ================== 辅助函数群 (支持 16 步) ==================

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
                case 9: return FB9;
                case 10: return FB10;
                case 11: return FB11;
                case 12: return FB12;
                case 13: return FB13;
                case 14: return FB14;
                case 15: return FB15;
                case 16: return FB16;
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
                case 9: return TIM9;
                case 10: return TIM10;
                case 11: return TIM11;
                case 12: return TIM12;
                case 13: return TIM13;
                case 14: return TIM14;
                case 15: return TIM15;
                case 16: return TIM16;
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
                case 9: return TLmt9;
                case 10: return TLmt10;
                case 11: return TLmt11;
                case 12: return TLmt12;
                case 13: return TLmt13;
                case 14: return TLmt14;
                case 15: return TLmt15;
                case 16: return TLmt16;
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
                case 9: OUT9[0] = value; break;
                case 10: OUT10[0] = value; break;
                case 11: OUT11[0] = value; break;
                case 12: OUT12[0] = value; break;
                case 13: OUT13[0] = value; break;
                case 14: OUT14[0] = value; break;
                case 15: OUT15[0] = value; break;
                case 16: OUT16[0] = value; break;
            }
        }

        private void ClearAllOutputs()
        {
            OUT1[0] = false; OUT2[0] = false; OUT3[0] = false; OUT4[0] = false;
            OUT5[0] = false; OUT6[0] = false; OUT7[0] = false; OUT8[0] = false;
            OUT9[0] = false; OUT10[0] = false; OUT11[0] = false; OUT12[0] = false;
            OUT13[0] = false; OUT14[0] = false; OUT15[0] = false; OUT16[0] = false;
        }
    }
}
