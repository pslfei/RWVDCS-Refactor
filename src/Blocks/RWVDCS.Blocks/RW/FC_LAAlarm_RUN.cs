﻿using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class LAAlarm
    {
        //public LAAlarm()
        //{
        //    this.FcName = "LAAlarm";
        //    this.FcCode = "000";
        //}
        protected override void Run(ICommand cmd)
        {
            Byte unack = 1;
            Byte reset = 0;
            Byte ack = 2;

            /*if(IN==nullptr)
            {
                return;
            }*/

            Byte alarmType = 0;
            if (AnalogPin < LowAlarmLimit1Value)
            {
                alarmType = 2;
                if (AnalogPin < LowAlarmLimit2Value)
                {
                    alarmType = 3;
                }
            }
            else if (AnalogPin > HighAlarmLimit1Value)
            {
                alarmType = 7;
                if (AnalogPin > HighAlarmLimit2Value)
                {
                    alarmType = 8;
                }
            }
            //if state is not unack, and alarmtype is changed to alarm, state should be changed to unack.
            if (State != unack)
            {
                //if alarmtype is changed, and is changed to alarm
                if ((alarmType != AlarmType) && (alarmType != 0))
                    State = unack;
            }
            if (alarmType != AlarmType)
            {
                AlarmType = alarmType;
                Time = DateTime.Now.ToBinary();
            }


            if (StateCommand == 1 && State == unack)
            {
                //if state is unack, and point is acked, state should be changed to ack
                State = ack;
                Time = DateTime.Now.ToBinary();
            }
            else if (StateCommand == 2 && State == ack && AlarmType == 0)
            {
                //if state is reseted, and point is in returntype, and state is in ack, state should be changed to reset state.
                State = reset;
                Time = DateTime.Now.ToBinary();
            }
        }
    }
}
