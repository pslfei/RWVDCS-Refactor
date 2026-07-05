using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
	public partial class ASELV
	{
		protected override void Run(ICommand cmd) 
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            if (SEL)
            {
                DOWNSEL = false;
            }
            else
            {
                UPSEL = false;
            }

            if (T21 == 0)
			{
                if (!SEL)
                {
                    OUT[0] = X1;
                }
			}
            else
            {
                if (!OLDSEL && SEL == false)
                {
                    DOWNSEL = true;
                }

                if (DOWNSEL)
                {
                    //输出OUT将从X2切换到X1
                    float Diff = X1 - X2;
                    OUT[0] = X2 + T21 * Diff;
                    if ((Diff > 0 && OUT > X1) || (Diff < 0 && OUT < X1))
                    {
                        OUT[0] = X1;
                    }
                }
            }

            if (T12 == 0)
            {
                if (SEL)
                {
                    OUT[0] = X2;
                }
            }
            else
            {
                if (OLDSEL && SEL == true)
                {
                    UPSEL = true;
                }

                if (UPSEL)
                {
                    //输出OUT将从X1切换到X2
                    float Diff = X2 - X1;
                    OUT[0] = X1 + T12 * Diff;
 
                    if ((Diff > 0 && OUT > X2) || (Diff < 0 && OUT < X2))
                    {
                        OUT[0] = X2;
                    }
                }
            }
            OLDSEL = SEL;
        }
    }
}
