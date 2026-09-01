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

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI合闸指令脉冲")]
        public LD CON = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI分闸指令脉冲")]
        public LD COF = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI投自动指令脉冲")]
        public LD CTA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI切手动指令脉冲")]
        public LD CTM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI故障确认指令脉冲")]
        public LD CAK = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI禁操翻转指令脉冲")]
        public LD CFB = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI复位指令脉冲")]
        public LD CRS = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI调试翻转指令脉冲")]
        public LD CDB = new LD(QualityTypes.Good, false, false, false, 0, false);

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
        public LP32 TAG = new LP32();

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

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public UInt32 QualityT = 0;

        // ==================== Internal (算法内部状态与 HMI 接口) ====================
        [PinType(PinTypes.Internal)]
        public bool firstRun = true;

        [PinType(PinTypes.Internal)]
        public bool oldFBOn = false;
        [PinType(PinTypes.Internal)]
        public bool oldFBOff = false;

        [PinType(PinTypes.Internal)]
        public bool oldCON = false;
        [PinType(PinTypes.Internal)]
        public bool oldCOF = false;
        [PinType(PinTypes.Internal)]
        public bool oldCTA = false;
        [PinType(PinTypes.Internal)]
        public bool oldCTM = false;
        [PinType(PinTypes.Internal)]
        public bool oldCAK = false;
        [PinType(PinTypes.Internal)]
        public bool oldCFB = false;
        [PinType(PinTypes.Internal)]
        public bool oldCRS = false;
        [PinType(PinTypes.Internal)]
        public bool oldCDB = false;

        [PinType(PinTypes.Internal)]
        public bool onCmdActive = false;
        [PinType(PinTypes.Internal)]
        public bool offCmdActive = false;

        [PinType(PinTypes.Internal)]
        public bool onPulseActive = false;
        [PinType(PinTypes.Internal)]
        public bool offPulseActive = false;

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
    }
}
