using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class LTOB
    {
        protected override void Run(ICommand cmd)
        {
            //LD[] lDs = new LD[16] { B0, B1, B2, B3, B4, B5, B6, B7, B8, B9, B10, B11, B12, B13, B14, B15 };
            //if (WORD == 0)
            //{
            //    for (int i = 0; i < lDs.Length; i++)
            //    {
            //        lDs[i].Value = (X & (1L << i)) != 0;
            //    }
            //}
            //else if (WORD == 1)
            //{
            //    for (int i = 0; i < lDs.Length; i++)
            //    {
            //        lDs[i].Value = (X & (1L << (31 - i))) != 0;
            //    }
            //}


            // 2. 将模拟量(float)安全的转换为32位无符号整数(uint)
            // unchecked 防止用户输入的浮点数超过 int 最大值时抛出溢出异常
            uint inputVal = unchecked((uint)X);
            // 3. 执行位提取与赋值 (采用循环展开，性能最高，0内存分配)
            if (WORD == 0) // 低字模式: 提取 Bit0 ~ Bit15
            {
                B0[0] = (inputVal & (1u << 0)) != 0;
                B1[0] = (inputVal & (1u << 1)) != 0;
                B2[0] = (inputVal & (1u << 2)) != 0;
                B3[0] = (inputVal & (1u << 3)) != 0;
                B4[0] = (inputVal & (1u << 4)) != 0;
                B5[0] = (inputVal & (1u << 5)) != 0;
                B6[0] = (inputVal & (1u << 6)) != 0;
                B7[0] = (inputVal & (1u << 7)) != 0;
                B8[0] = (inputVal & (1u << 8)) != 0;
                B9[0] = (inputVal & (1u << 9)) != 0;
                B10[0] = (inputVal & (1u << 10)) != 0;
                B11[0] = (inputVal & (1u << 11)) != 0;
                B12[0] = (inputVal & (1u << 12)) != 0;
                B13[0] = (inputVal & (1u << 13)) != 0;
                B14[0] = (inputVal & (1u << 14)) != 0;
                B15[0] = (inputVal & (1u << 15)) != 0;
            }
            else if (WORD == 1) // 高字模式: 提取 Bit16 ~ Bit31 (修复了原代码顺序写反的致命Bug)
            {
                B0[0] = (inputVal & (1u << 16)) != 0;
                B1[0] = (inputVal & (1u << 17)) != 0;
                B2[0] = (inputVal & (1u << 18)) != 0;
                B3[0] = (inputVal & (1u << 19)) != 0;
                B4[0] = (inputVal & (1u << 20)) != 0;
                B5[0] = (inputVal & (1u << 21)) != 0;
                B6[0] = (inputVal & (1u << 22)) != 0;
                B7[0] = (inputVal & (1u << 23)) != 0;
                B8[0] = (inputVal & (1u << 24)) != 0;
                B9[0] = (inputVal & (1u << 25)) != 0;
                B10[0] = (inputVal & (1u << 26)) != 0;
                B11[0] = (inputVal & (1u << 27)) != 0;
                B12[0] = (inputVal & (1u << 28)) != 0;
                B13[0] = (inputVal & (1u << 29)) != 0;
                B14[0] = (inputVal & (1u << 30)) != 0;
                B15[0] = (inputVal & (1u << 31)) != 0;
            }
        }
    }
}
