using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;


namespace RWVDCS.Blocks.RW
{
	public partial class ADD
    {

        protected override void Run(ICommand cmd)
        {

            _ENO[0] = Enable;
            if (!Enable)
                return;

            OUT[0] = k1 * X1 + b1 + k2 * X2 + b2;

        }
    }
}
