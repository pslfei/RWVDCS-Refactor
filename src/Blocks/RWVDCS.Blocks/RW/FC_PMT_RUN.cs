using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class PMT
    {
        protected override void Run(ICommand cmd)
        {
            //OUT[0] = X1 & X2 & X3 & X4 & X5 & X6 & X7 & X8 & X9 & X10 & X11 & X12 & X13 & X14 & X15 & X16;


            //UInt32 result = 0;
            //bool[] values = new bool[17] { X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14, X15, X16 , OUT };
            //for (int i = 0; i < values.Length; i++)
            //{
            //    if (values[i])
            //    {
            //        result |= (uint)(1 << i);
            //    }
            //}

            //PACK[0] = result;

            bool outState = X1 & X2 & X3 & X4 & X5 & X6 & X7 & X8 & X9 & X10 & X11 & X12 & X13 & X14 & X15 & X16;
            OUT[0] = outState;
            // 3. 高性能打包逻辑 (0内存分配，消除 new bool[] 带来的 GC 灾难)
            uint packValue = 0;
            if (X1) packValue |= (1u << 0);
            if (X2) packValue |= (1u << 1);
            if (X3) packValue |= (1u << 2);
            if (X4) packValue |= (1u << 3);
            if (X5) packValue |= (1u << 4);
            if (X6) packValue |= (1u << 5);
            if (X7) packValue |= (1u << 6);
            if (X8) packValue |= (1u << 7);
            if (X9) packValue |= (1u << 8);
            if (X10) packValue |= (1u << 9);
            if (X11) packValue |= (1u << 10);
            if (X12) packValue |= (1u << 11);
            if (X13) packValue |= (1u << 12);
            if (X14) packValue |= (1u << 13);
            if (X15) packValue |= (1u << 14);
            if (X16) packValue |= (1u << 15);

            // Bit16 为输出端 OUT 的状态
            if (outState) packValue |= (1u << 16);

            OUT[0] = outState;
            // 4. 将 uint 赋值给 LA(浮点型) 
            // 17位的最大值为 0x1FFFF(131071)，32位 IEEE754 float 可以完全无损精度地表示该整数
            PACK[0] = packValue;
        }
    }
}
