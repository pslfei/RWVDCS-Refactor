using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("VSETR")]
	[FCDisplay("断电保持设定值")]
	public partial class VSETR : Function
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪值")]
		public LA TR = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪开关")]
		public LD TS = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输出OUT高限值")]
		public LA MaxOut = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("输出OUT低限值")]
		public LA MinOut = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("写入使能")]
		public LD EnWrt = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Constant)]
        [PinDisplay("增减步长")]
        public float Step1 = 5.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("微调增减步长")]
        public float Step2 = 1.0f;

        [PinType(PinTypes.Constant)]
		[PinDisplay("读取变量位置")]
		public UInt32 LOCAT = 1;

		[PinType(PinTypes.Constant)]
		[PinDisplay("源端页号")]
		public UInt32 PAGE = 0;

        // ==================== 输出 ====================

        [PinType(PinTypes.Output)]
        [PinDisplay("使能输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("HMI 状态打包点 (PACK)，按位映射 跟踪/启动/增/减/微增/微减 状态")]
        public LP32 TAG = new LP32();

        [PinType(PinTypes.Constant)]
		[PinDisplay("手动指令源端页号")]
		public UInt32 CXMPAGE = 0;

		[PinType(PinTypes.IO)]
		[PinDisplay("手动指令")]
		public LA CXM = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("增指令源端页号")]
		public UInt32 CUPPAGE = 0;

		[PinType(PinTypes.Input)]
		[PinDisplay("增指令")]
		public LD CUP = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("减指令源端页号")]
		public UInt32 CDNPAGE = 0;

		[PinType(PinTypes.Input)]
		[PinDisplay("减指令")]
		public LD CDN = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("微调增指令源端页号")]
		public UInt32 CFUPAGE = 0;

		[PinType(PinTypes.Input)]
		[PinDisplay("微调增指令")]
		public LD CFU = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("微调减指令源端页号")]
		public UInt32 CFDPAGE = 0;

		[PinType(PinTypes.Input)]
		[PinDisplay("微调减指令")]
		public LD CFD = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

	}
}
