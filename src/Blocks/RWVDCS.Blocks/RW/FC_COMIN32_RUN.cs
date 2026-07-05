using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class COMIN32
	{
		protected override void Run(ICommand cmd)
		{
			if (!Enable)
				return;

			// 通讯四字节输入：将通道四字节输入点引用过来使用

			// 1. 获取TAG的原始32位值
			uint rawValue = (uint)TAG;

			// 2. 根据Mode参数进行字节顺序转换
			// Mode 0: Byte1234 (ABCD) - 默认顺序
			// Mode 1: Byte2143 (BADC) - 字内字节互换
			// Mode 2: Byte3412 (CDAB) - 字互换
			// Mode 3: Byte4321 (DCBA) - 完全反转
			byte[] bytes = BitConverter.GetBytes(rawValue);
			byte[] ordered = new byte[4];

			switch (Mode)
			{
				case 0: // Byte1234
					ordered[0] = bytes[0];
					ordered[1] = bytes[1];
					ordered[2] = bytes[2];
					ordered[3] = bytes[3];
					break;
				case 1: // Byte2143
					ordered[0] = bytes[1];
					ordered[1] = bytes[0];
					ordered[2] = bytes[3];
					ordered[3] = bytes[2];
					break;
				case 2: // Byte3412
					ordered[0] = bytes[2];
					ordered[1] = bytes[3];
					ordered[2] = bytes[0];
					ordered[3] = bytes[1];
					break;
				case 3: // Byte4321
					ordered[0] = bytes[3];
					ordered[1] = bytes[2];
					ordered[2] = bytes[1];
					ordered[3] = bytes[0];
					break;
				default:
					ordered = bytes;
					break;
			}

			// 3. 根据Type参数进行类型转换
			uint convertedUint = BitConverter.ToUInt32(ordered, 0);
			if (Type == 0) // REAL - IEEE 754浮点数
			{
				OUT[0] = BitConverter.ToSingle(ordered, 0);
			}
			else if (Type == 1) // DINT - 有符号32位整数
			{
				OUT[0] = (float)(int)convertedUint;
			}
			else // UDINT(2) - 无符号32位整数
			{
				OUT[0] = (float)convertedUint;
			}

			// 4. 品质处理：延时内输入品质一直为坏，则判定输入品质坏
			if (TAG.Quality == QualityTypes.Good)
			{
				OUT.Quality = QualityTypes.Good;
				_badQualityElapsed = 0.0;
			}
			else
			{
				if (TIME > 0)
				{
					_badQualityElapsed += cmd.Dpu.Cycle;
					if (_badQualityElapsed >= TIME)
					{
						OUT.Quality = QualityTypes.Bad;
					}
				}
				else
				{
					OUT.Quality = QualityTypes.Bad;
				}
			}
		}
	}
}
