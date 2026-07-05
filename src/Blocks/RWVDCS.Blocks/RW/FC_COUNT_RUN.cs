using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
namespace RWVDCS.Blocks.RW
{
    public partial class COUNT
    {
        public COUNT() 
        {
            if (MODE == 0)//加计数(INC)
            {
                OUT[0] = 0;
            }
            else if (MODE == 1)//减计数(DEC)
            {
                OUT[0] = SetV;
            }
        }

        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            //if (RST)
            //{
            //    if (MODE == 0)//加计数(INC)
            //    {
            //        OUT[0] = 0;
            //    }
            //    else if (MODE == 1)//减计数(DEC)
            //    {
            //        OUT[0] = SetV;
            //    }

            //    END[0] = false;
            //}


            //bool OLD_X = false;
            //if (MODE == 0)//INC
            //{
            //    if (OLD_X == false && X == true)
            //    {

            //        OUT[0] = OUT + 1;
            //        END[0] = true;
            //    }
            //}
            //else if (MODE == 1)//DEC
            //{
            //    if (OLD_X == true && X == false)
            //    {
            //        OUT[0] = OUT - 1;
            //        END[0] = true;
            //    }
            //}

            //if (OUT >= SetV)
            //{
            //    OUT[0] = SetV;
            //    END[0] = false;
            //}

            //if (OUT <= 0)
            //{
            //    OUT[0] = 0;
            //    END[0] = false;
            //}

            //OLD_X = X;

            // 使用局部变量暂存计数值，避免直接频繁读写 OUT 对象
            float tempOut = OUT;
            // 2. 复位逻辑 (RST 具有最高优先级)
            if (RST)
            {
                if (MODE == 0)      // INC 复位为 0
                    tempOut = 0.0f;
                else if (MODE == 1) // DEC 复位为 SetV
                    tempOut = SetV;
                END[0] = false;     // 复位时 END 强制为 FALSE
                OLD_X = X;          // 更新历史值，防止复位松开瞬间产生误触发
                OUT[0] = tempOut;
                return;             // 复位期间不处理计数逻辑，直接返回
            }
            // 3. 上升沿检测
            bool risingEdge = (!OLD_X & X);
            // 4. 计数及结束判断逻辑
            if (MODE == 0) // INC 加计数
            {
                // 如果有上升沿，且未达到设定值，则计数值加 1
                if (risingEdge && tempOut < SetV)
                {
                    tempOut += 1.0f;
                }
                // 判断是否到达设定值 (计数结束)
                if (tempOut >= SetV)
                {
                    tempOut = SetV;  // 停止计数（限幅）
                    END[0] = true;   // 计数结束，置位 TRUE
                }
                else
                {
                    END[0] = false;  // 计数过程中，保持 FALSE
                }
            }
            else if (MODE == 1) // DEC 减计数
            {
                // 文档要求：减计数同样是上升沿触发！
                // 如果有上升沿，且未减到 0，则计数值减 1
                if (risingEdge && tempOut > 0)
                {
                    tempOut -= 1.0f;
                }
                // 判断是否减到 0 (计数结束)
                if (tempOut <= 0)
                {
                    tempOut = 0.0f;  // 停止计数（限幅）
                    END[0] = true;   // 计数结束，置位 TRUE
                }
                else
                {
                    END[0] = false;  // 计数过程中，保持 FALSE
                }
            }
            // 5. 更新历史状态和输出
            OLD_X = X;
            OUT[0] = tempOut;
        }
    }
}
