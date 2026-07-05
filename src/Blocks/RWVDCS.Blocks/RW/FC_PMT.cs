using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("PMT")]
	[FCDisplay("启停允许")]
	public partial class PMT : Function 
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
		public LD X1 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入2")]
		public LD X2 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入3")]
		public LD X3 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入4")]
		public LD X4 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入5")]
		public LD X5 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入6")]
		public LD X6 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入7")]
		public LD X7 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入8")]
		public LD X8 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入9")]
		public LD X9 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入10")]
		public LD X10 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入11")]
		public LD X11 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入12")]
		public LD X12 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入13")]
		public LD X13 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入14")]
		public LD X14 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入15")]
		public LD X15 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入16")]
		public LD X16 = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD OUT = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("打包点")]
		public LP32 PACK = new LP32();

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

	}
}
