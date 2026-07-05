using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
	public partial class B2OF3
    {
		protected override void Run(ICommand cmd) 
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            OUT[0] = (X1 & X2) | (X2 & X3) | (X1 & X3);
		}
	}
}
