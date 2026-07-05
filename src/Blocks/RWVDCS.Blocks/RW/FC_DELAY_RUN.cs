using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class DELAY
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            const int BUF_LEN = 30; // 与基类 buffer SizeConst 保持一致
            float T = (float)cmd.Dpu.Cycle; // 扫描周期(秒)

            bool overflow = false; // d > 30 时置位，仅在正常运算分支可能触发

            if (TS)
            {
                // 1. TS=1: 跟踪状态，OUT(n) = TR(n)
                // 用 TR 当前值预填整个缓冲区，避免切回正常模式时残留旧值导致输出阶跃
                float trVal = TR;
                OUT[0] = trVal;

                for (int i = 0; i < BUF_LEN; i++)
                    buffer[i] = trVal;
                bufIndex = 0;
            }
            else
            {
                // 2. TS=0: 正常运算
                // 输入/参数一次性缓存为本地变量，避免后续公式多次触发 LA -> float 的隐式转换
                float xVal = X;
                double DTval = DT;
                double LTval = LT;
                double Kval = K;

                // 滞后周期数 d = DT / T 取整 (DT、T 均为秒，文档 §5.2.3)
                int d = 0;
                if (T > 0f && DTval > 0.0)
                    d = (int)Math.Round(DTval / T);
                if (d < 0) d = 0;

                // 文档要求: 缓冲长度 30，当 d > 30 时功能块进入溢出状态。
                // 这里按 d = 30 完成计算以维持控制回路输出连续性，
                // 同时通过 overflow 标志强制将 OUT 品质标记为 Bad 上抛
                if (d > BUF_LEN)
                {
                    d = BUF_LEN;
                    overflow = true;
                }

                // 当前输入写入环形缓冲
                buffer[bufIndex] = xVal;
                bufIndex = (bufIndex + 1) % BUF_LEN;

                // 读取延迟 d 个周期前的输入 X(n-d)
                float delayedX = d == 0
                    ? xVal
                    : buffer[(bufIndex - d + BUF_LEN) % BUF_LEN];

                // 离散化:
                // OUT(n) = LT / (LT + T) * OUT(n-1) + K * T / (LT + T) * X(n-d)
                double denom = LTval + T;
                float prevOut = OUT;
                if (denom > 0.0)
                {
                    float a = (float)(LTval / denom);
                    float b = (float)(Kval * T / denom);
                    OUT[0] = a * prevOut + b * delayedX;
                }
                else
                {
                    OUT[0] = (float)(Kval * delayedX);
                }
            }

            // 3. 品质传递逻辑
            // 溢出态优先级高于 QualityT 配置：参数越界必须以 Bad 品质对外告知
            if (overflow)
            {
                OUT.Quality = QualityTypes.Bad;
            }
            else
            {
                switch (QualityT)
                {
                    case 1: // OrTransfer: 任一输入品质 Bad 则输出 Bad
                        OUT.Quality = (X.Quality != QualityTypes.Good || TR.Quality != QualityTypes.Good)
                            ? QualityTypes.Bad
                            : QualityTypes.Good;
                        break;
                    case 2: // AndTransfer: 所有输入品质均 Bad 时输出才 Bad
                        OUT.Quality = (X.Quality != QualityTypes.Good && TR.Quality != QualityTypes.Good)
                            ? QualityTypes.Bad
                            : QualityTypes.Good;
                        break;
                    default: // NoTransfer: 输出始终 Good
                        OUT.Quality = QualityTypes.Good;
                        break;
                }
            }
        }
    }
}
