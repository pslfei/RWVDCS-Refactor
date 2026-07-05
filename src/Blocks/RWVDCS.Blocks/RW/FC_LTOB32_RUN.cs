using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class LTOB32
    {
        protected override void Run(ICommand cmd)
        {
            //LD[] lDs = new LD[32] { B0, B1, B2, B3, B4, B5, B6, B7, B8, B9, B10, B11, B12, B13, B14, B15, B16, B17, B18, B19, B20, B21, B22, B23, B24, B25, B26, B27, B28, B29, B30, B31 };

            //for (int i = 0; i < lDs.Length; i++)
            //{
            //    lDs[i].Value = (X & (1L << i)) != 0;
            //}

            uint inputVal = unchecked((uint)X);

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

            B16[0] = (inputVal & (1u << 16)) != 0;
            B17[0] = (inputVal & (1u << 17)) != 0;
            B18[0] = (inputVal & (1u << 18)) != 0;
            B19[0] = (inputVal & (1u << 19)) != 0;
            B20[0] = (inputVal & (1u << 20)) != 0;
            B21[0] = (inputVal & (1u << 21)) != 0;
            B22[0] = (inputVal & (1u << 22)) != 0;
            B23[0] = (inputVal & (1u << 23)) != 0;
            B24[0] = (inputVal & (1u << 24)) != 0;
            B25[0] = (inputVal & (1u << 25)) != 0;
            B26[0] = (inputVal & (1u << 26)) != 0;
            B27[0] = (inputVal & (1u << 27)) != 0;
            B28[0] = (inputVal & (1u << 28)) != 0;
            B29[0] = (inputVal & (1u << 29)) != 0;
            B30[0] = (inputVal & (1u << 30)) != 0;
            B31[0] = (inputVal & (1u << 31)) != 0;
        }
    }
}
