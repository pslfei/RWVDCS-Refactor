using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("PID")]
	[FCDisplay("PID")]
	public partial class PID : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("偏差输入")]
		public LA E = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("高限")]
		public LA H = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("低限")]
		public LA L = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪量")]
		public LA TR = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("跟踪切换开关")]
		public LD TS = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("闭锁增开关")]
		public LD LI = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("闭锁减开关")]
		public LD LD = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Input)]
		[PinDisplay("前馈量")]
		public LA FF = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("比例放大系数")]
		public LA Kp = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("积分时间")]
		public LA Ti = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("微分时间")]
		public LA Td = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("微分器放大系数")]
		public LA Kd = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);


		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("控制输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出超高限报警")]
		public LD HAlm = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出超低限报警")]
		public LD LAlm = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("比例增量")]
		public LA DOUTp = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("积分增量")]
		public LA DOUTi = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("微分增量")]
		public LA DOUTd = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("正反作用")]
		public bool PoN = false;

		[PinType(PinTypes.Constant)]
		[PinDisplay("积分分离阈值")]
		public double EDB = 0.0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("积分器停止积分时Kp的补偿值")]
		public double Dk = 0.0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("控制输出源端页号")]
		public double SOTPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("控制输出源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string SOT = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("比例增量源端页号")]
		public double SOPPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("比例增量源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string SOP = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("积分增量源端页号")]
		public double SOIPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("积分增量源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string SOI = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("微分增量源端页号")]
		public double SODPAGE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("微分增量源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string SOD = null;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public double QualityT = 0;

		[PinType(PinTypes.Internal)]
		[PinDisplay("上一周期偏差")]
		public float prevE = 0.0f;

		[PinType(PinTypes.Internal)]
		[PinDisplay("上一周期前馈")]
		public float prevFF = 0.0f;

		[PinType(PinTypes.Internal)]
		[PinDisplay("上一周期微分输出")]
		public float prevOUTd = 0.0f;

	}
}
