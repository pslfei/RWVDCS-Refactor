using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class DIVERGE8
	{
		protected override void Run(ICommand cmd)
		{
			if (!Enable) return;

			//// 将4字节输入转换成4个单字节无符号整型输出
			//// Out1~Out4表示4个字节的输出，字节位置从低到高
			//byte[] bytes;

			//if (Type == 1)
			//{
			//	// DINT: 有符号整型
			//	int intVal = (int)(float)In;
			//	bytes = BitConverter.GetBytes(intVal);
			//}
			//else if (Type == 2)
			//{
			//	// UDINT: 无符号整型
			//	uint uintVal = (uint)(float)In;
			//	bytes = BitConverter.GetBytes(uintVal);
			//}
			//else
			//{
			//	// REAL(0): 浮点数（默认）
			//	float floatVal = (float)In;
			//	bytes = BitConverter.GetBytes(floatVal);
			//}

			//// 字节位置从低到高：Out1=最低字节, Out4=最高字节
			//Out1[0] = bytes[0];
			//Out2[0] = bytes[1];
			//Out3[0] = bytes[2];
			//Out4[0] = bytes[3];

            // 获取输入值
            float inVal = In;

            // 用于存储统一的 32 位底层二进制数据
            uint rawBits = 0;
            // 2. 根据 Type 参数解析底层 32 位数据
            if (Type == 1)
            {
                // DINT (1): 有符号整型
                rawBits = (uint)inVal;
            }
            else if (Type == 2)
            {
                // UDINT (2): 无符号整型
                rawBits = (uint)inVal;
            }
            else
            {
                // REAL(0) 及默认: 浮点数 (获取 IEEE-754 底层位模式)
                byte[] bytes = BitConverter.GetBytes(inVal);
                rawBits = BitConverter.ToUInt32(bytes, 0);
            }
            // 3. 字节拆分 (核心重构：彻底解决跨平台大小端问题)
            // 无论底层硬件是 Little-Endian 还是 Big-Endian，
            // C# 的位移操作 (>> 和 &) 总能在逻辑层面上准确提取对应的字节。
            // 需求：Out1~Out4 表示字节位置从低到高

            Out1[0] = rawBits & 0xFF;         // 取最低字节 (Byte 0)
            Out2[0] = (rawBits >> 8) & 0xFF;  // 取次低字节 (Byte 1)
            Out3[0] = (rawBits >> 16) & 0xFF; // 取次高字节 (Byte 2)
            Out4[0] = (rawBits >> 24) & 0xFF; // 取最高字节 (Byte 3)
        }
	}
}
