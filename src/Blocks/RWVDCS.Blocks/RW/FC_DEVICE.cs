using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("DEVICE")]
    [FCDisplay("设备手操器")]
    public partial class DEVICE : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("算法块的描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("保护开指令      只要该保护指令为1时，无论设备处于手动或自动模式，允许条件是否满足，都将输出指令On，闭锁与其相反的其它指令；当该指令保持为1时，自动及手动的反方向指令无效。")]
        public LD POn = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("保护关指令      将输出指令Off，其它同POn。")]
        public LD POff = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("手动及自动开指令允许      该允许信号为1时，手动及自动的开指令On才有效")]
        public LD EnOn = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("手动及自动关指令允许      该允许信号为1时，手动及自动的关指令Off才有效")]
        public LD EnOff = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("手动及自动暂停指令允许      该允许信号为1时，手动及自动的停指令Stop才有效")]
        public LD EnStp = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("切手动指令      当该信号为1时，且无就地、条件时，功能块为手动方式")]
        public LD ToM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("自动请求指令      当该信号为1时并且切手动信号为0时，功能块切为自动方式")]
        public LD ReqA = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("当设备处于自动状态，并满足对应的允许条件时，该信号将触发相对应的输出On。      注：在故障ACK后，如处于自动状态，将继续自动输出。")]
        public LD AOn = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("当设备处于自动状态，并满足对应的允许条件时，该信号将触发相对应的输出Off。      注：在故障ACK后，如处于自动状态，将继续自动输出。")]
        public LD AOff = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("当设备处于自动状态，并满足对应的允许条件时，该信号将触发相对应的输出Stop。      注：在故障ACK后，如处于自动状态，将继续自动输出。")]
        public LD AStp = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("与输出On相对应的设备打开状态反馈信号")]
        public LD FBOn = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("与输出Off相对应的设备关闭状态反馈信号")]
        public LD FBOff = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("与输出Stop相对应的设备暂停状态反馈信号")]
        public LD FBStp = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("设备切就地状态      若此信号为1，则功能块所有输出被禁止，所有输入无效；此信号具有最高优先级。")]
        public LD Loc = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("控制电源失去状态      若此信号为1，则功能块所有输出被禁止，所有输入无效；此信号优先级与LOC相同。")]
        public LD FBat = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("设备故障状态      若此信号为1，则功能块所有输出被禁止，所有输入无效；此信号优先级与LOC相同。")]
        public LD FDev = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("控操作指令 CON      HMI 上位机送下的【开】指令脉冲信号")]
        public LP32 TAG = new LP32();

        [PinType(PinTypes.Input)]
        [PinDisplay("控操作指令 CON      HMI 上位机送下的【开】指令脉冲信号")]
        public LD CON = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("控操作指令 COF      HMI 上位机送下的【关】指令脉冲信号")]
        public LD COF = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("控操作指令 CSP      HMI 上位机送下的【停】指令脉冲信号")]
        public LD CSP = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("模式切换 CTA      HMI 上位机【投自动】指令脉冲信号")]
        public LD CTA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("模式切换 CTM      HMI 上位机【切手动】指令脉冲信号")]
        public LD CTM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("故障确认 CAK      HMI 上位机送下的【确认】指令脉冲信号，清除 Trip / OpFl")]
        public LD CAK = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("禁操翻转 CFB      HMI 上位机送下的【禁操】指令脉冲信号，翻转 manualForbid")]
        public LD CFB = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("故障复位 CRS      HMI 上位机送下的【复位】指令脉冲信号，清除 Trip/OpFl + cmdActive + Timers")]
        public LD CRS = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("调试翻转 CDB      HMI 上位机送下的【调试】指令脉冲信号，翻转 Debug 标志")]
        public LD CDB = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("开指令输出")]
        public LD On = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("关指令输出")]
        public LD Off = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("暂停指令输出")]
        public LD Stp = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Output)]
        [PinDisplay("手动状态指示，手动状态为1，自动状态为0")]
        public LD MA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出指令闭锁指示      FLB=0，当OpFl、Trip、Forbid任一为1时置1，否则为0。      FLB=1，当Forbid为1时置1，否则为0。")]
        public LD NoCon = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("反馈异常状态指示，当FBOn、FBOff同时为1时置1，否则为0。")]
        public LD FBFl = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("跳闸状态指示      当无任何指令发出且设备并不处于就地，而指定的设备运行状态反馈信号却发生变化时，该信号置1，此时保护、自动及手动操作均被禁止；执行功能块确认Ack指令后，复位为0。")]
        public LD Trip = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("操作失败指示      当输出指令（脉宽由参数SetT定义）发出后，延时一段时间（设备行程时间，由参数Tover定义），仍未收到对应的反馈信号，即认为操作失败，该信号置1。此时保护、自动及手动操作均被禁止；执行功能块确认Ack指令后，复位为0。")]
        public LD OpFl = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("操作禁止指示      可由输入Loc/FBAT/FDev，及操作指令Forbid触发")]
        public LD Forbid = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("开操作失败，对应On")]
        public LD OpFlOn = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("关操作失败，对应Off")]
        public LD OpFlOff = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("暂停操作失败，对应Stop")]
        public LD OpFlStp = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("保护跳闸或异常跳闸")]
        public LD Totp = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("调试状态指示")]
        public LD Debug = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("总故障状态指示")]
        public LD TRBL = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("输出信号模式，该参数定义了输出On,       Off信号的形式。      0—输出指令为定长单脉冲,当相应反馈为真时或其它指令信号有效时，信号Reset；      1—输出指令为长信号，当相应反馈为真时或其它指令信号有效时，信号Reset。")]
        public bool OutM = false;

        [PinType(PinTypes.Constant)]
        [PinDisplay("复位信号模式             0-允许反馈信号或操作失败复位输出信号；      1-允许反馈信号复位输出信号，不允许操作失败复位输出信号；      2-不允许反馈信号或操作失败复位输出信号。")]
        public UInt32 ResetM = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("输出On,Off,Stop信号的有效长度，单位：秒")]
        public double SetT = 5.0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("输出指令优先级设定  0=开指令优先  1=关指令优先  2=暂停指令优先")]
        public UInt32 OutPri = 1;

        [PinType(PinTypes.Constant)]
        [PinDisplay("Stop指令复位方式  0=Stop输出永远为1  1=Stop同On/Off一样定义输出")]
        public bool StopR = false;

        [PinType(PinTypes.Constant)]
        [PinDisplay("故障闭锁定义参数  0=任一信号将闭锁输出指令  1=任一信号将不闭锁输出指令")]
        public bool FLB = false;

        [PinType(PinTypes.Constant)]
        [PinDisplay("跳闸置位定义参数")]
        public UInt32 TripM = 1;

        [PinType(PinTypes.Constant)]
        [PinDisplay("设备行程，单位：秒")]
        public double Tover = 10.0;

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
        [PinDisplay("手动优先级")]
        public UInt32 MP = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public UInt32 QualityT = 0;

        // ==================== Internal ====================

        [PinType(PinTypes.Internal)]
        public bool oldFBOn = false;

        [PinType(PinTypes.Internal)]
        public bool oldFBOff = false;

        [PinType(PinTypes.Internal)]
        public bool oldFBStp = false;

        [PinType(PinTypes.Internal)]
        public bool oldCON = false;

        [PinType(PinTypes.Internal)]
        public bool oldCOF = false;

        [PinType(PinTypes.Internal)]
        public bool oldCSP = false;

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
        public bool stpCmdActive = false;

        [PinType(PinTypes.Internal)]
        public double onTimer = 0;

        [PinType(PinTypes.Internal)]
        public double offTimer = 0;

        [PinType(PinTypes.Internal)]
        public double stpTimer = 0;

        [PinType(PinTypes.Internal)]
        public double onToverTimer = 0;

        [PinType(PinTypes.Internal)]
        public double offToverTimer = 0;

        [PinType(PinTypes.Internal)]
        public double stpToverTimer = 0;

        [PinType(PinTypes.Internal)]
        public bool firstRun = true;

        [PinType(PinTypes.Internal)]
        public bool manualForbid = false;

        [PinType(PinTypes.Internal)]
        public bool middleStopActive = false;

    }
}
