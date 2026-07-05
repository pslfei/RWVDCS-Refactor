using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class OR8
	{
		protected override void Run(ICommand cmd) 
		{
            OUT[0] = X1 | X2 | X3 | X4 | X5 | X6 | X7 | X8;
        }
	}
}
