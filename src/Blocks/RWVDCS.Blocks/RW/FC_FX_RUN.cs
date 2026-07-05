using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class FX
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;


            _x[0] = X1; _x[1] = X2; _x[2] = X3; _x[3] = X4;
            _x[4] = X5; _x[5] = X6; _x[6] = X7; _x[7] = X8;
            _x[8] = X9; _x[9] = X10; _x[10] = X11; _x[11] = X12;

            _y[0] = Y1; _y[1] = Y2; _y[2] = Y3; _y[3] = Y4;
            _y[4] = Y5; _y[5] = Y6; _y[6] = Y7; _y[7] = Y8;
            _y[8] = Y9; _y[9] = Y10; _y[10] = Y11; _y[11] = Y12;

            // 规则(2)：X1~X12 应递增填写，不递增曲线取到递增转折点为止。
            // 从 X1 向后扫描，遇到第一个不严格递增的点即停止，last 为有效曲线末点索引。
            int last = 0;
            for (int i = 1; i < 12; i++)
            {
                if (_x[i] > _x[i - 1]) last = i;
                else break;
            }

            float x = X;
            if (x <= _x[0])
            {
                OUT[0] = _y[0];
            }
            else if (x >= _x[last])
            {
                OUT[0] = _y[last];
            }
            else
            {
                for (int i = 0; i < last; i++)
                {
                    if (x >= _x[i] && x <= _x[i + 1])
                    {
                        float dx = _x[i + 1] - _x[i];
                        OUT[0] = dx == 0f
                            ? _y[i]
                            : (_y[i + 1] - _y[i]) / dx * (x - _x[i]) + _y[i];
                        break;
                    }
                }
            }

            // 品质传递（QualityT）：FX 仅 X 一个动态输入，OrTransfer/AndTransfer 在单输入下等效（参照 FILTER）。
            // NoTransfer(0)：输出恒 Good；否则把输入 X 的品质透传给 OUT。
            OUT.Quality = QualityT == 0 ? QualityTypes.Good : X.Quality;
        }
    }
}
