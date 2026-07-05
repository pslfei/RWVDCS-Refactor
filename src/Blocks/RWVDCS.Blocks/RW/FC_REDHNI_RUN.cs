using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class REDHNI
	{
		protected override void Run(ICommand cmd)
		{
			if (!Enable)
				return;

			// 冗余整型数模拟量输入：
			// 两个通道品质都为GOOD时，默认选取主通道值作为输出；
			// 当两个通道品质均不为GOOD时，默认选取品质后变为非GOOD的通道的值作为输出。
			bool mainGood = (TAG.Quality == QualityTypes.Good);
			bool backupGood = (TAG_R.Quality == QualityTypes.Good);

			if (mainGood)
			{
				OUT[0] = TAG;
			}
			else if (backupGood)
			{
				OUT[0] = TAG_R;
			}
		}
	}
}
