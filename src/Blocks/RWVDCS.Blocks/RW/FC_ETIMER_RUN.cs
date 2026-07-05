using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class ETIMER
	{
		protected override void Run(ICommand cmd) 
		{
            _ENO[0] = Enable;
            if (!Enable)
            {
                return;
            }

            if (RST)
            {
                OUT[0] = RSTV;
                END[0] = false;
                return;
            }

            float delta = cmd.Dpu.Cycle;

         
            if (TU == 1)
                delta = cmd.Dpu.Cycle / 60.0f;
            else if (TU == 2)
                delta = cmd.Dpu.Cycle / 3600.0f;
            else if (TU == 3)
                delta = cmd.Dpu.Cycle / 86400.0f;

            if (MODE == 0)
            {
                if (X & OUT < SetV)
                {
                    OUT[0] = OUT + delta;
                }

                if (OUT >= SetV)
                {
                    OUT[0] = SetV;
                    END[0] = true;
                }
                else
                {
                    END[0] = false;
                }
            }
            else if (MODE == 1)
            {
                if (X & OUT > SetV)
                {
                    OUT[0] = OUT - delta;
                }

                if (OUT <= SetV)
                {
                    OUT[0] = SetV;
                    END[0] = true;
                }
                else
                {
                    END[0] = false;
                }
            }
        }
	}
}
