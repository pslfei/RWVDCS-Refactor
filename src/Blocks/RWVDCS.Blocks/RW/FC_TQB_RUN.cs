using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class TQB
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            bool qualityBad = (X.Quality != QualityTypes.Good);

            QA[0] = qualityBad;
            ComE[0] = qualityBad;
        }
    }
}
