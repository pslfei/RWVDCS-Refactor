using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("TIMER")]
	[FCDisplay("定时器")]
	public partial class TIMER : Function 
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

		[PinType(PinTypes.Input)]
		[PinDisplay("输入时间设定值")]
		public LA TIME = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("复位信号")]
		public LD RST = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("信号输出")]
		public LD OUT = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("时间计数")]
		public LA TRun = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("运行模式")]
		public UInt32 MODE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

        // --- 内部状态变量 (不作为引脚暴露) ---
        private bool _prevX = false;     // 上一运算周期的 X 状态
        private bool _prevRST = false;   // 上一运算周期的 RST 状态
        private bool _timing = false;    // 内部计时激活标志
        // _cycleTime removed: scan cycle now taken from cmd.Dpu.Cycle in Run(). // 运算周期(秒)，实际应用中已替换为系统真实的扫描周期，如 cmd.DeltaTime
    }
}
