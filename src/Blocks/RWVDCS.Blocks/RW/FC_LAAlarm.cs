﻿using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("LAAlarm")]
    [FCDisplay("模拟量报警")]
    public partial class LAAlarm : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("模拟量点描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 100)]
        public string Description = "";

        [PinType(PinTypes.Constant)]
        [PinDisplay("模拟量点单位")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 3)]
        public string Unit = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("Input value")]
        public LA AnalogPin = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Constant)]
        [PinDisplay("AckMode")]
        public int AckMode = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("LowAlarm1Priority")]
        public byte LowAlarm1Priority;

        [PinType(PinTypes.Constant)]
        [PinDisplay("LowAlarm2Priority")]
        public byte LowAlarm2Priority;

        [PinType(PinTypes.Constant)]
        [PinDisplay("HighAlarm1Priority")]
        public byte HighAlarm1Priority;

        [PinType(PinTypes.Constant)]
        [PinDisplay("HighAlarm2Priority")]
        public byte HighAlarm2Priority;

        [PinType(PinTypes.Constant)]
        [PinDisplay("LowAlarmLimit1Value")]
        public float LowAlarmLimit1Value;

        [PinType(PinTypes.Constant)]
        [PinDisplay("LowAlarmLimit2Value")]
        public float LowAlarmLimit2Value;

        [PinType(PinTypes.Constant)]
        [PinDisplay("HighAlarmLimit1Value")]
        public float HighAlarmLimit1Value;

        [PinType(PinTypes.Constant)]
        [PinDisplay("HighAlarmLimit2Value")]
        public float HighAlarmLimit2Value;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数21")]
        public byte AlarmType;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数22")]
        public byte State;


        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数24")]
        public Int64 Time;

        //内部变量
        [PinType(PinTypes.Internal)]
        [PinDisplay("内部变量1")]
        public byte StateCommand;
    }
}
