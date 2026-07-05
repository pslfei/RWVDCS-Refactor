using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("VSET")]
	[FCDisplay("设定值")]
	public partial class VSET : Function
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪值")]
		public LA TR = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪开关")]
		public LD TS = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输出 OUT 高限值")]
		public LA MaxOut = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("输出 OUT 低限值")]
		public LA MinOut = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		// ==================== HMI 指令 Input 引脚 (参照 DEVICE 模式) ====================

		[PinType(PinTypes.IO)]
		[PinDisplay("HMI 设定值直输 CXM    HMI 中央数值控件直写当前设定值")]
		public LA CXM = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 增指令 CUP        HMI 面板按下的“增”指令脉冲信号")]
		public LD CUP = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 减指令 CDN        HMI 面板按下的“减”指令脉冲信号")]
		public LD CDN = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 微增指令 CFU      HMI 面板按下的“微增”指令脉冲信号")]
		public LD CFU = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 微减指令 CFD      HMI 面板按下的“微减”指令脉冲信号")]
		public LD CFD = new LD(QualityTypes.Good, false, false, false, 0, false);

		// ==================== 输出 ====================

		[PinType(PinTypes.Output)]
		[PinDisplay("使能输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("HMI 状态打包点 (PACK)，按位映射 跟踪/启动/增/减/微增/微减 状态")]
		public LP32 TAG = new LP32();

		// ==================== 参数 ====================

		[PinType(PinTypes.Constant)]
		[PinDisplay("增减步长")]
		public float Step1 = 5.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("微调增减步长")]
		public float Step2 = 1.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		// ==================== Internal ====================

		[PinType(PinTypes.Internal)]
		public bool firstRun = true;

		[PinType(PinTypes.Internal)]
		public bool oldCUP = false;

		[PinType(PinTypes.Internal)]
		public bool oldCDN = false;

		[PinType(PinTypes.Internal)]
		public bool oldCFU = false;

		[PinType(PinTypes.Internal)]
		public bool oldCFD = false;

		[PinType(PinTypes.Internal)]
		public float oldCXM = 0.0f;

		[PinType(PinTypes.Internal)]
		public bool cxmInitialized = false;
	}
}
