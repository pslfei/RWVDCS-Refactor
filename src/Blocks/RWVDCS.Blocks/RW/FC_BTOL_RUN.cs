using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
    public partial class BTOL
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            // 1. 将 B0~B15 的布尔值转换为 16 位无符号整数
            uint tempVal = 0;
            if (B0)  tempVal |= (1u << 0);
            if (B1)  tempVal |= (1u << 1);
            if (B2)  tempVal |= (1u << 2);
            if (B3)  tempVal |= (1u << 3);
            if (B4)  tempVal |= (1u << 4);
            if (B5)  tempVal |= (1u << 5);
            if (B6)  tempVal |= (1u << 6);
            if (B7)  tempVal |= (1u << 7);
            if (B8)  tempVal |= (1u << 8);
            if (B9)  tempVal |= (1u << 9);
            if (B10) tempVal |= (1u << 10);
            if (B11) tempVal |= (1u << 11);
            if (B12) tempVal |= (1u << 12);
            if (B13) tempVal |= (1u << 13);
            if (B14) tempVal |= (1u << 14);
            if (B15) tempVal |= (1u << 15);

            // 2. 获取当前 32 位输出值，保留不需要修改的另一半字
            uint currentOut = (uint)OUT.Value;

            // 3. 根据 WORD 参数进行位合并
            if (WORD == 0) // 低字：保留高字不变，替换低字 (BIT0~BIT15)
            {
                currentOut = (currentOut & 0xFFFF0000) | tempVal;
            }
            else if (WORD == 1) // 高字：保留低字不变，替换高字 (BIT16~BIT31)
            {
                currentOut = (currentOut & 0x0000FFFF) | (tempVal << 16);
            }

            // 4. 将结果写回输出
            OUT[0] = currentOut;

            // 5. 品质传递逻辑
            if (QualityT == 0) // NoTransfer: 不传递品质，输出始终为Good
            {
                OUT.Quality = QualityTypes.Good;
            }
            else if (QualityT == 1) // OrTransfer: 任意一个输入品质为Bad，输出即为Bad
            {
                if (B0.Quality  != QualityTypes.Good || B1.Quality  != QualityTypes.Good ||
                    B2.Quality  != QualityTypes.Good || B3.Quality  != QualityTypes.Good ||
                    B4.Quality  != QualityTypes.Good || B5.Quality  != QualityTypes.Good ||
                    B6.Quality  != QualityTypes.Good || B7.Quality  != QualityTypes.Good ||
                    B8.Quality  != QualityTypes.Good || B9.Quality  != QualityTypes.Good ||
                    B10.Quality != QualityTypes.Good || B11.Quality != QualityTypes.Good ||
                    B12.Quality != QualityTypes.Good || B13.Quality != QualityTypes.Good ||
                    B14.Quality != QualityTypes.Good || B15.Quality != QualityTypes.Good)
                {
                    OUT.Quality = QualityTypes.Bad;
                }
                else
                {
                    OUT.Quality = QualityTypes.Good;
                }
            }
            else if (QualityT == 2) // AndTransfer: 所有输入品质均为Bad时，输出才为Bad
            {
                if (B0.Quality  != QualityTypes.Good && B1.Quality  != QualityTypes.Good &&
                    B2.Quality  != QualityTypes.Good && B3.Quality  != QualityTypes.Good &&
                    B4.Quality  != QualityTypes.Good && B5.Quality  != QualityTypes.Good &&
                    B6.Quality  != QualityTypes.Good && B7.Quality  != QualityTypes.Good &&
                    B8.Quality  != QualityTypes.Good && B9.Quality  != QualityTypes.Good &&
                    B10.Quality != QualityTypes.Good && B11.Quality != QualityTypes.Good &&
                    B12.Quality != QualityTypes.Good && B13.Quality != QualityTypes.Good &&
                    B14.Quality != QualityTypes.Good && B15.Quality != QualityTypes.Good)
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
