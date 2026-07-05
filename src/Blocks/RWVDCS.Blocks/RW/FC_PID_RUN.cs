using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class PID
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            // 提取基础输入，避免反复拆箱引发性能损耗
            float cycle = cmd.Dpu.Cycle; // 运算周期 T (单位：秒)
            float e = E;
            float curFF = FF;
            float prevOut = OUT; // 上一周期输出 OUT(n-1)
            // 正反作用处理：PoN为True(反作用)时，偏差反转
            if (PoN) e = -e;
            // =========================================================================
            // 2. 跟踪模式 (Tracking)
            // =========================================================================
            if ((bool)TS)
            {
                float trVal = TR;

                if (trVal >= H) 
                {
                    trVal = H;
                }
                else if (trVal <= L)
                {
                    trVal = L;
                }

                OUT[0] = trVal;
                OUT.Quality = TR.Quality;

                // 跟踪状态下，清空内部历史状态，实现无扰切换
                prevE = e;
                prevFF = curFF;
                prevOUTd = 0.0f; // 纯微分环节清零

                HAlm[0] = false;
                LAlm[0] = false;
                DOUTp[0] = 0.0f;
                DOUTi[0] = 0.0f;
                DOUTd[0] = 0.0f;
                return;
            }
            // =========================================================================
            // 3. 正常控制运算 (增量型 PID 计算)
            // =========================================================================

            // 3.1 积分分离判断与 Kp 补偿修正
            // 如果 |E(n)| > EDB > 0，则停止积分
            bool integralSeparated = (EDB > 0.0 && Math.Abs(e) > EDB);
            float kpVal = Kp;
            // 积分器停止积分时，修正后 Kp = 原Kp + Dk
            float kpEff = integralSeparated ? kpVal + (float)Dk : kpVal;
            // 3.2 比例增量计算: ΔOUTP(n) = Kp * [E(n) - E(n-1)]
            float dOutP = 0.0f;
            if (kpEff != 0.0f)
            {
                dOutP = kpEff * (e - prevE);
            }
            // 3.3 积分增量计算: ΔOUTI(n) = (T / Ti) * E(n)
            float dOutI = 0.0f;
            float tiVal = Ti;
            if (tiVal > 0.0f && !integralSeparated)
            {
                dOutI = (cycle / tiVal) * e;
            }
            // 3.4 微分增量计算
            // 离散公式: OUTD(n) = [Td / (Td + T)] * OUTD(n-1) + [Kd * Td / (Td + T)] * [E(n) - E(n-1)]
            // ΔOUTD(n) = OUTD(n) - OUTD(n-1)
            float dOutD = 0.0f;
            float tdVal = Td;
            if (tdVal > 0.0f)
            {
                float denom = tdVal + cycle; // Td + T
                float currentOUTd = (tdVal / denom) * prevOUTd + (Kd * tdVal / denom) * (e - prevE);
                dOutD = currentOUTd - prevOUTd;
                prevOUTd = currentOUTd; // 记录当周期纯微分结果
            }
            else
            {
                prevOUTd = 0.0f;
            }
            // 3.5 前馈增量计算: ΔOUTFF(n) = FF(n) - FF(n-1)
            float dOutFF = curFF - prevFF;
            // 3.6 总输出计算: OUT(n) = OUT(n-1) + ΔOUTP + ΔOUTI + ΔOUTD + ΔOUTFF
            float totalDelta = dOutP + dOutI + dOutD + dOutFF;
            float currentOut = prevOut + totalDelta;
            // =========================================================================
            // 4. 闭锁增/减判断 (Block Increase/Decrease)
            // =========================================================================
            if ((bool)LI && currentOut > prevOut) currentOut = prevOut; // 闭锁增
            if ((bool)LD && currentOut < prevOut) currentOut = prevOut; // 闭锁减
            // =========================================================================
            // 5. 绝对限幅与报警 (Limits and Alarms)
            // =========================================================================
            float max = H;
            float min = L;
            if (min > max) min = max; // 防呆容错：防止组态时低限填得比高限还大
            bool hAlm = false;
            bool lAlm = false;
            if (currentOut >= max)
            {
                currentOut = max;
                hAlm = true;
            }
            else if (currentOut <= min)
            {
                currentOut = min;
                lAlm = true;
            }
            // =========================================================================
            // 6. 结果输出与历史状态缓存
            // =========================================================================
            OUT[0] = currentOut;
            HAlm[0] = hAlm;
            LAlm[0] = lAlm;

            // 暴露各增量以便画面或调试观察
            DOUTp[0] = dOutP;
            DOUTi[0] = dOutI;
            DOUTd[0] = dOutD;

            OUT.Quality = E.Quality;
            // 缓存当周期状态留作下周期(n-1)使用
            prevE = e;
            prevFF = curFF;
        }
    }
}
