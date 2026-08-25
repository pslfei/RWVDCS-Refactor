using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
    public partial class BTOL32
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            // 1. 将 B0~B31 的布尔值转换为 32 位无符号整数
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
            if (B16) tempVal |= (1u << 16);
            if (B17) tempVal |= (1u << 17);
            if (B18) tempVal |= (1u << 18);
            if (B19) tempVal |= (1u << 19);
            if (B20) tempVal |= (1u << 20);
            if (B21) tempVal |= (1u << 21);
            if (B22) tempVal |= (1u << 22);
            if (B23) tempVal |= (1u << 23);
            if (B24) tempVal |= (1u << 24);
            if (B25) tempVal |= (1u << 25);
            if (B26) tempVal |= (1u << 26);
            if (B27) tempVal |= (1u << 27);
            if (B28) tempVal |= (1u << 28);
            if (B29) tempVal |= (1u << 29);
            if (B30) tempVal |= (1u << 30);
            if (B31) tempVal |= (1u << 31);

            // 2. 将结果写回输出
            OUT.Value = tempVal;

            // 3. 品质传递逻辑
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
                    B14.Quality != QualityTypes.Good || B15.Quality != QualityTypes.Good ||
                    B16.Quality != QualityTypes.Good || B17.Quality != QualityTypes.Good ||
                    B18.Quality != QualityTypes.Good || B19.Quality != QualityTypes.Good ||
                    B20.Quality != QualityTypes.Good || B21.Quality != QualityTypes.Good ||
                    B22.Quality != QualityTypes.Good || B23.Quality != QualityTypes.Good ||
                    B24.Quality != QualityTypes.Good || B25.Quality != QualityTypes.Good ||
                    B26.Quality != QualityTypes.Good || B27.Quality != QualityTypes.Good ||
                    B28.Quality != QualityTypes.Good || B29.Quality != QualityTypes.Good ||
                    B30.Quality != QualityTypes.Good || B31.Quality != QualityTypes.Good)
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
                    B14.Quality != QualityTypes.Good && B15.Quality != QualityTypes.Good &&
                    B16.Quality != QualityTypes.Good && B17.Quality != QualityTypes.Good &&
                    B18.Quality != QualityTypes.Good && B19.Quality != QualityTypes.Good &&
                    B20.Quality != QualityTypes.Good && B21.Quality != QualityTypes.Good &&
                    B22.Quality != QualityTypes.Good && B23.Quality != QualityTypes.Good &&
                    B24.Quality != QualityTypes.Good && B25.Quality != QualityTypes.Good &&
                    B26.Quality != QualityTypes.Good && B27.Quality != QualityTypes.Good &&
                    B28.Quality != QualityTypes.Good && B29.Quality != QualityTypes.Good &&
                    B30.Quality != QualityTypes.Good && B31.Quality != QualityTypes.Good)
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
