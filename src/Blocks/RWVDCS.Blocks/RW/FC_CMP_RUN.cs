using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.ComponentModel;
namespace RWVDCS.Blocks.RW
{
    public partial class CMP
    {

        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            if (!Enable)
                return;

            switch (MODE)
            {
                case 0:
                    OUT[0] = (X1 == X2);
                    break;

                case 1:
                    OUT[0] = (X1 != X2);
                    break;

                case 2:
                    OUT[0] = (X1 >= X2);
                    break;

                case 3:
                    OUT[0] = (X1 <= X2);
                    break;

                case 4:
                    OUT[0] = (X1 > X2);
                    break;

                case 5:
                    OUT[0] = (X1 < X2);
                    break;
                default:
                    OUT[0] = false;
                    break;
            }
        }
    }
}
