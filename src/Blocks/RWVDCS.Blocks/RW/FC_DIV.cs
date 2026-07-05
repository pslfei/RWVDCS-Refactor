using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("DIV")]
	[FCDisplay("除法")]
	public partial class DIV : Function 
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
		public LA X1 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入2")]
		public LA X2 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出商")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("X1增益")]
		public float k1 = 1.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("X1偏置")]
		public float b1 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("X2增益")]
		public float k2 = 1.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("X2偏置")]
		public float b2 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

	}
}
