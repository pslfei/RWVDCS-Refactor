using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("DIOLINK")]
    [FCDisplay("IOLINK诊断")]
    public partial class DIOLINK : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("算法块的描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string Description = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string TAG = "";


        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("左机连接状态")]
        public LD LLink = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("右机连接状态")]
        public LD RLink = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("双机同步异常")]
        public LD SyncErr = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("左机A网故障")]
        public LD LNTAErr = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("左机B网故障")]
        public LD LNTBErr = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("右机A网故障")]
        public LD RNTAErr = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("右机B网故障")]
        public LD RNTBErr = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("站号")]
        public UInt32 IOLinkStation = 2;

        [PinType(PinTypes.Constant)]
        [PinDisplay("源端页号")]
        public UInt32 PAGE = 0;



        [PinType(PinTypes.Constant)]
        [PinDisplay("左机连接状态源端页号")]
        public UInt32 LLKPAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("左机连接状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LLK = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("右机连接状态源端页号")]
        public UInt32 RLKPAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("右机连接状态源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RLK = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("双机同步异常源端页号")]
        public UInt32 SYEPAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("双机同步异常源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string SYE = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("左机A网故障源端页号")]
        public UInt32 LAEPAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("左机A网故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LAE = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("左机B网故障源端页号")]
        public UInt32 LBEPAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("左机B网故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string LBE = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("右机A网故障源端页号")]
        public UInt32 RAEPAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("右机A网故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RAE = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("右机B网故障源端页号")]
        public UInt32 RBEPAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("右机B网故障源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string RBE = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("站号源端页号")]
        public UInt32 STNPAGE = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("站号源端测点名")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string STN = "";

    }
}
