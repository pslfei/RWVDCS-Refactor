using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class MIN
	{
		protected override void Run(ICommand cmd)
        {
            if (cmd.Name == "1023$10$MIN43")
            {

            }
            float min = float.MaxValue;
            if (Enx1)
            {
                min = Math.Min(min, X1);
            }

            if (Enx2)
            {
                min = Math.Min(min, X2);
            }

            if (Enx3)
            {
                min = Math.Min(min, X3);
            }

            if (Enx4)
            {
                min = Math.Min(min, X4);
            }

            OUT[0] = min;
        }
	}
}
