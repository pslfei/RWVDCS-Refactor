using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
	public partial class ALM
    {
		protected override void Run(ICommand cmd)  
		{
            _ENO[0] = Enable;
            if (!Enable)
                return;
         
            if (EnH)
            {
                if (X > H)
                {
                    HAlm[0] = true;
                }
                else if (X <= H - DBH)
                {
                    HAlm[0] = false;
                }
            }
            else
            {
                HAlm[0] = false;
            }

            if (EnL)
            {
                if (X < L)
                {
                    LAlm[0] = true;
                }
                else if (X >= L + DBL)
                {
                    LAlm[0] = false;
                }
            }
            else
            {
                LAlm[0] = false;
            }
            Alm[0] = HAlm | LAlm;

        }
    }
}
