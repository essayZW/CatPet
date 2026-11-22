using System;
using System.Globalization;

namespace CatPet.Core;

/// <summary>
/// 启动问候：先看有没有节日（公历 + 农历），没有就按时段说一句。
/// 文案尽量短——气泡宽度受窗口限制，太长会被裁掉。
/// </summary>
public static class Greeting
{
    /// <summary>按时段 + 节日组合出一句问候。</summary>
    public static string Build(DateTime now)
    {
        var festival = Festival(now);
        var period = Period(now);

        if (festival is not null)
        {
            // 节日优先，只保留很短的时段词
            return period.Short is null ? festival : $"{period.Short}，{festival}";
        }

        return period.Text;
    }

    private readonly record struct PeriodText(string? Short, string Text);

    private static PeriodText Period(DateTime now)
    {
        var h = now.Hour;
        var pick = now.Minute % 3;      // 同一时段里换着说，避免每次一样

        if (h >= 5 && h < 9)
            return new PeriodText("早上好", new[] { "早上好喵～", "早上好，新的一天", "早～喵（伸了个懒腰）" }[pick]);

        if (h >= 9 && h < 11)
            return new PeriodText("上午好", new[] { "上午好喵～", "上午好，慢慢来喵", "上午好，一起加油" }[pick]);

        if (h >= 11 && h < 13)
            return new PeriodText("中午好", new[] { "中午好，记得吃饭喵", "中午好喵～", "中午好，歇一会儿吧" }[pick]);

        if (h >= 13 && h < 18)
            return new PeriodText("下午好", new[] { "下午好喵～", "下午好，来杯水？喵", "下午好，别太累喵" }[pick]);

        if (h >= 18 && h < 22)
            return new PeriodText("晚上好", new[] { "晚上好喵～", "晚上好，今天辛苦了喵", "晚上好，摸摸头喵" }[pick]);

        if (h >= 22 || h < 1)
            return new PeriodText(null, new[] { "夜深了，早点睡喵", "还不睡呀……喵", "（打了个哈欠）" }[pick]);

        return new PeriodText(null, new[] { "凌晨了，快去休息喵", "（困得睁不开眼）", "这么晚还没睡…喵" }[pick]);
    }

    /// <summary>公历节日优先，其次农历节日；都不是就返回 null。</summary>
    private static string? Festival(DateTime now)
    {
        // ---------------- 公历 ----------------
        switch (now.Month, now.Day)
        {
            case (1, 1): return "元旦快乐喵～";
            case (2, 14): return "情人节快乐";
            case (3, 8): return "节日快乐";
            case (4, 1): return "愚人节快乐";
            case (5, 1): return "劳动节快乐";
            case (5, 4): return "青年节快乐";
            case (6, 1): return "儿童节快乐";
            case (9, 10): return "教师节快乐";
            case (10, 1): return "国庆快乐";
            case (12, 24): return "平安夜快乐";
            case (12, 25): return "圣诞快乐喵～";
            case (12, 31): return "今年最后一天啦";
        }
        if (now.Month == 10 && now.Day <= 7) return "国庆假期快乐";

        // ---------------- 农历 ----------------
        try
        {
            var lunar = new ChineseLunisolarCalendar();
            var year = lunar.GetYear(now);
            var month = lunar.GetMonth(now);
            var day = lunar.GetDayOfMonth(now);
            var leap = lunar.GetLeapMonth(year);

            // 有闰月时，闰月之后的月份号会整体后移 1
            var isLeap = leap > 0 && month == leap;
            if (leap > 0 && month >= leap) month--;

            if (month == 12 && day >= 29) return "除夕快乐喵";   // 腊月廿九 / 三十
            if (isLeap) return null;

            switch (month, day)
            {
                case (1, 1): return "过年好喵！";
                case (1, 15): return "元宵节快乐喵";
                case (5, 5): return "端午快乐喵";
                case (7, 7): return "七夕快乐喵";
                case (8, 15): return "中秋快乐喵～";
                case (9, 9): return "重阳节快乐喵";
            }
        }
        catch
        {
            // 农历换算失败就当没有节日
        }

        return null;
    }
}
