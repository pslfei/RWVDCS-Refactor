using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class SEL2V
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            float cycle = cmd.Dpu.Cycle;

            // ================== 1. 首次运行：curMode 取参数 MODE 初始值 ==================
            if (!notFirstRun)
            {
                curMode = MODE;
            }

            // ================== 2. HMI 命令上升沿采集 (参照 DEVICE 模式) ==================
            bool curCD1 = CD1, edgeCD1 = curCD1 && !oldCD1;
            bool curCD2 = CD2, edgeCD2 = curCD2 && !oldCD2;
            bool curCE1 = CE1, edgeCE1 = curCE1 && !oldCE1;
            bool curCE2 = CE2, edgeCE2 = curCE2 && !oldCE2;
            bool curCAG = CAG, edgeCAG = curCAG && !oldCAG;
            bool curCMN = CMN, edgeCMN = curCMN && !oldCMN;
            bool curCMX = CMX, edgeCMX = curCMX && !oldCMX;

            // ================== 3. X1/X2 启用/禁用切换 ==================
            // 规范约束：X1 和 X2 不能同时被禁用。禁用沿到达时，需要先校验另一通道处于启用状态
            if (edgeCE1) x1Disabled = false;            // 启用 X1：直接生效
            if (edgeCE2) x2Disabled = false;            // 启用 X2：直接生效
            if (edgeCD1 && !x2Disabled) x1Disabled = true;  // 禁用 X1：另一通道必须启用
            if (edgeCD2 && !x1Disabled) x2Disabled = true;  // 禁用 X2：同上

            // ================== 4. MODE 切换 (CAG/CMN/CMX) ==================
            if (edgeCAG) curMode = 0; // AVG
            if (edgeCMN) curMode = 1; // MIN
            if (edgeCMX) curMode = 2; // MAX

            // ================== 5. 输入品质评估 ==================
            bool x1Good = (X1.Quality == QualityTypes.Good);
            bool x2Good = (X2.Quality == QualityTypes.Good);

            // X1Bad / X2Bad 对应禁用时强制为 0；启用时反映品质
            X1Bad[0] = x1Disabled ? false : !x1Good;
            X2Bad[0] = x2Disabled ? false : !x2Good;

            // ================== 6. 优选计算 ==================
            bool curDevH = false;
            bool outBad = false;
            float newTarget = OUT;

            if (x1Disabled)
            {
                // X1 禁用：跟随 X2，仅当 X2 坏点时输出坏点保持
                if (x2Good)
                {
                    newTarget = X2;
                    outBad = false;
                }
                else
                {
                    outBad = true;
                }
            }
            else if (x2Disabled)
            {
                // X2 禁用：跟随 X1，仅当 X1 坏点时输出坏点保持
                if (x1Good)
                {
                    newTarget = X1;
                    outBad = false;
                }
                else
                {
                    outBad = true;
                }
            }
            else
            {
                // X1/X2 都启用
                if (!x1Good && !x2Good)
                {
                    // 两个都坏：输出保持，标记坏点
                    outBad = true;
                }
                else if (!x1Good)
                {
                    // 仅 X1 坏：跟随 X2
                    newTarget = X2;
                    outBad = false;
                }
                else if (!x2Good)
                {
                    // 仅 X2 坏：跟随 X1
                    newTarget = X1;
                    outBad = false;
                }
                else
                {
                    // 二者品质均为 GOOD：偏差校验 + MODE 计算
                    if (DB > 0.0f && Math.Abs(X1 - X2) > DB)
                    {
                        // 偏差越限：输出坏点保持，DevH=1
                        curDevH = true;
                        outBad = true;
                    }
                    else
                    {
                        // 偏差合格：按 curMode 计算（HMI 可切换）
                        outBad = false;
                        if (curMode == 0)
                            newTarget = (X1 + X2) / 2;
                        else if (curMode == 1)
                            newTarget = Math.Min(X1, X2);
                        else if (curMode == 2)
                            newTarget = Math.Max(X1, X2);
                    }
                }
            }

            DevH[0] = curDevH;

            // ================== 7. 状态签名 (用于触发速率控制) ==================
            int curStateSignature = (x1Good ? 1 : 0) | (x2Good ? 2 : 0) |
                                    (x1Disabled ? 4 : 0) | (x2Disabled ? 8 : 0) |
                                    (curDevH ? 16 : 0) | ((int)curMode << 8);

            // ================== 8. 输出 + 速率限制 ==================
            if (!notFirstRun)
            {
                // 首次运行：跳过速率限制，直接初始化
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
                    // 速率限制：每周期变化量 = RATE * (cycle / 60s)
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

            // ================== 9. 品质与综合报警 ==================
            OUT.Quality = outBad ? QualityTypes.Bad : QualityTypes.Good;
            QA[0] = outBad;
            Alm[0] = X1Bad | X2Bad | DevH;

            // ================== 10. 保存沿检测历史 ==================
            oldCD1 = curCD1;
            oldCD2 = curCD2;
            oldCE1 = curCE1;
            oldCE2 = curCE2;
            oldCAG = curCAG;
            oldCMN = curCMN;
            oldCMX = curCMX;

            // ================== 11. 状态打包点 (PACK) → TAG.Value ==================
            // 严格遵循 SEL2V.md PACK 位定义表，与 XTP 面板颜色绑定一一对应
            uint pack = 0;
            if (x1Disabled) pack |= (1u << 0);   // Bit0: X1 禁用
            if (x2Disabled) pack |= (1u << 1);   // Bit1: X2 禁用
            // Bit2 保留
            if (!x1Disabled && !x1Good) pack |= (1u << 3); // Bit3: X1 品质坏 (禁用时不报)
            if (!x2Disabled && !x2Good) pack |= (1u << 4); // Bit4: X2 品质坏
            // Bit5 保留
            if (curDevH) pack |= (1u << 6);      // Bit6: DevH 偏差大
            if (outBad)  pack |= (1u << 7);      // Bit7: QA 品质坏
            if (curMode == 0) pack |= (1u << 8); // Bit8: AVG 模式
            if (curMode == 1) pack |= (1u << 9); // Bit9: MIN 模式
            if (curMode == 2) pack |= (1u << 10);// Bit10: MAX 模式
            // Bit11~13 保留
            if (rateActive) pack |= (1u << 14);  // Bit14: 速率控制活动
            if (notFirstRun) pack |= (1u << 15); // Bit15: 非首次运算
            TAG.Value = pack;
        }
    }
}
