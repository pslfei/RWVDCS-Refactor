using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class FILTER
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // 缓存当前输入，避免下方多次触发 LA -> float 的隐式转换
            float curX = X;

            // 历史值整体后移一位：移位后 buffer[i] 表示 X(n-i)
            // 8 项 float 移位开销极小，保留循环写法以保证可读性
            for (int i = 7; i > 0; i--)
                buffer[i] = buffer[i - 1];
            buffer[0] = curX;

            // 8 阶 FIR 数字滤波：OUT(n) = Σ Ki * X(n-i+1)，i = 1..8
            // 直接展开为标量乘加，省去 coefficients 数组拷贝与循环边界检查，
            // 同时让 JIT 更易识别为 FMA 模式
            OUT[0] = K1 * buffer[0]
                   + K2 * buffer[1]
                   + K3 * buffer[2]
                   + K4 * buffer[3]
                   + K5 * buffer[4]
                   + K6 * buffer[5]
                   + K7 * buffer[6]
                   + K8 * buffer[7];

            // 品质传递：FILTER 仅 X 一个动态输入，OrTransfer 与 AndTransfer 在单输入下等效
            OUT.Quality = QualityT == 0 ? QualityTypes.Good : X.Quality;
        }
    }
}
