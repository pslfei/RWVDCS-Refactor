using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("DELAY2")]
	[FCDisplay("大缓冲区滞后")]
	public partial class DELAY2 : Function
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
		[PinDisplay("跟踪值")]
		public LA TR = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪开关")]
		public LD TS = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("滞后输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
		[PinDisplay("纯滞后常数")]
		public float DT = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("比例增益")]
		public float K = 1.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("惯性常数")]
		public float LT = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		// 内部缓冲区，长度120，存储历史输入值
		[PinType(PinTypes.Internal)]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 120)]
		public float[] buffer = new float[120];

		[PinType(PinTypes.Internal)]
		public int bufIndex = 0;

		[PinType(PinTypes.Internal)]
		public bool bufFilled = false;

	}
}
