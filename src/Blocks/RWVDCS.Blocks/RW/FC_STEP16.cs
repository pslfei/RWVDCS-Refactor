using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("STEP16")]
    [FCDisplay("步序控制16")]
    public partial class STEP16 : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("启动指令")]
        public LD START = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("暂停指令")]
        public LD PAUSE = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("跳步指令")]
        public LD SKIP = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("置步允许")]
        public LD TRACK = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("置步序号")]
        public LA TNO = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作1完成反馈")]
        public LD FB1 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作2完成反馈")]
        public LD FB2 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作3完成反馈")]
        public LD FB3 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作4完成反馈")]
        public LD FB4 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作5完成反馈")]
        public LD FB5 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作6完成反馈")]
        public LD FB6 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作7完成反馈")]
        public LD FB7 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作8完成反馈")]
        public LD FB8 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作9完成反馈")]
        public LD FB9 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作10完成反馈")]
        public LD FB10 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作11完成反馈")]
        public LD FB11 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作12完成反馈")]
        public LD FB12 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作13完成反馈")]
        public LD FB13 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作14完成反馈")]
        public LD FB14 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作15完成反馈")]
        public LD FB15 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作16完成反馈")]
        public LD FB16 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序1超时限定(秒)")]
        public LA TLmt1 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序2超时限定(秒)")]
        public LA TLmt2 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序3超时限定(秒)")]
        public LA TLmt3 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序4超时限定(秒)")]
        public LA TLmt4 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序5超时限定(秒)")]
        public LA TLmt5 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序6超时限定(秒)")]
        public LA TLmt6 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序7超时限定(秒)")]
        public LA TLmt7 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序8超时限定(秒)")]
        public LA TLmt8 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序9超时限定(秒)")]
        public LA TLmt9 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序10超时限定(秒)")]
        public LA TLmt10 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序11超时限定(秒)")]
        public LA TLmt11 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序12超时限定(秒)")]
        public LA TLmt12 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序13超时限定(秒)")]
        public LA TLmt13 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序14超时限定(秒)")]
        public LA TLmt14 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序15超时限定(秒)")]
        public LA TLmt15 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序16超时限定(秒)")]
        public LA TLmt16 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序禁止设定值")]
        public LA BitDis = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("功能块复位指令")]
        public LD RST = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序启动允许")]
        public LD EN = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("HMI 状态打包点 1 (PACK1)，按位映射 FB1-8/OUT1-8/RUN/FAIL/END/START/PAUSE/SKIP/TRACK/RST/EN/paused")]
        public LP32 TAG = new LP32();

        [PinType(PinTypes.Output)]
        [PinDisplay("HMI 状态打包点 2 (PACK2)，按位映射 FB9-16/OUT9-16")]
        public LP32 PK2 = new LP32();

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI 启动命令 CST")]
        public LD CST = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI 中止/暂停命令 CPS")]
        public LD CPS = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI 跳步命令 CSK")]
        public LD CSK = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI 复位命令 CRS")]
        public LD CRS = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI 切手动命令 CTM")]
        public LD CTM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("HMI 投自动命令 CTA")]
        public LD CTA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("使能输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("当前执行步序号")]
        public LA STEP = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("当前步序已消耗(秒)")]
        public LA TRun = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("当前步序剩余(秒)")]
        public LA TRst = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序执行状态输出")]
        public LD RUN = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序故障状态输出")]
        public LD FAIL = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序完成状态输出")]
        public LD END = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序1输出指令")]
        public LD OUT1 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序2输出指令")]
        public LD OUT2 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序3输出指令")]
        public LD OUT3 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序4输出指令")]
        public LD OUT4 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序5输出指令")]
        public LD OUT5 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序6输出指令")]
        public LD OUT6 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序7输出指令")]
        public LD OUT7 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序8输出指令")]
        public LD OUT8 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序9输出指令")]
        public LD OUT9 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序10输出指令")]
        public LD OUT10 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序11输出指令")]
        public LD OUT11 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序12输出指令")]
        public LD OUT12 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序13输出指令")]
        public LD OUT13 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序14输出指令")]
        public LD OUT14 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序15输出指令")]
        public LD OUT15 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序16输出指令")]
        public LD OUT16 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("手动状态指示，手动状态为1，自动状态为0")]
        public LD MA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("最大步数设置值")]
        public float MaxS = 16.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序1设定(秒)")]
        public float TIM1 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序2设定(秒)")]
        public float TIM2 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序3设定(秒)")]
        public float TIM3 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序4设定(秒)")]
        public float TIM4 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序5设定(秒)")]
        public float TIM5 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序6设定(秒)")]
        public float TIM6 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序7设定(秒)")]
        public float TIM7 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序8设定(秒)")]
        public float TIM8 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序9设定(秒)")]
        public float TIM9 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序10设定(秒)")]
        public float TIM10 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序11设定(秒)")]
        public float TIM11 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序12设定(秒)")]
        public float TIM12 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序13设定(秒)")]
        public float TIM13 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序14设定(秒)")]
        public float TIM14 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序15设定(秒)")]
        public float TIM15 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序16设定(秒)")]
        public float TIM16 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public float QualityT = 0.0f;

        [PinType(PinTypes.Internal)]
        public float stepTimer = 0.0f;

        [PinType(PinTypes.Internal)]
        public bool oldSTART = false;

        [PinType(PinTypes.Internal)]
        public bool oldPAUSE = false;

        [PinType(PinTypes.Internal)]
        public bool oldSKIP = false;

        [PinType(PinTypes.Internal)]
        public bool oldTRACK = false;

        [PinType(PinTypes.Internal)]
        public bool oldRST = false;

        [PinType(PinTypes.Internal)]
        public bool paused = false;

        [PinType(PinTypes.Internal)]
        public bool oldCST = false;

        [PinType(PinTypes.Internal)]
        public bool oldCPS = false;

        [PinType(PinTypes.Internal)]
        public bool oldCSK = false;

        [PinType(PinTypes.Internal)]
        public bool oldCRS = false;

        [PinType(PinTypes.Internal)]
        public bool oldCTM = false;

        [PinType(PinTypes.Internal)]
        public bool oldCTA = false;

    }
}
