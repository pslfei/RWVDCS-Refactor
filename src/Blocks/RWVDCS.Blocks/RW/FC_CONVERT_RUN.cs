using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;
using System.ComponentModel;
namespace RWVDCS.Blocks.RW
{
    public partial class CONVERT
    {

        protected override void Run(ICommand cmd)
        {
            // 1. 模块使能逻辑
            _ENO[0] = Enable;
            if (!Enable)
            {
                return;
            }


            // 2. 获取输入值并截断为16位无符号整数 (高字和低字，范围 0~0xFFFF)
            uint highWord = (uint)HWIn & 0xFFFF;
            uint lowWord = (uint)LWIn & 0xFFFF;

            // 3. 字节序处理 (功能描述：当 ByteOrder=SWAP(1)时，先将输入的高低字节置换)
            if (ByteOrder == 1)
            {
                // 将16位字内的 高8位 和 低8位 互换 (例如：0x1234 -> 0x3412)
                highWord = ((highWord & 0x00FF) << 8) | ((highWord & 0xFF00) >> 8);
                lowWord = ((lowWord & 0x00FF) << 8) | ((lowWord & 0xFF00) >> 8);
            }

            // 4. 合成 4 字节 (32位)
            // 将高字左移16位，然后与低字进行按位或操作
            uint combined32Bit = (highWord << 16) | lowWord;

            // 5. 输出码值类型转换 (功能描述：根据 Type 参数决定解析方式)
            if (Type == 0) // REAL(0) - 浮点数值
            {
                // 将合成的32位内存码值按 IEEE 754 标准直接解析为 float
                byte[] bytes = BitConverter.GetBytes(combined32Bit);
                OUT[0] = BitConverter.ToSingle(bytes, 0);
            }
            else if (Type == 1) // DINT(1) - 有符号整型值
            {
                // 强制转换为有符号32位整数，再转为float供LA引脚输出
                int signedInt = (int)combined32Bit;
                OUT[0] = (float)signedInt;
            }
            else if (Type == 2) // UDINT(2) - 无符号整型值
            {
                // 直接作为无符号32位整数转为float供LA引脚输出
                OUT[0] = (float)combined32Bit;
            }

            // 6. 品质传递逻辑 (QualityT)
            // 根据常规 DCS 逻辑处理，通常较低的枚举值代表较差的品质
            if (QualityT == 0) // NoTransfer(0): 不传递品质，默认输出为 Good
            {
                OUT.Quality = QualityTypes.Good;
            }
            else if (QualityT == 1) // OrTransfer(1): 或传递，只要有一个输入品质为坏(或非Good)，输出即为坏
            {
                if (HWIn.Quality != QualityTypes.Good || LWIn.Quality != QualityTypes.Good)
                {
                    OUT.Quality = QualityTypes.Bad; // 此处简化处理为Bad，实际平台可取 Math.Min(hw, lw)
                }
                else
                {
                    OUT.Quality = QualityTypes.Good;
                }
            }
            else if (QualityT == 2) // AndTransfer(2): 与传递，只有当两个输入品质都为坏时，输出才为坏
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
