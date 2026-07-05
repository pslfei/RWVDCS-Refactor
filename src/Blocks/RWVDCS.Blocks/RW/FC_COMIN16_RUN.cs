using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.ComponentModel;
namespace RWVDCS.Blocks.RW
{
    public partial class COMIN16
    {

        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            OUT[0] = TAG;
        }
    }
}
