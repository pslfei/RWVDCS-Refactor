using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;
using System.Diagnostics;
using static System.Net.WebRequestMethods;
namespace RWVDCS.Blocks.RW
{
    public partial class ABS
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            OUT[0] = Math.Abs(k * X + b);

            TAG.Value = 1;
        }
    }
}
