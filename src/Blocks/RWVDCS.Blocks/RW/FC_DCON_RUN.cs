using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
	public partial class DCON
    {
		protected override void Run(ICommand cmd)  
		{
            _ENO[0] = Enable;
            if (!Enable)
                return;

			OUT[0] = Value;     
		}
	}
}
