using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("FX")]
	[FCDisplay("12段函数变换")]
	public partial class FX : Function 
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

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("第1个坐标点横坐标")]
		public float X1 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第1个坐标点纵坐标")]
		public float Y1 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第2个坐标点横坐标")]
		public float X2 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第2个坐标点纵坐标")]
		public float Y2 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第3个坐标点横坐标")]
		public float X3 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第3个坐标点纵坐标")]
		public float Y3 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第4个坐标点横坐标")]
		public float X4 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第4个坐标点纵坐标")]
		public float Y4 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第5个坐标点横坐标")]
		public float X5 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第5个坐标点纵坐标")]
		public float Y5 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第6个坐标点横坐标")]
		public float X6 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第6个坐标点纵坐标")]
		public float Y6 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第7个坐标点横坐标")]
		public float X7 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第7个坐标点纵坐标")]
		public float Y7 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第8个坐标点横坐标")]
		public float X8 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第8个坐标点纵坐标")]
		public float Y8 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第9个坐标点横坐标")]
		public float X9 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第9个坐标点纵坐标")]
		public float Y9 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第10个坐标点横坐标")]
		public float X10 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第10个坐标点纵坐标")]
		public float Y10 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第11个坐标点横坐标")]
		public float X11 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第11个坐标点纵坐标")]
		public float Y11 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第12个坐标点横坐标")]
		public float X12 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("第12个坐标点纵坐标")]
		public float Y12 = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;


		[PinType(PinTypes.Internal)]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 12)]
        float[] _x = new float[12];

        [PinType(PinTypes.Internal)]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 12)]
        float[] _y = new float[12];

    }
}
