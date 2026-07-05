using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("PDI")]
	[FCDisplay("页间开关量引用")]
	public partial class PDI : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD OUT = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("源端页号")]
		public UInt32 PAGE = 0;

        [PinType(PinTypes.Input)]
        [PinDisplay("源端实例名")]
        public LD TAG = new LD(QualityTypes.Good, false, false, false, 0, false);
    }
}
