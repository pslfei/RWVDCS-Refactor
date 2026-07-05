using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class SIGNAL
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            float cycle = cmd.Dpu.Cycle;

            bool periodStart = false;

            if (RST)
            {
                elapsed = 0.0f;
            }
            else if (!PAUSE)
            {
                elapsed += cycle;
                if (T > 0.0f && elapsed >= T)
                {
                    elapsed -= T;
                    periodStart = true;
                }
            }

            OUTD[0] = periodStart;

            if (T <= 0.0f)
            {
                OUT[0] = BIAS;
                return;
            }

            float phase = elapsed / T;

            if (MODE == 0)
            {
                OUT[0] = (phase < 0.5f ? AMP : -AMP) + BIAS;
            }
            else if (MODE == 1)
            {
                float value;
                if (phase < 0.5f)
                    value = AMP * (4.0f * phase - 1.0f);
                else
                    value = AMP * (3.0f - 4.0f * phase);
                OUT[0] = value + BIAS;
            }
            else if (MODE == 2)
            {
                OUT[0] = AMP * Math.Sin(2.0 * Math.PI * phase) + BIAS;
            }
            else if (MODE == 3)
            {
                OUT[0] = AMP * Math.Cos(2.0 * Math.PI * phase) + BIAS;
            }
        }
    }
}
