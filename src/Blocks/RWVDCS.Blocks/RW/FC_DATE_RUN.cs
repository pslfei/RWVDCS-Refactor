using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;
namespace RWVDCS.Blocks.RW
{
    public partial class DATE
    {


        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            Year[0] = DateTime.Now.Year;
            Mon[0] = DateTime.Now.Month;
            Day[0] = DateTime.Now.Day;
            Hour[0] = DateTime.Now.Hour;
            Min[0] = DateTime.Now.Minute;
            Sec[0] = DateTime.Now.Second;
            MSec[0] = DateTime.Now.Millisecond;
            Week[0] = DateTime.Now.DayOfWeek;
        }
    }
}
