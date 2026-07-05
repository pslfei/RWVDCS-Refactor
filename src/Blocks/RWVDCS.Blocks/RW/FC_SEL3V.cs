using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("SEL3V")]
	[FCDisplay("带限速的模拟量三值优选模块")]
	public partial class SEL3V : Function
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("模块描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入1")]
		public LA X1 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入2")]
		public LA X2 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入3")]
		public LA X3 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		// ==================== HMI 指令 Input 引脚 (参照 DEVICE 模式) ====================

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI X1禁用指令 CD1    HMI 面板按下的“禁用 X1”指令脉冲信号")]
		public LD CD1 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI X2禁用指令 CD2    HMI 面板按下的“禁用 X2”指令脉冲信号")]
		public LD CD2 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI X3禁用指令 CD3    HMI 面板按下的“禁用 X3”指令脉冲信号")]
		public LD CD3 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI X1启用指令 CE1    HMI 面板按下的“启用 X1”指令脉冲信号")]
		public LD CE1 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI X2启用指令 CE2    HMI 面板按下的“启用 X2”指令脉冲信号")]
		public LD CE2 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI X3启用指令 CE3    HMI 面板按下的“启用 X3”指令脉冲信号")]
		public LD CE3 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 切平均模式 CAG    HMI 面板按下的“切 AVG 模式”指令脉冲信号")]
		public LD CAG = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 切低选模式 CMN    HMI 面板按下的“切 MIN 模式”指令脉冲信号")]
		public LD CMN = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 切高选模式 CMX    HMI 面板按下的“切 MAX 模式”指令脉冲信号")]
		public LD CMX = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 切中值模式 CMD    HMI 面板按下的“切 MIDDLE 模式”指令脉冲信号")]
		public LD CMD = new LD(QualityTypes.Good, false, false, false, 0, false);

		// ==================== 输出 ====================

		[PinType(PinTypes.Output)]
		[PinDisplay("使能输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("三值优选结果输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("偏差大报警")]
		public LD DevH = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("品质坏报警输出")]
		public LD QA = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("冗余测点报警")]
		public LD Alm = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输入1品质坏报警")]
		public LD X1Bad = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输入2品质坏报警")]
		public LD X2Bad = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输入3品质坏报警")]
		public LD X3Bad = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输入1、2偏差大报警")]
		public LD DEV12 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输入1、3偏差大报警")]
		public LD DEV13 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输入2、3偏差大报警")]
		public LD DEV23 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("HMI 状态打包点 (PACK)，按位映射 X1/X2/X3 禁用/品质/DevH/QA/MODE/偏差越限/非首次运算")]
		public LP32 TAG = new LP32();

		// ==================== 参数 ====================

		[PinType(PinTypes.Constant)]
		[PinDisplay("优选模式初始值  0=AVG平均  1=MIN低选  2=MAX高选  3=MIDDLE中值")]
		public UInt32 MODE = 0;

		[PinType(PinTypes.Constant)]
		[PinDisplay("死区")]
		public float DB = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("速率值(值/分钟)")]
		public float RATE = 0.0f;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		// ==================== Internal ====================

		[PinType(PinTypes.Internal)]
		[PinDisplay("X1禁用状态")]
		public bool x1Disabled = false;

		[PinType(PinTypes.Internal)]
		[PinDisplay("X2禁用状态")]
		public bool x2Disabled = false;

		[PinType(PinTypes.Internal)]
		[PinDisplay("X3禁用状态")]
		public bool x3Disabled = false;

		[PinType(PinTypes.Internal)]
		[PinDisplay("当前优选模式 (受 HMI CAG/CMN/CMX/CMD 切换)")]
		public UInt32 curMode = 0;

		[PinType(PinTypes.Internal)]
		[PinDisplay("速率控制目标值")]
		public float targetValue = 0.0f;

		[PinType(PinTypes.Internal)]
		[PinDisplay("非首次运行标志")]
		public bool notFirstRun = false;

		[PinType(PinTypes.Internal)]
		[PinDisplay("速率控制活动标志")]
		public bool rateActive = false;

		[PinType(PinTypes.Internal)]
		[PinDisplay("上一周期状态签名")]
		public int prevStateSignature = 0;

		[PinType(PinTypes.Internal)]
		public bool oldCD1 = false;

		[PinType(PinTypes.Internal)]
		public bool oldCD2 = false;

		[PinType(PinTypes.Internal)]
		public bool oldCD3 = false;

		[PinType(PinTypes.Internal)]
		public bool oldCE1 = false;

		[PinType(PinTypes.Internal)]
		public bool oldCE2 = false;

		[PinType(PinTypes.Internal)]
		public bool oldCE3 = false;

		[PinType(PinTypes.Internal)]
		public bool oldCAG = false;

		[PinType(PinTypes.Internal)]
		public bool oldCMN = false;

		[PinType(PinTypes.Internal)]
		public bool oldCMX = false;

		[PinType(PinTypes.Internal)]
		public bool oldCMD = false;
	}
}
