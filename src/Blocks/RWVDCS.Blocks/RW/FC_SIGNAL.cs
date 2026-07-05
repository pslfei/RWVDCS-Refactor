using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("SIGNAL")]
	[FCDisplay("信号发生器")]
	public partial class SIGNAL : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("暂停指令")]
		public LD PAUSE = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("复位指令")]
		public LD RST = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("信号输出值")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("周期信号，单脉冲")]
		public LD OUTD = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("信号类型")]
		public UInt32 MODE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("信号幅值")]
		public float AMP = 1.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("信号周期")]
		public float T = 10.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("零位偏置")]
		public float BIAS = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		[PinType(PinTypes.Internal)]
		[PinDisplay("周期内累积时间")]
		public float elapsed = 0.0f;

	}
}
