using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("COMIN32")]
	[FCDisplay("通讯四字节输入")]
	public partial class COMIN32 : Function
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("页号")]
		public UInt32 PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("站号")]
		public UInt32 Station = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("分支号")]
		public UInt32 Branch = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("槽位号")]
		public UInt32 Slot = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("通道号")]
		public UInt32 Channel = 0;

		[PinType(PinTypes.Input)]
		[PinDisplay("测点名")]
		public LA TAG = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("输出类型")]
		public UInt32 Type = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("字节顺序")]
		public UInt32 Mode = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("延时")]
		public double TIME = 0;

		// --- 内部状态变量 ---
		private double _badQualityElapsed = 0.0;

	}
}
