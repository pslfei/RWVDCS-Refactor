using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class PNI
	{
		protected override void Run(ICommand cmd) 
		{
			OUT[0] = TAG;
		}
	}
}
