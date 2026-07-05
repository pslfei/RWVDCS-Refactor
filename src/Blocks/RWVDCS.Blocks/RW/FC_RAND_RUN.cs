using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class RAND
	{
		private static Random rng = new Random();

		protected override void Run(ICommand cmd)
		{
			if (!Enable) return;

			// 产生随机数输出，范围 -1 ~ 1
			OUT[0] = (float)(rng.NextDouble() * 2.0 - 1.0);
		}
	}
}
