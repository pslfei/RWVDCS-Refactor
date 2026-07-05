using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("SAIP")]
	[FCDisplay("慢信号保护模块")]
	public partial class SAIP : Function
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入")]
		public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("确认信号输入")]
		public LD ACK = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("报警输出")]
		public LD ALM = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("保护动作输出")]
		public LD PAct = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("增速率限值")]
		public float IRL = 100;

		[PinType(PinTypes.Constant)]
		[PinDisplay("减速率限值")]
		public float DRL = 100;

		[PinType(PinTypes.Constant)]
		[PinDisplay("输入X高限")]
		public float H = 100;

		[PinType(PinTypes.Constant)]
		[PinDisplay("输入X低限")]
		public float L = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("输入X高高限")]
		public float HH = 110;

		[PinType(PinTypes.Constant)]
		[PinDisplay("输入X低低限")]
		public float LL = -10;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		[PinType(PinTypes.Internal)]
		public float oldX = 0;

	}
}
