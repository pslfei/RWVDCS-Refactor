using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("DNODE")]
	[FCDisplay("IO节点诊断")]
	public partial class DNODE : Function 
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
		[PinDisplay("IO节点总故障")]
		public LD NTErr = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("分支总故障")]
		public LA BTErr = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("1#分支故障")]
		public LA BR1Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("2#分支故障")]
		public LA BR2Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("3#分支故障")]
		public LA BR3Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("4#分支故障")]
		public LA BR4Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("5#分支故障")]
		public LA BR5Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("6#分支故障")]
		public LA BR6Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("站号")]
		public UInt32 Station = 1;

		[PinType(PinTypes.Constant)]
		[PinDisplay("源端页号")]
		public UInt32 PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string TAG = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("IO节点总故障源端页号")]
		public UInt32 NTEPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("IO节点总故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string NTE = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("分支总故障源端页号")]
		public UInt32 BTEPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("分支总故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string BTE = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("1#分支故障源端页号")]
		public UInt32 BR1PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("1#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string BR1 = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("2#分支故障源端页号")]
		public UInt32 BR2PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("2#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string BR2 = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("3#分支故障源端页号")]
		public UInt32 BR3PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("3#分支故障源端测点名")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string BR3 = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("4#分支故障源端页号")]
		public UInt32 BR4PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("4#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string BR4 = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("5#分支故障源端页号")]
		public UInt32 BR5PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("5#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string BR5 = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("6#分支故障源端页号")]
		public UInt32 BR6PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("6#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string BR6 = "";

		[PinType(PinTypes.Constant)]
		[PinDisplay("站号源端页号")]
		public UInt32 STNPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("站号源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string STN = "";

	}
}
