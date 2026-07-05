using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class SQRT
    {
        protected override void Run(ICommand cmd)
        {
            if (X <= DB)
            {
                OUT[0] = 0;
            }
            else
            {
                float sqrtValue = kX * X + bX;
                if (sqrtValue >= 0)
                {
                    OUT[0] = k * Math.Sqrt(sqrtValue);
                }
            }
        }
    }
}
