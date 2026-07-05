using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class DIV
	{
		protected override void Run(ICommand cmd) 
		{
            if (k2 * X2 + b2 != 0)
			{
                OUT[0] = (k1 * X1 + b1) / (k2 * X2 + b2);
                OUT.Quality = QualityTypes.Good;
            }
			else
			{
                OUT.Quality = QualityTypes.Bad;
            }
        }
	}
}
