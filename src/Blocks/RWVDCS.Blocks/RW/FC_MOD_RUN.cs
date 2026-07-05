using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class MOD
	{
		protected override void Run(ICommand cmd) 
		{
			if(X2 == 0)
			{
                //OUT保持上一时刻值不变， 模块输出OUT品质属性为坏点
                OUT.Quality = QualityTypes.Bad;
            }
            else
            {
                OUT[0] = X1 % X2;
                OUT.Quality = QualityTypes.Good;
            }
        }
	}
}
