﻿using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("LDAlarm")]
    [FCDisplay("数字量报警")]
    public partial class LDAlarm : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("数字量点描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 100)]
        public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("Binary Input 1")]
        public LD BoolPin = new LD(QualityTypes.Good, false, false, false, 0, false);

        //规格数
        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数1")]
        public int AlarmPriority;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数2")]
        public int StatusChecking;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数3")]
        public byte AlarmType;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数4")]
        public byte State;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数5")]
        public byte Quality;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数6")]
        public Int64 Time;

        //内部变量
        [PinType(PinTypes.Internal)]
        [PinDisplay("内部变量1")]
        public byte StateCommand;

        [PinType(PinTypes.Internal)]
        [PinDisplay("内部变量2")]
        public bool Old_IN;
    }
}
