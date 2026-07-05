using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class MAX
    {
        protected override void Run(ICommand cmd)
        {
            float max = 0;
            if (Enx1)
            {
                max = Math.Max(max, X1);
            }

            if (Enx2)
            {
                max = Math.Max(max, X2);
            }

            if (Enx3)
            {
                max = Math.Max(max, X3);
            }

            if (Enx4)
            {
                max = Math.Max(max, X4);
            }

            OUT[0] = max;
        }
    }
}
