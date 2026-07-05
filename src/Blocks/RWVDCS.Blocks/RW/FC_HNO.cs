using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("HNO")]
	[FCDisplay("整型数模拟量输出")]
	public partial class HNO : Function 
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
		//public LP32 X = new LP32();
        public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LP32 OUT = new LP32();
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        //public LP32 TAG = new LP32();
        public LA TAG = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

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


	}
}
