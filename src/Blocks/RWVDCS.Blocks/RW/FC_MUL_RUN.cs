using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class MUL
	{
		protected override void Run(ICommand cmd) 
		{
			OUT[0] = (k1 * X1 + b1) * (k2 * X2 + b2);

        }
	}
}
