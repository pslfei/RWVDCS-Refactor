using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
    public partial class BALAN2
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            //if (TS1 == true && TS2 == true)
            //{
            //    OUT1[0] = TR1;
            //    OUT2[0] = TR2;
            //}

            //if (TS1 == false && TS2 == false)
            //{
            //    OUT1[0] = X + BIAS;
            //    OUT2[0] = X - BIAS;
            //}

            //if (TS1 == true && TS2 == false)
            //{
            //    OUT1[0] = TR1;
            //    OUT2[0] = 2 * X - OUT1;
            //}

            //if (TS1 == false && TS2 == true)
            //{
            //    OUT2[0] = TR2;
            //    OUT1[0] = 2 * X - OUT2;
            //}

            //if (OUT1 > H)
            //{
            //    OUT1[0] = H;
            //}

            //if (OUT1 < L)
            //{
            //    OUT1[0] = L;
            //}

            //if (OUT2 > H)
            //{
            //    OUT2[0] = H;
            //}

            //if (OUT2 < L)
            //{
            //    OUT2[0] = L;
            //}

            // 如果模块未使能，直接返回，保持 OUT1 和 OUT2 上一周期的值不变
            if (!Enable)
                return;
            // 定义局部临时变量用于计算，避免直接读写输出引脚对象引发错误
            float tempOut1 = 0.0f;
            float tempOut2 = 0.0f;
            // ================= 1. 指令分配逻辑 (根据跟踪开关) =================
            if (TS1 & TS2)
            {
                tempOut1 = TR1;
                tempOut2 = TR2;
            }
            else if (!TS1 & !TS2)
            {
                tempOut1 = X + BIAS;
                tempOut2 = X - BIAS;
            }
            else if (TS1 & !TS2)
            {
                tempOut1 = TR1;
                tempOut2 = 2 * X - tempOut1; // 使用临时变量计算
            }
            else if (!TS1 & TS2)
            {
                tempOut2 = TR2;
                tempOut1 = 2 * X - tempOut2; // 使用临时变量计算
            }
            // ================= 2. 闭锁增/减（高低限幅）逻辑 =================
            // 对于 OUT1 的限幅
            if (tempOut1 > H)
            {
                tempOut1 = H; // 达到上限，闭锁增
            }
            else if (tempOut1 < L)
            {
                tempOut1 = L; // 达到下限，闭锁减
            }
            // 对于 OUT2 的限幅
            if (tempOut2 > H)
            {
                tempOut2 = H; // 达到上限，闭锁增
            }
            else if (tempOut2 < L)
            {
                tempOut2 = L; // 达到下限，闭锁减
            }
            // ================= 3. 最终输出赋值 =================
            OUT1[0] = tempOut1;
            OUT2[0] = tempOut2;
        }
    }
}
