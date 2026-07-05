using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class STAAVG
	{
		protected override void Run(ICommand cmd)
        {// 1. 模块使能逻辑
            _ENO[0] = Enable;
            if (!Enable)
            {
                return;
            }

            // 2. 检测 AMOUNT 是否改变及越界限制 (0 <= AMOUNT <= 900)
            uint currentAmount = Math.Min(AMOUNT, 900);
            if (currentAmount != _lastAmount)
            {
                ClearBuffer();
                _lastAmount = currentAmount;
            }

            // 3. 启停控制逻辑 (如果 START 为 FALSE，停止计算并清空输出)
            if (!START)
            {
                ClearBuffer();

                SUM[0] = 0.0f;
                AVG[0] = 0.0f;
                MAX[0] = 0.0f;
                MIN[0] = 0.0f;
                COUNT[0] = 0.0f;
                FIN[0] = false;
                // Old_X 在清空时通常保持为 0
                Old_X[0] = 0.0f;
                _cycleCounter = 0;

                SetOutputQuality(QualityTypes.Good); // 停止状态下输出品质默认为 Good
                return;
            }

            // 4. 采样周期控制逻辑 (只有当 START 为 TRUE 且满足 ITE 间隔时才采集)
            _cycleCounter++;
            uint currentIte = ITE > 0 ? ITE : 1; // 防止 ITE 被误设为 0 导致不采样

            if (_cycleCounter >= currentIte)
            {
                _cycleCounter = 0; // 计数器复位
                float newVal = X;

                // 极端情况防御：如果容量设为0
                if (currentAmount == 0)
                {
                    SUM[0] = 0.0f; AVG[0] = 0.0f; MAX[0] = 0.0f; MIN[0] = 0.0f;
                    COUNT[0] = 0.0f; FIN[0] = true; Old_X[0] = newVal;
                    UpdateQuality();
                    return;
                }

                // --- 队列操作 (先进先出 FIFO) ---
                if (_currentCount < currentAmount)
                {
                    // 缓冲区未满，直接存入队尾
                    _buffer[_tail] = newVal;
                    _tail = (_tail + 1) % (int)currentAmount;
                    _currentCount++;
                    FIN[0] = false;
                }
                else
                {
                    // 缓冲区已满
                    FIN[0] = true;
                    // 将最先采集的数据 (队头) 输出至 Old_X
                    Old_X[0] = _buffer[_head];
                    // 新数据覆盖旧数据位置
                    _buffer[_head] = newVal;
                    // 头尾指针同时后移
                    _tail = (_tail + 1) % (int)currentAmount;
                    _head = (_head + 1) % (int)currentAmount;
                    // _currentCount 保持为 currentAmount 不变
                }

                COUNT[0] = (float)_currentCount;

                // --- 统计计算 (遍历当前有效缓冲区元素求和及最值) ---
                if (_currentCount > 0)
                {
                    float sumVal = 0.0f;
                    float maxVal = float.MinValue;
                    float minVal = float.MaxValue;

                    for (int i = 0; i < _currentCount; i++)
                    {
                        int index = (_head + i) % (int)currentAmount;
                        float val = _buffer[index];

                        sumVal += val;
                        if (val > maxVal) maxVal = val;
                        if (val < minVal) minVal = val;
                    }

                    SUM[0] = sumVal;
                    AVG[0] = sumVal / _currentCount;
                    MAX[0] = maxVal;
                    MIN[0] = minVal;
                }
            }

            // 5. 品质传递逻辑
            UpdateQuality();
        }

        /// <summary>
        /// 清空内部缓冲区和状态
        /// </summary>
        private void ClearBuffer()
        {
            _head = 0;
            _tail = 0;
            _currentCount = 0;
            Array.Clear(_buffer, 0, _buffer.Length);
        }

        /// <summary>
        /// 处理品质传递
        /// </summary>
        private void UpdateQuality()
        {
            QualityTypes currentQ = QualityTypes.Good;

            if (QualityT == 1) // OrTransfer: 如果输入的品质变坏，则输出品质变坏
            {
                if (X.Quality != QualityTypes.Good)
                {
                    currentQ = QualityTypes.Bad;
                }
            }
            else if (QualityT == 2) // AndTransfer: 由于只有一个实际模拟量输入引脚 X，And 和 Or 在此场景下效果一致
            {
                if (X.Quality != QualityTypes.Good)
                {
                    currentQ = QualityTypes.Bad;
                }
            }

            SetOutputQuality(currentQ);
        }

        /// <summary>
        /// 统一设置各输出引脚的品质
        /// </summary>
        private void SetOutputQuality(QualityTypes q)
        {
            SUM.Quality = q;
            AVG.Quality = q;
            MAX.Quality = q;
            MIN.Quality = q;
            Old_X.Quality = q;
            COUNT.Quality = q;
            FIN.Quality = q;
        }
    }
}
