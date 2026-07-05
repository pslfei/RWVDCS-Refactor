using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class SR
	{
		protected override void Run(ICommand cmd) 
		{
            bool nextQ = S | (!R & Q); ;
            Q[0] = nextQ;
            QN[0] = !nextQ;
        }
	}
}
