using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class XOR
	{
		protected override void Run(ICommand cmd) 
		{
			OUT[0] = (!X1 & X2) || (X1 & !X2);

        }
	}
}
