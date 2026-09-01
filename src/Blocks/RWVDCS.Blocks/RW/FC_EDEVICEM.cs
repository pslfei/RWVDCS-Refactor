using System;
using System.Runtime.InteropServices;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    [FCName("EDEVICEM")]
    [FCDisplay("带手自动电气设备手操器")]
    public partial class EDEVICEM : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("算法块描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string Description = "";

        // ==================== Input ====================
        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("手动及自动合闸指令允许")]
        public LD EnOn = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("手动及自动分闸指令允许")]
        public LD EnOff = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("切手动指令")]
        public LD ToM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("自动请求指令")]
        public LD ReqA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("自动合闸指令")]
        public LD AOn = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("自动分闸指令")]
        public LD AOff = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("合闸反馈")]
        public LD FBOn = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("分闸反馈")]
        public LD FBOff = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("设备就地状态")]
        public LD Loc = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("控制电源失去状态")]
        public LD FBat = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("设备故障状态")]
        public LD FDev = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("工作位置")]
        public LD POpe = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("弹簧未储能")]
        public LD FSpr = new LD(QualityTypes.Good, false, false, false, 0, false);

        // ==================== Output ====================
        [PinType(PinTypes.Output)]
        [PinDisplay("合闸指令输出")]
        public LD On = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("分闸指令输出")]
        public LD Off = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("手动状态指示")]
        public LD MA = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出指令闭锁指示")]
        public LD NoCon = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("设备故障状态指示")]
        public LD FBFl = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("跳闸状态指示")]
        public LD Trip = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("操作失败指示")]
        public LD OpFl = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("操作禁止指示")]
        public LD Forbid = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("合闸操作失败")]
        public LD OpFlOn = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("分闸操作失败")]
        public LD OpFlOff = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("HMI状态打包点")]
        public LA TAG = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        // ==================== Parameter ====================
        [PinType(PinTypes.Constant)]
        [PinDisplay("复位信号模式")]
        public UInt32 ResetM = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("输出脉冲长度(秒)")]
        public double SetT = 3.0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("故障闭锁定义")]
        public bool FLB = false;

        [PinType(PinTypes.Constant)]
        [PinDisplay("设备行程(秒)")]
        public double Tover = 5.0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("就地禁操使能")]
        public bool EnLoc = true;

        [PinType(PinTypes.Constant)]
        [PinDisplay("控制电源失去禁操使能")]
        public bool EnFBat = true;

        [PinType(PinTypes.Constant)]
        [PinDisplay("设备故障信号禁操使能")]
        public bool EnFDev = true;

        [PinType(PinTypes.Constant)]
        [PinDisplay("弹簧未储能信号禁合使能")]
        public bool EnFSpr = true;

        [PinType(PinTypes.Constant)]
        [PinDisplay("手动优先级")]
        public UInt32 MP = 0;

        // 页号与源端名定义 (与手册保持一致，隐藏)
        // ... (保持您原有的 CONPAGE, CON 等定义不变)

        // ==================== Internal (算法内部状态与 HMI 接口) ====================
        [PinType(PinTypes.Internal)]
        public bool firstRun = true;

        [PinType(PinTypes.Internal)]
        public bool oldFBOn = false;
        [PinType(PinTypes.Internal)]
        public bool oldFBOff = false;

        [PinType(PinTypes.Internal)]
        public bool onCmdActive = false;
        [PinType(PinTypes.Internal)]
        public bool offCmdActive = false;

        [PinType(PinTypes.Internal)]
        public double onTimer = 0.0;
        [PinType(PinTypes.Internal)]
        public double offTimer = 0.0;
        [PinType(PinTypes.Internal)]
        public double onToverTimer = 0.0;
        [PinType(PinTypes.Internal)]
        public double offToverTimer = 0.0;

        [PinType(PinTypes.Internal)]
        public bool manualForbid = false;
        [PinType(PinTypes.Internal)]
        public bool debugMode = false;

        // --- HMI 操作接口 (由上位机直接置位 true) ---
        [PinType(PinTypes.Internal)]
        public bool hmiCmdOn = false;     // 上位机点击“合闸”
        [PinType(PinTypes.Internal)]
        public bool hmiCmdOff = false;    // 上位机点击“分闸”
        [PinType(PinTypes.Internal)]
        public bool hmiCmdAuto = false;   // 上位机点击“投自动”
        [PinType(PinTypes.Internal)]
        public bool hmiCmdManual = false; // 上位机点击“切手动”
        [PinType(PinTypes.Internal)]
        public bool hmiCmdAck = false;    // 上位机点击“报警确认”
        [PinType(PinTypes.Internal)]
        public bool hmiCmdReset = false;  // 上位机点击“复位”
        [PinType(PinTypes.Internal)]
        public bool hmiCmdForbid = false; // 上位机点击“禁操” (Toggle)
        [PinType(PinTypes.Internal)]
        public bool hmiCmdDebug = false;  // 上位机点击“调试” (Toggle)
    }
}
