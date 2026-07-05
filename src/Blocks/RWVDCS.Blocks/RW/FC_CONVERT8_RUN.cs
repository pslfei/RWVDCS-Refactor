using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class CONVERT8
	{
		protected override void Run(ICommand cmd)
		{
            //if (!Enable) return;

            //// 根据字节高低位合成2字节输出
            //byte hi = (byte)(int)(float)HWIn;
            //byte lo = (byte)(int)(float)LWIn;
            //byte[] bytes = new byte[] { lo, hi };

            //if (Type == 1)
            //{
            //    // DINT: 输出为输入合成码值所对应的有符号整型值
            //    short val = BitConverter.ToInt16(bytes, 0);
            //    OUT[0] = val;
            //}
            //else if (Type == 2)
            //{
            //    // UDINT: 输出为输入合成码值所对应的无符号整型值
            //    ushort val = BitConverter.ToUInt16(bytes, 0);
            //    OUT[0] = val;
            //}
            //else
            //{
            //    // REAL(0): 输出为输入合成码值所对应的浮点数值
            //    // 2字节合成16位整数，再转为浮点
            //    ushort val = BitConverter.ToUInt16(bytes, 0);
            //    OUT[0] = val;
            //}


            _ENO[0] = Enable;
            if (!Enable)
                return;

            // 2. 获取输入值并截断为 8 位无符号整数 (高字节和低字节，范围限制在 0~0xFF)
            uint highByte = (uint)HWIn & 0xFF;
            uint lowByte = (uint)LWIn & 0xFF;

            // 3. 合成 2 字节 (16位)
            // 将高字节左移 8 位，然后与低字节按位或，拼接成 16 位无符号整数
            uint combined16Bit = (highByte << 8) | lowByte;

            // 4. 输出码值类型转换 (根据 Type 参数决定如何解析这 16 位码值)
            if (Type == 1) // 1: DINT - 对应有符号整型
            {
                // 将 16 位数据强转为有符号的 short (Int16)，处理如 0xFFFF -> -1 的符号位问题
                short signedVal = (short)combined16Bit;
                OUT[0] = (float)signedVal;
            }
            else if (Type == 2) // 2: UDINT - 对应无符号整型
            {
                // 将 16 位数据作为无符号 ushort (UInt16) 直接转为 float (范围 0 ~ 65535)
                OUT[0] = (float)combined16Bit;
            }
            else // 0: REAL - 浮点数值
            {
                // 说明：因为 2 字节不足以拼成一个 32 位的标准 IEEE 754 浮点数，
                // 此处按照规格书“码值所对应的浮点数值”最合理的解释：
                // 将其同无符号整数一样处理，直接将数值本身赋给浮点类型的 OUT 引脚。
                OUT[0] = (float)combined16Bit;
            }

            // 5. 品质传递逻辑处理 (QualityT)
            if (QualityT == 0) // NoTransfer(0): 不传递品质，输出始终为 Good
            {
                OUT.Quality = QualityTypes.Good;
            }
            else if (QualityT == 1) // OrTransfer(1): 或传递 (任意一个输入为Bad，输出即为Bad)
            {
                if (HWIn.Quality != QualityTypes.Good || LWIn.Quality != QualityTypes.Good)
                {
                    OUT.Quality = QualityTypes.Bad;
                }
                else
                {
                    OUT.Quality = QualityTypes.Good;
                }
            }
            else if (QualityT == 2) // AndTransfer(2): 与传递 (所有输入均为Bad时，输出才为Bad)
            {
                if (HWIn.Quality != QualityTypes.Good && LWIn.Quality != QualityTypes.Good)
                {
                    OUT.Quality = QualityTypes.Bad;
                }
                else
                {
                    OUT.Quality = QualityTypes.Good;
                }
            }
        }
	}
}
