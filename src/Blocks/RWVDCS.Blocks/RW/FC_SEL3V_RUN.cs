using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class SEL3V
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            float cycle = (float)cmd.Dpu.Cycle;

            // ================== 1. 首次运行：curMode 取参数 MODE 初始值 ==================
            if (!notFirstRun)
            {
                curMode = MODE;
            }

            // ================== 2. HMI 命令上升沿采集 (参照 DEVICE 模式) ==================
            bool curCD1 = CD1, edgeCD1 = curCD1 && !oldCD1;
            bool curCD2 = CD2, edgeCD2 = curCD2 && !oldCD2;
            bool curCD3 = CD3, edgeCD3 = curCD3 && !oldCD3;
            bool curCE1 = CE1, edgeCE1 = curCE1 && !oldCE1;
            bool curCE2 = CE2, edgeCE2 = curCE2 && !oldCE2;
            bool curCE3 = CE3, edgeCE3 = curCE3 && !oldCE3;
            bool curCAG = CAG, edgeCAG = curCAG && !oldCAG;
            bool curCMN = CMN, edgeCMN = curCMN && !oldCMN;
            bool curCMX = CMX, edgeCMX = curCMX && !oldCMX;
            bool curCMD = CMD, edgeCMD = curCMD && !oldCMD;

            // ================== 3. X1/X2/X3 启用/禁用切换 ==================
            // 启用沿：直接生效
            if (edgeCE1) x1Disabled = false;
            if (edgeCE2) x2Disabled = false;
            if (edgeCE3) x3Disabled = false;

            // 禁用沿：必须保证另两个通道至少有 1 个保持启用，禁止全部关闭
            if (edgeCD1 && !(x2Disabled && x3Disabled)) x1Disabled = true;
            if (edgeCD2 && !(x1Disabled && x3Disabled)) x2Disabled = true;
            if (edgeCD3 && !(x1Disabled && x2Disabled)) x3Disabled = true;

            // ================== 4. MODE 切换 (CAG/CMN/CMX/CMD) ==================
            if (edgeCAG) curMode = 0; // AVG
            if (edgeCMN) curMode = 1; // MIN
            if (edgeCMX) curMode = 2; // MAX
            if (edgeCMD) curMode = 3; // MIDDLE

            // ================== 5. 输入品质评估 ==================
            bool x1Good = (X1.Quality == QualityTypes.Good);
            bool x2Good = (X2.Quality == QualityTypes.Good);
            bool x3Good = (X3.Quality == QualityTypes.Good);

            bool x1Active = !x1Disabled;
            bool x2Active = !x2Disabled;
            bool x3Active = !x3Disabled;

            // XiBad 对应 Xi 在不被禁用时的品质；禁用时强制为 0 (规范要求)
            X1Bad[0] = x1Active ? !x1Good : false;
            X2Bad[0] = x2Active ? !x2Good : false;
            X3Bad[0] = x3Active ? !x3Good : false;

            // ================== 6. 两两偏差越限计算 (仅对启用且品质好的对) ==================
            bool d12 = false, d13 = false, d23 = false;
            if (DB > 0.0f)
            {
                if (x1Active && x2Active && x1Good && x2Good)
                    d12 = Math.Abs(X1 - X2) > DB;
                if (x1Active && x3Active && x1Good && x3Good)
                    d13 = Math.Abs(X1 - X3) > DB;
                if (x2Active && x3Active && x2Good && x3Good)
                    d23 = Math.Abs(X2 - X3) > DB;
            }

            DEV12[0] = d12;
            DEV13[0] = d13;
            DEV23[0] = d23;

            // ================== 7. 优选计算 ==================
            bool outBad = false;
            float newTarget = OUT;
            int activeCount = (x1Active ? 1 : 0) + (x2Active ? 1 : 0) + (x3Active ? 1 : 0);

            if (activeCount == 3)
            {
                int goodCount = (x1Good ? 1 : 0) + (x2Good ? 1 : 0) + (x3Good ? 1 : 0);

                if (goodCount == 0)
                {
                    // 三点全坏：输出保持，坏点标记
                    outBad = true;
                }
                else if (goodCount == 1)
                {
                    // 只有一点好：取该点
                    outBad = false;
                    if (x1Good) newTarget = X1;
                    else if (x2Good) newTarget = X2;
                    else newTarget = X3;
                }
                else if (goodCount == 2)
                {
                    // 两点好：判断这两点的偏差
                    if (!x1Good)
                    {
                        if (d23) { outBad = true; }
                        else { outBad = false; newTarget = ComputeMode2(X2, X3); }
                    }
                    else if (!x2Good)
                    {
                        if (d13) { outBad = true; }
                        else { outBad = false; newTarget = ComputeMode2(X1, X3); }
                    }
                    else
                    {
                        if (d12) { outBad = true; }
                        else { outBad = false; newTarget = ComputeMode2(X1, X2); }
                    }
                }
                else
                {
                    // 三点全好：按偏差越限数量分支
                    int devCount = (d12 ? 1 : 0) + (d13 ? 1 : 0) + (d23 ? 1 : 0);

                    if (devCount == 0)
                    {
                        // 全部不越限：按 MODE 三点优选
                        outBad = false;
                        newTarget = ComputeMode3(X1, X2, X3);
                    }
                    else if (devCount == 3)
                    {
                        // 全部互相越限：输出保持，坏点
                        outBad = true;
                    }
                    else if (devCount == 1)
                    {
                        // 仅一对越限：取另一不参与越限的点
                        outBad = false;
                        if (d12) newTarget = X3;
                        else if (d13) newTarget = X2;
                        else newTarget = X1;
                    }
                    else
                    {
                        // 两对越限：取共同点对应的另两点中不越限的那对，按 MODE 两点优选
                        outBad = false;
                        if (!d12) newTarget = ComputeMode2(X1, X2);
                        else if (!d13) newTarget = ComputeMode2(X1, X3);
                        else newTarget = ComputeMode2(X2, X3);
                    }
                }
            }
            else if (activeCount == 2)
            {
                // 两点启用：等效 SEL2V 行为
                float va, vb;
                bool aGood, bGood, devAB;

                if (!x1Active)
                {
                    va = X2; vb = X3; aGood = x2Good; bGood = x3Good; devAB = d23;
                }
                else if (!x2Active)
                {
                    va = X1; vb = X3; aGood = x1Good; bGood = x3Good; devAB = d13;
                }
                else
                {
                    va = X1; vb = X2; aGood = x1Good; bGood = x2Good; devAB = d12;
                }

                if (!aGood && !bGood)
                {
                    outBad = true;
                }
                else if (!aGood)
                {
                    outBad = false;
                    newTarget = vb;
                }
                else if (!bGood)
                {
                    outBad = false;
                    newTarget = va;
                }
                else
                {
                    if (devAB)
                    {
                        outBad = true;
                    }
                    else
                    {
                        outBad = false;
                        newTarget = ComputeMode2(va, vb);
                    }
                }
            }
            else if (activeCount == 1)
            {
                // 仅一点启用：直接跟随
                float va;
                bool aGood;

                if (x1Active) { va = X1; aGood = x1Good; }
                else if (x2Active) { va = X2; aGood = x2Good; }
                else { va = X3; aGood = x3Good; }

                if (aGood)
                {
                    outBad = false;
                    newTarget = va;
                }
                else
                {
                    outBad = true;
                }
            }
            else
            {
                // 三个全部禁用 (理论上 HMI 已阻止，后端再做兜底防御)
                outBad = true;
            }

            // ================== 8. DevH 综合偏差大报警 ==================
            bool curDevH = d12 || d13 || d23;
            DevH[0] = curDevH;

            // ================== 9. 状态签名 (用于触发速率控制) ==================
            int curStateSignature = (x1Good ? 1 : 0) | (x2Good ? 2 : 0) | (x3Good ? 4 : 0) |
                                    (x1Disabled ? 8 : 0) | (x2Disabled ? 16 : 0) | (x3Disabled ? 32 : 0) |
                                    (d12 ? 64 : 0) | (d13 ? 128 : 0) | (d23 ? 256 : 0) |
                                    ((int)curMode << 16);

            // ================== 10. 输出 + 速率限制 ==================
            if (!notFirstRun)
            {
                if (!outBad)
                {
                    OUT[0] = newTarget;
                    targetValue = newTarget;
                }
                notFirstRun = true;
            }
            else if (outBad)
            {
                // 坏点：保持上一时刻的 OUT，仅更新目标值
                targetValue = OUT;
            }
            else
            {
                targetValue = newTarget;
                bool stateChanged = (curStateSignature != prevStateSignature);
                if (stateChanged)
                    rateActive = true;

                if (RATE > 0.0f && rateActive)
                {
                    float maxChange = RATE * (cycle / 60.0f);
                    float diff = targetValue - OUT;
                    if (Math.Abs(diff) <= maxChange)
                    {
                        OUT[0] = targetValue;
                        rateActive = false;
                    }
                    else
                    {
                        OUT[0] = OUT + Math.Sign(diff) * maxChange;
                    }
                }
                else
                {
                    OUT[0] = targetValue;
                    rateActive = false;
                }
            }

            prevStateSignature = curStateSignature;

            // ================== 11. 品质与综合报警 ==================
            OUT.Quality = outBad ? QualityTypes.Bad : QualityTypes.Good;
            QA[0] = outBad;
            Alm[0] = X1Bad | X2Bad | X3Bad | DevH;

            // ================== 12. 保存沿检测历史 ==================
            oldCD1 = curCD1;
            oldCD2 = curCD2;
            oldCD3 = curCD3;
            oldCE1 = curCE1;
            oldCE2 = curCE2;
            oldCE3 = curCE3;
            oldCAG = curCAG;
            oldCMN = curCMN;
            oldCMX = curCMX;
            oldCMD = curCMD;

            // ================== 13. 状态打包点 (PACK) → TAG.Value ==================
            // 严格遵循 SEL3V.md PACK 位定义表，与 XTP 面板颜色绑定一一对应
            uint pack = 0;
            if (x1Disabled) pack |= (1u << 0);   // Bit0: X1 禁用
            if (x2Disabled) pack |= (1u << 1);   // Bit1: X2 禁用
            if (x3Disabled) pack |= (1u << 2);   // Bit2: X3 禁用
            if (x1Active && !x1Good) pack |= (1u << 3); // Bit3: X1 品质坏 (禁用时不报)
            if (x2Active && !x2Good) pack |= (1u << 4); // Bit4: X2 品质坏
            if (x3Active && !x3Good) pack |= (1u << 5); // Bit5: X3 品质坏
            if (curDevH) pack |= (1u << 6);      // Bit6: DevH 偏差大
            if (outBad)  pack |= (1u << 7);      // Bit7: QA 品质坏
            if (curMode == 0) pack |= (1u << 8); // Bit8: AVG 模式
            if (curMode == 1) pack |= (1u << 9); // Bit9: MIN 模式
            if (curMode == 2) pack |= (1u << 10);// Bit10: MAX 模式
            if (curMode == 3) pack |= (1u << 11);// Bit11: MIDDLE 模式
            // 规范 Bit12/13/14 为"输入 Xi 偏差越限指示"：Xi 参与的任一对越限即置位
            if (d12 || d13) pack |= (1u << 12);  // Bit12: X1 涉及的偏差越限
            if (d12 || d23) pack |= (1u << 13);  // Bit13: X2 涉及的偏差越限
            if (d13 || d23) pack |= (1u << 14);  // Bit14: X3 涉及的偏差越限
            if (notFirstRun) pack |= (1u << 15); // Bit15: 非首次运算
            TAG.Value = pack;
        }

        private float ComputeMode2(float a, float b)
        {
            // 两点优选：curMode 中 MIDDLE(3) 在两点情况等同 AVG
            if (curMode == 1) return Math.Min(a, b);
            if (curMode == 2) return Math.Max(a, b);
            return (a + b) / 2;
        }

        private float ComputeMode3(float a, float b, float c)
        {
            if (curMode == 0) return (a + b + c) / 3;
            if (curMode == 1) return Math.Min(Math.Min(a, b), c);
            if (curMode == 2) return Math.Max(Math.Max(a, b), c);
            if (curMode == 3)
            {
                // 中值：去最大与最小后剩下的点
                if ((a - b) * (a - c) <= 0) return a;
                if ((b - a) * (b - c) <= 0) return b;
                return c;
            }
            return (a + b + c) / 3;
        }
    }
}
