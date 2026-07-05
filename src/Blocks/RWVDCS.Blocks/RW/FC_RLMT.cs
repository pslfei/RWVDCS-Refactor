using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("RLMT")]
	[FCDisplay("速率限制")]
	public partial class RLMT : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入")]
		public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("增速率限值输入")]
		public LA IRL = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("减速率限值输入")]
		public LA DRL = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

	}
}
