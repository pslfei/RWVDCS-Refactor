using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Security.Authentication.ExtendedProtection;

namespace RWVDCS.Blocks.RW
{
    public partial class STAT
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            // BitDis 一次性转为整型位掩码：LA 重载了 operator &(LA, float) 返回 float，
            // 若按原写法在 8 次循环内反复调用，会触发 8 次方法调用 + 浮点位运算转换；
            // 这里折算成单次 LA->float->int + 8 次整数 AND，开销降至原来的 1/8 以下
            int mask = (int)BitDis;

            // 用栈上局部变量替代 new List<float> 与 new LA[8]，彻底消除 GC 压力
            float sumVal = 0f;
            float maxVal = float.MinValue;
            float minVal = float.MaxValue;
            int count = 0;

            // BitDis 第 i 位为 1 表示 X(i+1) 参与统计，行为与原版完全一致
            if ((mask & 0x01) == 0) { float v = X1; sumVal += v; if (v > maxVal) maxVal = v; if (v < minVal) minVal = v; count++; }
            if ((mask & 0x02) == 0) { float v = X2; sumVal += v; if (v > maxVal) maxVal = v; if (v < minVal) minVal = v; count++; }
            if ((mask & 0x04) == 0) { float v = X3; sumVal += v; if (v > maxVal) maxVal = v; if (v < minVal) minVal = v; count++; }
            if ((mask & 0x08) == 0) { float v = X4; sumVal += v; if (v > maxVal) maxVal = v; if (v < minVal) minVal = v; count++; }
            if ((mask & 0x10) == 0) { float v = X5; sumVal += v; if (v > maxVal) maxVal = v; if (v < minVal) minVal = v; count++; }
            if ((mask & 0x20) == 0) { float v = X6; sumVal += v; if (v > maxVal) maxVal = v; if (v < minVal) minVal = v; count++; }
            if ((mask & 0x40) == 0) { float v = X7; sumVal += v; if (v > maxVal) maxVal = v; if (v < minVal) minVal = v; count++; }
            if ((mask & 0x80) == 0) { float v = X8; sumVal += v; if (v > maxVal) maxVal = v; if (v < minVal) minVal = v; count++; }

            if (count == 0)
            {
                OUT[0] = 0f;
                CNT[0] = 0;
                return;
            }

            if (cmd.Name == "1018$50$STAT13")
            {

            }

            switch (MODE)
            {
                case 0: OUT[0] = sumVal; break;          // Sum
                case 1: OUT[0] = sumVal / count; break;  // Average
                case 2: OUT[0] = maxVal; break;          // Max
                case 3: OUT[0] = minVal; break;          // Min
                default: OUT[0] = 0f; break;
            }

            CNT[0] = count;
        }
    }
}
