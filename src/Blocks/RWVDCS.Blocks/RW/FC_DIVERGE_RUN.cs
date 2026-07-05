using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class DIVERGE
	{
		protected override void Run(ICommand cmd) 
		{
            //// 将浮点数转换为字节数组
            //byte[] floatBytes = BitConverter.GetBytes(In);

            //// 确定字节序
            //if (ByteOrder == 1) // SWAP
            //{
            //    Array.Reverse(floatBytes);
            //}

            //// 提取两个short值
            //LWOut[0] = BitConverter.ToInt16(floatBytes, 0);
            //HWOut[0] = BitConverter.ToInt16(floatBytes, 2);


            // 提取输入值
            float inVal = In;

            // 用于存储统一的 32 位底层二进制数据
            uint rawBits = 0;
            // 2. 根据 Type 参数决定如何解析输入数据的底层二进制
            if (Type == 0) // REAL (0): 按 IEEE-754 浮点数解析
            {
                byte[] bytes = BitConverter.GetBytes(inVal);
                rawBits = BitConverter.ToUInt32(bytes, 0);
            }
            else if (Type == 1) // DINT (1): 按 32位有符号整型解析
            {
                // 先将数值转换为整型，再取其底层的位模式
                int intVal = (int)inVal;
                rawBits = (uint)intVal;
            }
            else if (Type == 2) // UDINT (2): 按 32位无符号整型解析
            {
                uint uintVal = (uint)inVal;
                rawBits = uintVal;
            }
            else
            {
                // 默认防呆处理，按浮点数处理
                byte[] bytes = BitConverter.GetBytes(inVal);
                rawBits = BitConverter.ToUInt32(bytes, 0);
            }
            // 3. 将 32 位数据分解为两个 16 位数据 (高字和低字)
            // 使用跨平台绝对安全的位移操作，不依赖 CPU 的大小端
            ushort highWord = (ushort)((rawBits >> 16) & 0xFFFF);
            ushort lowWord = (ushort)(rawBits & 0xFFFF);
            // 4. 字节序处理 (SWAP)
            if (ByteOrder == 1) // SWAP (1): 2字节内部高低字节置换
            {
                // 对高字内部进行高低字节互换 (例如：0xABCD 变成 0xCDAB)
                highWord = (ushort)(((highWord & 0x00FF) << 8) | ((highWord & 0xFF00) >> 8));

                // 对低字内部进行高低字节互换
                lowWord = (ushort)(((lowWord & 0x00FF) << 8) | ((lowWord & 0xFF00) >> 8));
            }
            // 5. 结果输出
            // 依据文档范围 0~0xFFFF，将无符号 16 位整数赋给输出的模拟量引脚
            HWOut[0] = highWord;
            LWOut[0] = lowWord;
        }
	}
}
