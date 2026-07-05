using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Security.Cryptography.X509Certificates;

namespace RWVDCS.Blocks.RW
{
	public partial class RLMT
	{

		protected override void Run(ICommand cmd) 
		{
			if (OUT < X)
			{
				OUT[0] = OUT + IRL * cmd.Dpu.Cycle / 60;
                if (OUT > X)
                {
                    OUT[0] = X;
                }
            }
			else if (OUT > X)
			{
                OUT[0] = OUT - DRL * cmd.Dpu.Cycle / 60;
                if (OUT < X)
                {
                    OUT[0] = X;
                }
            }
		}
	}
}
