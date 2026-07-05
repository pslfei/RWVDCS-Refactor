using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("RMP")]
	[FCDisplay("斜坡信号发生器")]
	public partial class RMP : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("起始值")]
		public LA BASE = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("终点值")]
		public LA END = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

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
		[PinDisplay("输出值到达终点值指示")]
		public LD OUTD = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("变化率（单位：/s）")]
		public float DY = 1.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		[PinType(PinTypes.Internal)]
		[PinDisplay("上一次复位指令值")]
        public bool OLD_RST = false;


    }
}
