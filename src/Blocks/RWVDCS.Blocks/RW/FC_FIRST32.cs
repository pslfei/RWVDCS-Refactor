using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("FIRST32")]
	[FCDisplay("32位首出")]
	public partial class FIRST32 : Function
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入1")]
		public LD X1 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入2")]
		public LD X2 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入3")]
		public LD X3 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入4")]
		public LD X4 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入5")]
		public LD X5 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入6")]
		public LD X6 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入7")]
		public LD X7 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入8")]
		public LD X8 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入9")]
		public LD X9 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入10")]
		public LD X10 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入11")]
		public LD X11 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入12")]
		public LD X12 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入13")]
		public LD X13 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入14")]
		public LD X14 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入15")]
		public LD X15 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入16")]
		public LD X16 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入17")]
		public LD X17 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入18")]
		public LD X18 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入19")]
		public LD X19 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入20")]
		public LD X20 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入21")]
		public LD X21 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入22")]
		public LD X22 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入23")]
		public LD X23 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入24")]
		public LD X24 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入25")]
		public LD X25 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入26")]
		public LD X26 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入27")]
		public LD X27 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入28")]
		public LD X28 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入29")]
		public LD X29 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入30")]
		public LD X30 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入31")]
		public LD X31 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入32")]
		public LD X32 = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("复位信号 (硬引脚)")]
		public LD RST = new LD(QualityTypes.Good, false, false, false, 0, false);

		// ==================== HMI 指令 Input 引脚 (参照 DEVICE 模式) ====================

		[PinType(PinTypes.Input)]
		[PinDisplay("HMI 复位指令 CRS      HMI 面板按下的“复位”指令脉冲信号")]
		public LD CRS = new LD(QualityTypes.Good, false, false, false, 0, false);

		// ==================== 输出 ====================

		[PinType(PinTypes.Output)]
		[PinDisplay("使能输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD OUT = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("首出输入序号")]
		public LA FNo = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("首出输入序号 HMI 显示通道 (与 FNo 同值，专供 @SFN 子测点反向同步用)")]
		public LA SFN = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("输入取真输出")]
		public LD QOut = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Output)]
		[PinDisplay("HMI 状态打包点1 (PACK1)，Bit0~15=X1~16, Bit16=RST, Bit17=OUT, Bit18=QOut")]
		public LP32 TAG = new LP32();

		[PinType(PinTypes.Output)]
		[PinDisplay("HMI 状态打包点2 (PACK2)，Bit16~Bit31=X17~X32 (按规范从 Bit16 起)")]
		public LP32 PK2 = new LP32();

		// ==================== 参数 ====================

		[PinType(PinTypes.Constant)]
		[PinDisplay("与运算的输入个数")]
		public UInt32 NUM = 1;

		[PinType(PinTypes.Constant)]
		[PinDisplay("品质传递")]
		public UInt32 QualityT = 0;

		// ==================== Internal ====================

		[PinType(PinTypes.Internal)]
		public bool oldCRS = false;
	}
}
