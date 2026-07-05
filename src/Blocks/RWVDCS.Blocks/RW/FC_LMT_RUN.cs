using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class LMT
	{
		protected override void Run(ICommand cmd) 
		{
			if (X > H)
			{
				OUT[0] = H;
			}
            else if (X < L)
			{
				OUT[0] = L;
            }
			else
			{
                OUT[0] = X;
			}
		}
	}
}
