using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("DDPU")]
	[FCDisplay("节点诊断")]
	public partial class DDPU : Function 
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
		[PinDisplay("左侧DPU       CPU负荷(%)")]
		public LA LCLoad = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("左侧DPU内存使用率(%)")]
		public LA LMLoad = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("左侧DPU主控状态")]
		public LD LMain = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("左侧DPU异常状态")]
		public LA LState = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("左侧DPU       IO故障")]
		public LA LIOErr = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("左侧DPU软件版本")]
		public LA LSVer = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("左侧DPU启动时间")]
		public LA LStartT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("右侧DPU       CPU负荷(%)")]
		public LA RCLoad = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("右侧DPU内存使用率(%)")]
		public LA RMLoad = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("右侧DPU主控状态")]
		public LD RMain = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("右侧DPU异常状态")]
		public LA RState = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("右侧DPU       IO故障")]
		public LA RIOErr = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("右侧DPU       软件版本")]
		public LA RSVer = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("右侧DPU启动时间")]
		public LA RStartT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("双机同步状态")]
		public LD Sync = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("连接用户IP")]
		public LA UserIP = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("算法强制")]
		public LA Forced = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("左侧DPU温度（℃）")]
		public LA LTemp = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("右侧DPU温度（℃）")]
		public LA RTemp = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("本地分支总故障")]
		public LA BTErr = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("本地1#分支故障")]
		public LA BR1Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("本地2#分支故障")]
		public LA BR2Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("本地3#分支故障")]
		public LA BR3Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("本地4#分支故障")]
		public LA BR4Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("本地5#分支故障")]
		public LA BR5Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("本地6#分支故障")]
		public LA BR6Err = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("源端测点名")]
        public LA TAG = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器CPU负荷源端页号")]
		public UInt32 LCLPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器CPU负荷源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LCL = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器内存占用率源端页号")]
		public UInt32 LMLPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器内存占用率源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LML = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器主控状态源端页号")]
		public UInt32 LMNPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器主控状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LMN = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器异常状态源端页号")]
		public UInt32 LSTPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器异常状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LST = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器IO故障状态源端页号")]
		public UInt32 LIEPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器IO故障状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LIE = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器软件版本源端页号")]
		public UInt32 LSVPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器软件版本源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LSV = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器启动时间源端页号")]
		public UInt32 LSMPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器启动时间源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LSM = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器CPU负荷源端页号")]
		public UInt32 RCLPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器CPU负荷源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RCL = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器内存占用率源端页号")]
		public UInt32 RMLPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器内存占用率源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RML = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器主控状态源端页号")]
		public UInt32 RMNPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器主控状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RMN = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器异常状态源端页号")]
		public UInt32 RSTPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器异常状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RST = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器IO故障状态源端页号")]
		public UInt32 RIEPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器IO故障状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RIE = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器软件版本源端页号")]
		public UInt32 RSVPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器软件版本源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RSV = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器启动时间源端页号")]
		public UInt32 RSMPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器启动时间源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RSM = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("双机同步状态源端页号")]
		public UInt32 SNCPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("双机同步状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string SNC = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("用户IP源端页号")]
		public UInt32 UIPPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("用户IP源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string UIP = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("强制状态源端页号")]
		public UInt32 FRCPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("强制状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string FRC = null;
			
		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器温度源端页号")]
		public UInt32 LTMPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("左侧控制器温度源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LTM = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器温度源端页号")]
		public UInt32 RTMPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("右侧控制器温度源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RTM = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地分支总故障源端页号")]
		public UInt32 DBTPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地分支总故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string DBT = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地1#分支故障源端页号")]
		public UInt32 DB1PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地1#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string DB1 = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地2#分支故障源端页号")]
		public UInt32 DB2PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地2#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string DB2 = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地3#分支故障源端页号")]
		public UInt32 DB3PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地3#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string DB3 = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地4#分支故障源端页号")]
		public UInt32 DB4PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地4#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string DB4 = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地5#分支故障源端页号")]
		public UInt32 DB5PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地5#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string DB5 = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地6#分支故障源端页号")]
		public UInt32 DB6PAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("本地6#分支故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string DB6 = null;

	}
}
