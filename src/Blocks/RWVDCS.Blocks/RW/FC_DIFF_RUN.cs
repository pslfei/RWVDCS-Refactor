using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
    public partial class DIFF
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            //float T = cmd.Dpu.Cycle;
            //OUT[0] = Td / (T + Td) * OLD_OUT + (Kd + Td) / (T + Td) * (X - OLD_X);

            //OLD_X = X;
            //OLD_OUT = OUT;

            float T = cmd.Dpu.Cycle;

            // 提取输入值，使用局部变量提高运算效率
            float currentX = X;
            float outTemp = 0.0f;
            // 3. 微分差分公式运算
            float denominator = T + Td;
            // 安全校验：如果分母极小，视为 Td 接近于 0（无惯性也无微分），输出直接为0
            if (denominator > 0.000001f)
            {
                // 修复 Bug：将原代码的 (Kd + Td) 修正为文档公式要求的 (Kd * Td)
                float term1 = (Td / denominator) * OLD_OUT;
                float term2 = ((Kd * Td) / denominator) * (currentX - OLD_X);

                outTemp = term1 + term2;
            }
            // 4. 输出限幅处理 (非常重要：防止微分尖峰击穿系统)
            // 采用 H 和 L 管脚进行上下限钳位
            if (outTemp > H)
            {
                outTemp = H;
            }
            else if (outTemp < L)
            {
                outTemp = L;
            }
            // 5. 更新历史状态，供下个周期计算使用
            // 注意：存入的 OLD_OUT 应该是限幅后的值，以防积分/微分风缩 (Windup)
            OLD_X = currentX;
            OLD_OUT = outTemp;
            // 6. 赋值给输出对象
            OUT[0] = outTemp;
        }
    }
}
