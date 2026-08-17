using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RWVDCS.Core.Types;

namespace RWVDCS.Core.Tests.Types;

/// <summary>
/// 布局守卫：老系统字段前缀必须保持原 CLR 布局，新字段只能追加在结构末尾。
/// 这些断言一旦失败，说明有人改了字段顺序/类型——工况/快照二进制会静默损坏。
/// </summary>
public class TypeLayoutTests
{
    [Fact]
    public void LD_layout_is_10_bytes()
    {
        Assert.Equal(10, Unsafe.SizeOf<LD>());
        Assert.Equal(LD.Size, Unsafe.SizeOf<LD>());
        // 老系统偏移：quality 0, istrace 4, isalarm 5, isConnected 6, forcevalue 7, isforced 8, buffer 9
        Assert.Equal(0, (int)Marshal.OffsetOf<LD>("quality"));
        Assert.Equal(4, (int)Marshal.OffsetOf<LD>("istrace"));
        Assert.Equal(5, (int)Marshal.OffsetOf<LD>("isalarm"));
        Assert.Equal(6, (int)Marshal.OffsetOf<LD>("isConnected"));
        Assert.Equal(7, (int)Marshal.OffsetOf<LD>("forcevalue"));
        Assert.Equal(8, (int)Marshal.OffsetOf<LD>("isforced"));
        Assert.Equal(9, (int)Marshal.OffsetOf<LD>("buffer"));
    }

    [Fact]
    public void LA_layout_preserves_legacy_prefix_and_appends_alarm_limits()
    {
        Assert.Equal(76, Unsafe.SizeOf<LA>());
        Assert.Equal(LA.Size, Unsafe.SizeOf<LA>());
        Assert.Equal(0, (int)Marshal.OffsetOf<LA>("quality"));
        Assert.Equal(4, (int)Marshal.OffsetOf<LA>("istrace"));
        Assert.Equal(5, (int)Marshal.OffsetOf<LA>("isalarm"));
        Assert.Equal(6, (int)Marshal.OffsetOf<LA>("forcevalue"));
        Assert.Equal(10, (int)Marshal.OffsetOf<LA>("isforced"));
        Assert.Equal(11, (int)Marshal.OffsetOf<LA>("maxreached"));
        Assert.Equal(12, (int)Marshal.OffsetOf<LA>("minreached"));
        Assert.Equal(13, (int)Marshal.OffsetOf<LA>("ishighalarm"));
        Assert.Equal(14, (int)Marshal.OffsetOf<LA>("islowalarm"));
        Assert.Equal(15, (int)Marshal.OffsetOf<LA>("isConnected"));
        Assert.Equal(16, (int)Marshal.OffsetOf<LA>("maxvalue"));
        Assert.Equal(20, (int)Marshal.OffsetOf<LA>("minvalue"));
        Assert.Equal(24, (int)Marshal.OffsetOf<LA>("buffer"));
        Assert.Equal(28, (int)Marshal.OffsetOf<LA>("highAlarmLimit3Value"));
        Assert.Equal(36, (int)Marshal.OffsetOf<LA>("highAlarmLimit2Value"));
        Assert.Equal(44, (int)Marshal.OffsetOf<LA>("highAlarmLimit1Value"));
        Assert.Equal(52, (int)Marshal.OffsetOf<LA>("lowAlarmLimit3Value"));
        Assert.Equal(60, (int)Marshal.OffsetOf<LA>("lowAlarmLimit2Value"));
        Assert.Equal(68, (int)Marshal.OffsetOf<LA>("lowAlarmLimit1Value"));
    }

    [Fact]
    public void LP_layout_is_12_bytes()
    {
        Assert.Equal(12, Unsafe.SizeOf<LP>());
        Assert.Equal(0, (int)Marshal.OffsetOf<LP>("quality"));
        Assert.Equal(4, (int)Marshal.OffsetOf<LP>("istrace"));
        Assert.Equal(5, (int)Marshal.OffsetOf<LP>("isalarm"));
        Assert.Equal(6, (int)Marshal.OffsetOf<LP>("isConnected"));
        Assert.Equal(7, (int)Marshal.OffsetOf<LP>("forcevalue"));
        Assert.Equal(9, (int)Marshal.OffsetOf<LP>("isforced"));
        Assert.Equal(10, (int)Marshal.OffsetOf<LP>("buffer"));
    }

    [Fact]
    public void LP32_layout_is_16_bytes()
    {
        Assert.Equal(16, Unsafe.SizeOf<LP32>());
        Assert.Equal(0, (int)Marshal.OffsetOf<LP32>("quality"));
        Assert.Equal(4, (int)Marshal.OffsetOf<LP32>("istrace"));
        Assert.Equal(5, (int)Marshal.OffsetOf<LP32>("isalarm"));
        Assert.Equal(6, (int)Marshal.OffsetOf<LP32>("isConnected"));
        Assert.Equal(7, (int)Marshal.OffsetOf<LP32>("forcevalue"));
        Assert.Equal(11, (int)Marshal.OffsetOf<LP32>("isforced"));
        Assert.Equal(12, (int)Marshal.OffsetOf<LP32>("buffer"));
    }
}
