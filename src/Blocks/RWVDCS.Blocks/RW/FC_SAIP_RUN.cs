using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class SAIP
    {
        protected override void Run(ICommand cmd)
        {
            if (!Enable) return;

            float cycle = cmd.Dpu.Cycle; // 扫描周期(秒)
            float currentX = X;
            float prevX = oldX;

            // 计算变化率 (值/分钟)
            float rate = 0;
            if (cycle > 0)
                rate = (currentX - prevX) / (cycle / 60.0f);
            oldX = currentX;

            // 幅度限制判断：X达到高限H或低限L → 保护动作PAct
            bool overH = (currentX >= H);
            bool underL = (currentX <= L);
            PAct[0] = overH | underL;

            // 变化率限制及高高/低低限判断 → 报警ALM
            // 增速率超限 (IRL>0时检查)
            bool irlTrip = (IRL > 0 && rate > IRL);
            // 减速率超限 (DRL>0时检查，rate为负表示减少)
            bool drlTrip = (DRL > 0 && rate < -DRL);
            // 高高限/低低限
            bool overHH = (currentX >= HH);
            bool underLL = (currentX <= LL);

            // 报警触发：速率超限 或 高高限/低低限
            bool almTrigger = irlTrip | drlTrip | overHH | underLL;

            if (almTrigger)
            {
                ALM[0] = true;
                // 报警时闭锁保护动作PAct
                PAct[0] = false;
            }

            // ACK确认：按ACK确认键对报警确认，消除ALM报警信号
            // 报警复位条件：输入X在低限L和高限H之间时，报警ALM才能复位
            if (ACK & currentX > L & currentX < H)
            {
                ALM[0] = false;
            }
        }
    }
}
