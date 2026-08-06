using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("ALML")]
	[FCDisplay("模拟量动态报警器")]
	public partial class ALML : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("测点名")]
        public LA TAG = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("报警低低低限")]
        public LA LLL = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("报警低低限")]
        public LA LL = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("报警低限")]
        public LA L = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("报警高限")]
        public LA H = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("报警高高限")]
        public LA HH = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("报警高高高限")]
        public LA HHH = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Output)]
        [PinDisplay("源端禁用状态: 禁用(1)/启用(0)")]
        public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Constant)]
        [PinDisplay("低低限报警等级")]
        public uint LLAl = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("低限报警等级")]
        public uint LAl = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("高限报警等级")]
        public uint HAl = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("高高限报警等级")]
        public uint HHAl = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("高高高限报警等级")]
        public uint HHHAl = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("低低限报警等级")]
        public uint LLLAl = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("报警特征字")]
        public uint Attr = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("报警组")]
        public uint Grp = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("报警死区")]
        public float DB;

        [PinType(PinTypes.Constant)]
        [PinDisplay("页号")]
        public uint PAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("站号")]
        public uint Station = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("分支号")]
        public uint Branch = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("槽位号")]
        public uint Slot = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("通道号")]
        public uint Channel = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public uint QualityT = 0;

    }
}
