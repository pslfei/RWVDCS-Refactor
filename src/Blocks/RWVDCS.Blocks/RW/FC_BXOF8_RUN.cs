using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
    public partial class BXOF8
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // 无分支计算 (Branchless): 利用三元运算符直接转为整型相加
            // 这种写法不仅代码极简，还能避免 CPU 分支预测失败带来的性能损耗
            int count = (X1 ? 1 : 0) +
                        (X2 ? 1 : 0) +
                        (X3 ? 1 : 0) +
                        (X4 ? 1 : 0) +
                        (X5 ? 1 : 0) +
                        (X6 ? 1 : 0) +
                        (X7 ? 1 : 0) +
                        (X8 ? 1 : 0);

            // 直接将逻辑表达式的结果赋给布尔变量
            OUT[0] = count >= NUM;
            TNum[0] = count;

        }
    }
}
