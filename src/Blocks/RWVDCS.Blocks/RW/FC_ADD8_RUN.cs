using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;


namespace RWVDCS.Blocks.RW
{
	public partial class ADD8
    {

        protected override void Run(ICommand cmd)
        {

            _ENO[0] = Enable;
            if (!Enable)
                return;

            OUT[0] = k1 * X1 + b1 + k2 * X2 + b2 + k3 * X3 + b3 + k4 * X4 + b4 + k5 * X5 + b5 + k6 * X6 + b6 + k7 * X7 + b7 + k8 * X8 + b8;

        }
    }
}
