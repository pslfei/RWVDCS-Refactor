using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class DSEL
	{
		protected override void Run(ICommand cmd) 
		{
			if (!SEL)
			{
				OUT[0] = X1;
			}
			else
			{
				OUT[0] = X2;
			}
		}
	}
}
