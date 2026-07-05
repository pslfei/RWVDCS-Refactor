using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("STEP40")]
    [FCDisplay("步序控制40")]
    public partial class STEP40 : Function
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
        [PinDisplay("置步启动")]
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
        [PinDisplay("步序动作17完成反馈")]
        public LD FB17 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作18完成反馈")]
        public LD FB18 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作19完成反馈")]
        public LD FB19 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作20完成反馈")]
        public LD FB20 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作21完成反馈")]
        public LD FB21 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作22完成反馈")]
        public LD FB22 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作23完成反馈")]
        public LD FB23 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作24完成反馈")]
        public LD FB24 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作25完成反馈")]
        public LD FB25 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作26完成反馈")]
        public LD FB26 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作27完成反馈")]
        public LD FB27 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作28完成反馈")]
        public LD FB28 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作29完成反馈")]
        public LD FB29 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作30完成反馈")]
        public LD FB30 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作31完成反馈")]
        public LD FB31 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作32完成反馈")]
        public LD FB32 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作33完成反馈")]
        public LD FB33 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作34完成反馈")]
        public LD FB34 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作35完成反馈")]
        public LD FB35 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作36完成反馈")]
        public LD FB36 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作37完成反馈")]
        public LD FB37 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作38完成反馈")]
        public LD FB38 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作39完成反馈")]
        public LD FB39 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序动作40完成反馈")]
        public LD FB40 = new LD(QualityTypes.Good, false, false, false, 0, false);

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
        [PinDisplay("步序17超时限定(秒)")]
        public LA TLmt17 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序18超时限定(秒)")]
        public LA TLmt18 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序19超时限定(秒)")]
        public LA TLmt19 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序20超时限定(秒)")]
        public LA TLmt20 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序21超时限定(秒)")]
        public LA TLmt21 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序22超时限定(秒)")]
        public LA TLmt22 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序23超时限定(秒)")]
        public LA TLmt23 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序24超时限定(秒)")]
        public LA TLmt24 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序25超时限定(秒)")]
        public LA TLmt25 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序26超时限定(秒)")]
        public LA TLmt26 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序27超时限定(秒)")]
        public LA TLmt27 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序28超时限定(秒)")]
        public LA TLmt28 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序29超时限定(秒)")]
        public LA TLmt29 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序30超时限定(秒)")]
        public LA TLmt30 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序31超时限定(秒)")]
        public LA TLmt31 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序32超时限定(秒)")]
        public LA TLmt32 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序33超时限定(秒)")]
        public LA TLmt33 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序34超时限定(秒)")]
        public LA TLmt34 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序35超时限定(秒)")]
        public LA TLmt35 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序36超时限定(秒)")]
        public LA TLmt36 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序37超时限定(秒)")]
        public LA TLmt37 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序38超时限定(秒)")]
        public LA TLmt38 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序39超时限定(秒)")]
        public LA TLmt39 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("步序40超时限定(秒)")]
        public LA TLmt40 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 60.0f);

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

        [PinType(PinTypes.Output)]
        [PinDisplay("HMI 状态打包点 3 (PACK3)，按位映射 FB17-32 (低16位)/OUT17-32 (高16位)")]
        public LP32 PK3 = new LP32();

        [PinType(PinTypes.Output)]
        [PinDisplay("HMI 状态打包点 4 (PACK4)，按位映射 FB33-40/OUT33-40")]
        public LP32 PK4 = new LP32();

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
        [PinDisplay("步序17输出指令")]
        public LD OUT17 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序18输出指令")]
        public LD OUT18 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序19输出指令")]
        public LD OUT19 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序20输出指令")]
        public LD OUT20 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序21输出指令")]
        public LD OUT21 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序22输出指令")]
        public LD OUT22 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序23输出指令")]
        public LD OUT23 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序24输出指令")]
        public LD OUT24 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序25输出指令")]
        public LD OUT25 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序26输出指令")]
        public LD OUT26 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序27输出指令")]
        public LD OUT27 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序28输出指令")]
        public LD OUT28 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序29输出指令")]
        public LD OUT29 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序30输出指令")]
        public LD OUT30 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序31输出指令")]
        public LD OUT31 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序32输出指令")]
        public LD OUT32 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序33输出指令")]
        public LD OUT33 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序34输出指令")]
        public LD OUT34 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序35输出指令")]
        public LD OUT35 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序36输出指令")]
        public LD OUT36 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序37输出指令")]
        public LD OUT37 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序38输出指令")]
        public LD OUT38 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序39输出指令")]
        public LD OUT39 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("步序40输出指令")]
        public LD OUT40 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("手动状态指示，手动状态为1，自动状态为0")]
        public LD MA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("最大步数设置值")]
        public float MaxS = 40.0f;

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
        [PinDisplay("步序17设定(秒)")]
        public float TIM17 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序18设定(秒)")]
        public float TIM18 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序19设定(秒)")]
        public float TIM19 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序20设定(秒)")]
        public float TIM20 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序21设定(秒)")]
        public float TIM21 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序22设定(秒)")]
        public float TIM22 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序23设定(秒)")]
        public float TIM23 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序24设定(秒)")]
        public float TIM24 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序25设定(秒)")]
        public float TIM25 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序26设定(秒)")]
        public float TIM26 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序27设定(秒)")]
        public float TIM27 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序28设定(秒)")]
        public float TIM28 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序29设定(秒)")]
        public float TIM29 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序30设定(秒)")]
        public float TIM30 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序31设定(秒)")]
        public float TIM31 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序32设定(秒)")]
        public float TIM32 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序33设定(秒)")]
        public float TIM33 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序34设定(秒)")]
        public float TIM34 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序35设定(秒)")]
        public float TIM35 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序36设定(秒)")]
        public float TIM36 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序37设定(秒)")]
        public float TIM37 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序38设定(秒)")]
        public float TIM38 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序39设定(秒)")]
        public float TIM39 = 999999.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("步序40设定(秒)")]
        public float TIM40 = 999999.0f;

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
