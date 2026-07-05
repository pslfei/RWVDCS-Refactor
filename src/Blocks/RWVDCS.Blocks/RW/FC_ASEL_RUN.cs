using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
	public partial class ASEL
	{
		protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            if (SEL)
            {
                OUT[0] = X2;
            }
            else
            {
                OUT[0] = X1;
            }
		}
	}
}
