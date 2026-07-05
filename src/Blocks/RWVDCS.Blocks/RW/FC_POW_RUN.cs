using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class POW
	{
		protected override void Run(ICommand cmd) 
		{
			OUT[0] = Math.Pow(X1, X2);
		}
	}
}
