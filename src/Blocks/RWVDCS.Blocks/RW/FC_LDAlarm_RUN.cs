﻿using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class LDAlarm
    {
        //public LDAlarm()
        //{
        //    this.FcName = "LDAlarm";
        //    this.FcCode = "000";
        //}
        protected override void Run(ICommand cmd)
        {
            Byte unack = 1;
            Byte reset = 0;
            Byte ack = 2;

            bool oldValue = Old_IN;
            Old_IN = BoolPin;

            /*if(BoolPin==nullptr)
            {
                return;
            }*/
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

            Byte isAlarm = 0;
            if (StatusChecking == 1)
            {
                //1:Alarm on zero
                if (BoolPin == false)
                    isAlarm = 1; // 11 means alarm
            }
            else if (StatusChecking == 2)
            {
                //2:Alarm on one
                if (BoolPin == true)
                    isAlarm = 1;
            }
            else if (StatusChecking == 3)
            {
                //3:State change
            }
            else if (StatusChecking == 4)
            {
                //4:Alarm on 0 To 1;
                if (BoolPin == true && oldValue == false)
                    isAlarm = 1;
                else
                    return;
            }
            else if (StatusChecking == 5)
            {
                //5:Alarm on 1 To 0
                if (BoolPin == false && oldValue == true)
                    isAlarm = 1;
                else
                    return;
            }

            //if state is not unack, and alarmtype is changed to alarm, state should be changed to unack.
            if (State != unack)
            {
                //if alarmtype is changed, and is changed to alarm
                if ((isAlarm != AlarmType) && (isAlarm != 0))
                    State = unack;
            }
            if (isAlarm != AlarmType)
            {
                AlarmType = isAlarm;
                Time = DateTime.Now.ToBinary();
            }
        }
    }
}
