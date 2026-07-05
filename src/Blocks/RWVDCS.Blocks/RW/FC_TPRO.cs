using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("TPRO")]
	[FCDisplay("温度保护")]
	public partial class TPRO : Function
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

		[PinType(PinTypes.Input)]
		[PinDisplay("温度测点")]
		public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("温度高报警值值")]
		public LA AlmV = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 70.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("温度保护值")]
		public LA ProV = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("飞升速率限制值")]
		public LA RatV = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 600.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("复位温度保护切除状态")]
		public LD RST = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("温度保护动作")]
		public LD PAct = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("温度高报警")]
		public LD HAlm = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("温度飞升速率高报警")]
		public LD RAlm = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("温度测点坏质量")]
		public LD BAD = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("温度测点综合故障报警")]
		public LD TRL = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("温度保护切除状态")]
		public LD Cut = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("RST输入使能")]
		public bool EnR = false;

		[PinType(PinTypes.Constant)]
		[PinDisplay("温度高死区")]
		public float DB1 = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("温度保护重新投入死区")]
		public float DB2 = 5;

		[PinType(PinTypes.Constant)]
		[PinDisplay("温度保护延时")]
		public float TIME = 3;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		[PinType(PinTypes.Internal)]
		public float oldX = 0;

		[PinType(PinTypes.Internal)]
		public float timerAcc = 0;

	}
}
