using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("HDO")]
	[FCDisplay("开关量输出")]
	public partial class HDO : Function 
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
		public LD X = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);



        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD TAG = new LD(QualityTypes.Good, false, false, false, 0, false);


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
