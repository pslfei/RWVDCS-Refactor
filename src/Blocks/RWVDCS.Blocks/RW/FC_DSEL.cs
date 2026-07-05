using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("DSEL")]
	[FCDisplay("开关量选择")]
	public partial class DSEL : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入1")]
		public LD X1 = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入2")]
		public LD X2 = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("选择信号输入")]
		public LD SEL = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD OUT = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

	}
}
