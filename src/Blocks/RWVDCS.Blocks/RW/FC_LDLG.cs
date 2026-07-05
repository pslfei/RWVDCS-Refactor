using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("LDLG")]
	[FCDisplay("超前滞后")]
	public partial class LDLG : Function 
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

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪值")]
		public LA TR = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪开关")]
		public LD TS = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("超前时间常数")]
		public LA LD = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("滞后时间常数")]
		public LA LG = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("超前滞后输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("高限")]
		public float H = 9999.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("低限")]
		public float L = -9999.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		[PinType(PinTypes.Internal)]
		[PinDisplay("上一次输入")]
		public float OLD_X = 0;

		[PinType(PinTypes.Internal)]
		[PinDisplay("上一次输出")]
		public float OLD_OUT = 0;
    }
}
