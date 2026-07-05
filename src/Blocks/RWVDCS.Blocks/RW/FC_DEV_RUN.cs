using System;
using System.Collections;
using System.Collections.Generic;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
	public partial class DEV
    {
		protected override void Run(ICommand cmd)  
		{
            _ENO[0] = Enable;
            if (!Enable)
                return;

            float dValue = (k1 * X1 + b1) - (k2 * X2 + b2);
            float outTemp = 0.0f;
            if (dValue >= H + DBX)
            {
                outTemp = H;
            }
            else if (dValue > DBX)
            {
                outTemp = dValue - DBX;
            }
            else if (dValue >= -DBX)
            {
                outTemp = 0.0f;
            }
            else if (dValue > L - DBX)
            {
                outTemp = dValue + DBX;
            }
            else
            {
                outTemp = L;
            }

            OUT[0] = outTemp;

            if (outTemp >= H)
            {
                HAlm[0] = true;
            }
            else if (outTemp < H - DBA)
            {
                HAlm[0] = false;
            }

            if (outTemp <= L)
            {
                LAlm[0] = true;
            }
            else if (outTemp > L + DBA)
            {
                LAlm[0] = false;
            }

            ALM[0] = HAlm | LAlm;
        }
	}
}
