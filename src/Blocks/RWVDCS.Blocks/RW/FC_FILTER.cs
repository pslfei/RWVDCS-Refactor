using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("FILTER")]
	[FCDisplay("数字滤波")]
	public partial class FILTER : Function 
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
		[PinDisplay("滤波运算输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("滤波器系数1")]
		public float K1 = 0.1f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("滤波器系数2")]
		public float K2 = 0.1f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("滤波器系数3")]
		public float K3 = 0.1f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("滤波器系数4")]
		public float K4 = 0.1f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("滤波器系数5")]
		public float K5 = 0.1f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("滤波器系数6")]
		public float K6 = 0.1f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("滤波器系数7")]
		public float K7 = 0.1f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("滤波器系数8")]
		public float K8 = 0.1f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

        [PinType(PinTypes.Internal)]
        [PinDisplay("存储最近的8个输入值，包括当前值")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 8)]
        public float[] buffer = new float[8];

    }
}
