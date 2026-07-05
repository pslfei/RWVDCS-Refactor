using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("REDHNI")]
	[FCDisplay("冗余整型数模拟量输入")]
	public partial class REDHNI : Function
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
		[PinDisplay("备用通道页号")]
		public UInt32 PAGE_R = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("备用通道站号")]
		public UInt32 Station_R = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("备用通道分支号")]
		public UInt32 Branch_R = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("备用通道槽位号")]
		public UInt32 Slot_R = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("备用通道通道号")]
		public UInt32 Channel_R = 0;

		[PinType(PinTypes.Input)]
		[PinDisplay("备用通道测点名")]
		public LA TAG_R = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("延时")]
		public double TIME = 0;

	}
}
