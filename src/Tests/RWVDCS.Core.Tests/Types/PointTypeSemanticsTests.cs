using RWVDCS.Core.Types;

namespace RWVDCS.Core.Tests.Types;

/// <summary>
/// 语义守卫：复刻老系统 LA/LD/LP/LP32 的行为怪癖（force 不对称、报警副作用等），
/// 每条用例都对应老代码中的一段确证行为。
/// </summary>
public class PointTypeSemanticsTests
{
    // ---------- LD ----------

    [Fact]
    public void LD_Value_setter_ignores_force_but_indexer_honors_it()
    {
        // 老系统不对称语义：LD.Value 直写 buffer（无 force 检查）；LD[i] 检查 force。
        var ld = new LD(QualityTypes.Good, false, false, forceValue: true, isForced: 1, value: true);

        ld.Value = false;
        Assert.False((bool)ld.Value); // Value 路径绕过强制

        ld[0] = false;
        Assert.True((bool)ld.Value);  // 索引器路径被强制值覆盖回 true
    }

    [Fact]
    public void LD_IsForced_set_applies_force_value_immediately()
    {
        var ld = new LD(QualityTypes.Good, false, false, forceValue: true, isForced: 0, value: false);
        Assert.False((bool)ld.Value);

        ld.IsForced = 1;
        Assert.True((bool)ld.Value);
    }

    [Fact]
    public void LD_ForceValue_set_updates_buffer_only_when_forced()
    {
        var ld = new LD(QualityTypes.Good, false, false, forceValue: false, isForced: 0, value: false);
        ld.ForceValue = true;
        Assert.False((bool)ld.Value);

        ld.IsForced = 1;
        ld.ForceValue = false;
        Assert.False((bool)ld.Value);
        ld.ForceValue = true;
        Assert.True((bool)ld.Value);
    }

    [Fact]
    public void LD_operators_match_old_behavior()
    {
        var t = new LD(QualityTypes.Good, false, false, false, 0, true);
        var f = new LD(QualityTypes.Good, false, false, false, 0, false);

        Assert.True(t & true);
        Assert.False(t & f);
        Assert.True(t | f);
        Assert.True(t ^ f);
        Assert.True(t == true);
        Assert.True(f != true);
        Assert.True(t);           // implicit bool
        Assert.False((bool)f);
    }

    [Fact]
    public void LD_member_access_roundtrip()
    {
        var ld = new LD();
        ld.SetMemberValue(true, "buffer");
        Assert.Equal(true, ld.GetMemberValue("buffer"));
        ld.SetMemberValue(QualityTypes.Bad, "quality");
        Assert.Equal(QualityTypes.Bad, ld.GetMemberValue("quality"));
        // 老系统 LD 的成员名区分大小写："Buffer" 不命中、静默忽略
        ld.SetMemberValue(false, "Buffer");
        Assert.Equal(true, ld.GetMemberValue("buffer"));
    }

    // ---------- LA ----------

    [Fact]
    public void LA_Value_setter_applies_range_alarms()
    {
        var la = new LA(QualityTypes.Good, false, false, false, false, false,
            maxValue: 100f, minValue: 0f, forceValue: 0f, isForced: 0, value: 50f);

        la.Value = 150f;
        Assert.True(la.MaxReached);
        Assert.True(la.IsHighalarm);
        Assert.True(la.IsAlarm);
        Assert.False(la.MinReached);

        la.Value = 100f; // 恰等于上限：达限但不报警（老系统语义）
        Assert.True(la.MaxReached);
        Assert.False(la.IsHighalarm);
        Assert.False(la.IsAlarm);

        la.Value = -1f;
        Assert.True(la.MinReached);
        Assert.True(la.IsLowalarm);
        Assert.True(la.IsAlarm);

        la.Value = 50f;
        Assert.False(la.MaxReached);
        Assert.False(la.MinReached);
        Assert.False(la.IsAlarm);
    }

    [Fact]
    public void LA_Value_setter_uses_force_value_when_forced()
    {
        var la = new LA(QualityTypes.Good, false, false, false, false, false,
            200f, -200f, forceValue: 77f, isForced: 1, value: 0f);

        la.Value = 10f; // 被忽略，强制值生效
        Assert.Equal(77f, (float)la);
    }

    [Fact]
    public void LA_force_paths_do_not_trigger_alarm_computation()
    {
        // 老系统语义：ForceValue/IsForced 路径只改 buffer，不做量程报警计算。
        var la = new LA(QualityTypes.Good, false, false, false, false, false,
            maxValue: 100f, minValue: 0f, forceValue: 0f, isForced: 0, value: 50f);

        la.IsForced = 1;
        la.ForceValue = 500f; // 远超上限
        Assert.Equal(500f, (float)la);
        Assert.False(la.IsHighalarm); // 不触发报警副作用
        Assert.False(la.MaxReached);
    }

    [Fact]
    public void LA_highalarm_property_links_isalarm()
    {
        var la = new LA();
        la.IsHighalarm = true;
        Assert.True(la.IsAlarm);
        // 属性置 false 不会自动清 IsAlarm（老系统语义）
        la.IsHighalarm = false;
        Assert.True(la.IsAlarm);
    }

    [Fact]
    public void LA_operators_match_old_behavior()
    {
        var a = new LA(QualityTypes.Good, false, false, false, false, false,
            float.MaxValue, float.MinValue, 0f, 0, 6f);
        var b = new LA(QualityTypes.Good, false, false, false, false, false,
            float.MaxValue, float.MinValue, 0f, 0, 3f);

        Assert.Equal(9f, a + b);
        Assert.Equal(3f, a - b);
        Assert.Equal(18f, a * b);
        Assert.Equal(2f, a / b);
        Assert.Equal(0f, a % b);
        Assert.True(a > b);
        Assert.True(b < 6f);
        Assert.Equal(2f, a & b);   // 6 & 3 = 2
        Assert.Equal(5f, a ^ b);   // 6 ^ 3 = 5
        Assert.Equal(7f, a | b);   // 6 | 3 = 7
        Assert.Equal(0f, !a);
        Assert.Equal(~6, (int)~a);
        Assert.Equal(6f, (float)a); // implicit float
    }

    [Fact]
    public void LA_member_access_is_case_insensitive_on_set_exact_on_get()
    {
        // 老系统：LA.SetMemberValue 走 ToLower（大小写不敏感），GetMemberValue 精确匹配。
        var la = new LA();
        la.SetMemberValue(123f, "Buffer");
        Assert.Equal(123f, la.GetMemberValue("buffer"));
        Assert.Null(la.GetMemberValue("Buffer"));
    }

    // ---------- LP ----------

    [Fact]
    public void LP_bit_indexer_reads_and_writes_bits()
    {
        var lp = new LP();
        lp[0] = (ushort)1;
        lp[3] = (ushort)1;
        Assert.Equal((ushort)9, (ushort)lp.Value);
        Assert.Equal((ushort)1, lp[0]);
        Assert.Equal((ushort)0, lp[1]);

        lp[0] = (ushort)0;
        Assert.Equal((ushort)8, (ushort)lp.Value);
    }

    [Fact]
    public void LP_bit_indexer_out_of_range_semantics()
    {
        var lp = new LP { Value = (ushort)0xABCD };
        Assert.Equal((ushort)0xABCD, lp[16]); // 越界读返回整个 buffer
        lp[16] = (ushort)1;                   // 越界写忽略
        Assert.Equal((ushort)0xABCD, (ushort)lp.Value);
        lp[2] = (ushort)5;                    // 值 >1 忽略
        Assert.Equal((ushort)0xABCD, (ushort)lp.Value);
    }

    // ---------- LP32 ----------

    [Fact]
    public void LP32_bit_indexer_reads_and_writes_bits()
    {
        var lp = new LP32();
        lp[31] = 1u;
        Assert.Equal(0x8000_0000u, (uint)lp.Value);
        Assert.Equal(1u, lp[31]);

        lp[31] = 0u;
        Assert.Equal(0u, (uint)lp.Value);
    }

    [Fact]
    public void LP32_bit32_aliases_bit0_like_old_system()
    {
        // 老系统边界判定是 i > 32，i=32 时 1<<32 按 C# 规则等于 1<<0——bit32 实为 bit0 别名。
        var lp = new LP32();
        lp[32] = 1u;
        Assert.Equal(1u, (uint)lp.Value);
        Assert.Equal(1u, lp[32]);
        Assert.Equal(1u, lp[0]);
    }

    [Fact]
    public void LP32_force_semantics()
    {
        var lp = new LP32 { Value = 5u };
        lp.IsForced = 1;
        Assert.Equal(0u, (uint)lp.Value); // 强制瞬间被 forcevalue(默认0) 覆盖
        lp.ForceValue = 42u;
        Assert.Equal(42u, (uint)lp.Value);
    }
}
