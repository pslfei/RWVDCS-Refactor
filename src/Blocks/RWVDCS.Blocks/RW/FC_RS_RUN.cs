using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;
using System.Collections.Generic;

namespace RWVDCS.Blocks.RW
{
    public partial class RS
    {
        protected override void Run(ICommand cmd)
        {
            bool nextQ = (!R) & (S | Q);
            Q[0] = nextQ;
            QN[0] = !nextQ;
        }
    }
}
