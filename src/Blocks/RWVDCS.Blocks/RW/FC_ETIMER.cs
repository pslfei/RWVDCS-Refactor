using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("ETIMER")]
	[FCDisplay("过去时间计时器")]
	public partial class ETIMER : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("计时开关")]
		public LD X = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("复位开关")]
		public LD RST = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("计时设置值")]
		public LA SetV = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("计时复位值")]
		public LA RSTV = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("计时器输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("计时结束指示输出")]
		public LD END = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("计时模式")]
		public UInt32 MODE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("计时单位")]
		public UInt32 TU = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

	}
}
