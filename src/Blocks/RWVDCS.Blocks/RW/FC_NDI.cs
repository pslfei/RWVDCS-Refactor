using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("NDI")]
	[FCDisplay("控制器间开关量引用")]
	public partial class NDI : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD OUT = new LD(QualityTypes.Good, false, false, false,0,false);


		[PinType(PinTypes.Input)]
		[PinDisplay("输入")]
		public LD TAG = new LD(QualityTypes.Good, false, false, false,0,false);


		[PinType(PinTypes.Output)]
		[PinDisplay("品质报警输出")]
		public LD QA = new LD(QualityTypes.Good, false, false, false,0, false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("源端控制器号")]
		public UInt32 DPU = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("源端页号")]
		public UInt32 PAGE = 0;
	}
}
