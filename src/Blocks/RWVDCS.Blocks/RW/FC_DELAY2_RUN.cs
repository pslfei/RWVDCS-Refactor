using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class DELAY2
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            float T = cmd.Dpu.Cycle; // 扫描周期(秒)

            // 1. TS=1: 跟踪状态，OUT(n)=TR(n)
            if (TS)
            {
                OUT[0] = TR;
                // 跟踪时用TR值填充缓冲区，切回正常模式时不产生阶跃
                for (int i = 0; i < 120; i++)
                    buffer[i] = TR;
                bufIndex = 0;
            }
            else
            {
                // 2. TS=0: 正常运算
                // 滞后周期数 d = DT / T，取整；缓冲区长度120，d>120时溢出
                int d = 0;
                if (T > 0 && DT > 0)
                    d = (int)Math.Round(DT / T);
                if (d < 0) d = 0;
                if (d > 120) d = 120;

                // 将当前输入存入环形缓冲区
                buffer[bufIndex] = X;
                bufIndex = (bufIndex + 1) % 120;

                // 读取延迟d个周期前的输入值 X(n-d)
                float delayedX;
                if (d == 0)
                {
                    delayedX = X;
                }
                else
                {
                    int delayIdx = (bufIndex - d + 120) % 120;
                    delayedX = buffer[delayIdx];
                }

                // 离散化计算:
                // OUT(n) = LT/(LT+T) * OUT(n-1) + K*T/(LT+T) * X(n-d)
                float prevOut = OUT;
                if (LT + T > 0)
                {
                    float a = (LT / (LT + T));
                    float b = (K * T / (LT + T));
                    OUT[0] = a * prevOut + b * delayedX;
                }
                else
                {
                    OUT[0] = (K * delayedX);
                }
            }

            // 3. 品质传递逻辑
            if (QualityT == 0) // NoTransfer: 不传递品质，输出始终为Good
            {
                OUT.Quality = QualityTypes.Good;
            }
            else if (QualityT == 1) // OrTransfer: 任意一个输入品质为Bad，输出即为Bad
            {
                if (X.Quality != QualityTypes.Good || TR.Quality != QualityTypes.Good)
                    OUT.Quality = QualityTypes.Bad;
                else
                    OUT.Quality = QualityTypes.Good;
            }
            else if (QualityT == 2) // AndTransfer: 所有输入品质均为Bad时，输出才为Bad
            {
                if (X.Quality != QualityTypes.Good && TR.Quality != QualityTypes.Good)
                    OUT.Quality = QualityTypes.Bad;
                else
                    OUT.Quality = QualityTypes.Good;
            }
        }
    }
}
