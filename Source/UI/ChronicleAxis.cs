using RimTalk.Memory.Utils;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

public class ChronicleAxis
{

    private const float AxisLabelGap = 4.0f;


    private const int MaxTickCount = 12;
    private const int MaxYearTickCount = 9;

    // 时间轴天数阈值，超出则切换单位为“象”。此处 5 即我们允许的最大天数步长
    private const int DayTickThreshold = 5 * (MaxTickCount + 1);

    // 时间轴象数阈值，超出则切换单位为“年”。此处 2 即我们允许的最大象数步长
    private const int QuadrumTickThreshold = 2 * (MaxTickCount + 1);


    private const float DayTickHeight = 6f;
    private const float QuadrumTickHeight = 8f;
    private const float YearTickHeight = 10f;







    private int _startAbsTick;
    private int _endAbsTick;

    // 时间轴底栏：按可见天数自适应选刻度步长（1/5/15/60 天），再画刻度线与日期。cursorTick 暂未使用。
    public void DrawAxis(Rect rect, int startGameTick, int endGameTick)
    {
        _startAbsTick = GenDate.TickGameToAbs(startGameTick);
        _endAbsTick = GenDate.TickGameToAbs(endGameTick);

        // 背景
        Widgets.DrawBoxSolid(rect, new Color(0.095f, 0.105f, 0.115f, 0.96f));

        // 两端点
        Color tickColor = new Color(0.38f, 0.42f, 0.45f);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 2f, YearTickHeight), tickColor);
        Widgets.DrawBoxSolid(new Rect(rect.xMax - 2f, rect.y, 2f, YearTickHeight), tickColor);

        Span<int> dayTickList = stackalloc int[MaxTickCount];
        Span<int> quadrumTickList = stackalloc int[MaxTickCount];
        Span<int> yearTickList = stackalloc int[MaxYearTickCount];

        CalculateTickLists(
            dayTickList, quadrumTickList, yearTickList,
            _startAbsTick, _endAbsTick,
            out int dayTickCount, out int quadrumTickCount, out int yearTickCount, out float step
            );




        float width = rect.width;
        float x = rect.x;
        float y = rect.y;
        float xMax = rect.xMax;
        float labelY = rect.y + AxisLabelGap;
        float labelHeight = rect.yMax - labelY;

        int startTick;
        int endTick;
        float tickRange;
        List<long> tickList;
        long step;
        float stepWidth;


        // 绘制刻度线与日期文本
        switch (_endAbsTick - _startAbsTick)
        {
            // 刻度单位为天，最多 6 个刻度，刻度步长超过 5 则切换为象。
            case <= DayTickThreshold * GenDate.TicksPerDay:
                // 换算为天数时需要 +1，因为我们需要表示的是“第几天”，而不是“已经过去了几天“
                startTick = _startAbsTick / GenDate.TicksPerDay + 1;
                endTick = _endAbsTick / GenDate.TicksPerDay + 1;

                tickRange = endTick - startTick;
                tickList = MathUtil.GenerateTicks(startTick, endTick, MaxDayTicks, out step);
                stepWidth = step / tickRange * width;

                for (int i = 0; i < tickList.Count; i++)
                {
                    int dayTick = (int)tickList[i];
                    long absTick = GenDate.TickGameToAbs(dayTick * GenDate.TicksPerDay);

                    if (i == 0)
                        x += (dayTick - startTick) / tickRange * rect.width;
                    else
                        x += stepWidth;

                    using (new TextBlock(GameFont.Tiny))
                        switch (dayTick)
                        {
                            case var _ when dayTick % GenDate.DaysPerYear == 1:
                                Widgets.DrawBoxSolid(new Rect(x, y, 2f, YearTickHeight), tickColor);
                                using (new TextBlock(GameFont.Medium))
                                    Widgets.Label(
                                        new Rect(x + 2f + AxisLabelGap, labelY, xMax - (x + 2f + AxisLabelGap), labelHeight),
                                        $"{GenDate.Year(absTick, 0L)}"
                                        );
                                break;

                            case var _ when dayTick % GenDate.DaysPerQuadrum == 1:
                                Widgets.DrawBoxSolid(new Rect(x, y, 1f, QuadrumTickHeight), tickColor);
                                using (new TextBlock(GameFont.Small))
                                    Widgets.Label(
                                        new Rect(x + 1f + AxisLabelGap, labelY, xMax - (x + 1f + AxisLabelGap), labelHeight),
                                        GenDate.Quadrum(absTick, 0L).Label()
                                        );
                                break;

                            default:
                                Widgets.DrawBoxSolid(new Rect(x, y, 1f, DayTickHeight), tickColor);
                                Widgets.Label(
                                    new Rect(x + 1f + AxisLabelGap, labelY, xMax - (x + 1f + AxisLabelGap), labelHeight),
                                    $"{GenDate.DayOfQuadrum(absTick, 0L) + 1}"
                                    );
                                break;
                        }
                }
                break;

            // 刻度单位为象，最多 9 个刻度，刻度步长超过 2 则切换为年。
            case <= QuadrumTickThreshold * GenDate.TicksPerQuadrum:
                // 换算为象数时需要 +1，因为我们需要表示的是“第几象”，而不是“已经过去了几象“
                startTick = _startAbsTick / GenDate.TicksPerDay + 1;
                endTick = _endAbsTick / GenDate.TicksPerDay + 1;

                tickRange = endTick - startTick;
                tickList = MathUtil.GenerateTicks(startTick, endTick, MaxDayTicks, out step);
                stepWidth = step / tickRange * width;

                for (int i = 0; i < tickList.Count; i++)
                {
                    int dayTick = (int)tickList[i];

                    if (i == 0)
                        x += (dayTick - startTick) / tickRange * rect.width;
                    else
                        x += stepWidth;

                    switch (dayTick)
                    {
                        case var _ when dayTick % GenDate.DaysPerYear == 1:
                            Widgets.DrawBoxSolid(new Rect(x, y, 2f, YearTickHeight), tickColor);
                            break;

                        case var _ when dayTick % GenDate.DaysPerQuadrum == 1:
                            Widgets.DrawBoxSolid(new Rect(x, y, 1f, QuadrumTickHeight), tickColor);
                            break;

                        default:
                            Widgets.DrawBoxSolid(new Rect(x, y, 1f, DayTickHeight), tickColor);
                            break;
                    }
                }


                break;
        }





        // 把 Tick 范围换算成“天”范围，便于按天定刻度。
        // 刻度单位为天
        int firstDay = _startAbsTick / GenDate.TicksPerDay;
        int lastDay = _endAbsTick / GenDate.TicksPerDay;
        int visibleDays = Math.Max(1, lastDay - firstDay);

        // 固定绘制五个刻度
        int textStep = visibleDays / 5;

        Text.Font = GameFont.Tiny;
        GUI.color = new Color(0.62f, 0.66f, 0.69f);
        // 从第一个对齐到 step 的天开始，每隔 step 天画一个刻度。
        for (int day = firstDay - firstDay % step; day <= lastDay; day += step)
        {
            int tick = day * GenDate.TicksPerDay;
            float x = TickToX(tick, startTick, endTick, rect);
            Widgets.DrawBoxSolid(new Rect(x, rect.y, 1f, 7f), new Color(0.38f, 0.42f, 0.45f));
            Widgets.Label(new Rect(x + 3f, rect.y + 7f, 82f, 18f), DateLabel(tick));
        }
        GUI.color = Color.white;
        Text.Font = GameFont.Small;
    }

    // 填充 dayTicks、quadrumTicks、yearTicks 三个 Span，分别表示在当前时间轴范围内的天、象、年刻度列表。
    // 因为是非常纯粹的数学运算，所以封装成一个静态方法。
    private static void CalculateTickLists(
        Span<int> dayTicks, Span<int> quadrumTicks, Span<int> yearTicks,
        int startAbsTick, int endAbsTick,
        out int dayTickCount, out int quadrumTickCount, out int yearTickCount, out float step
        )
    {
        dayTickCount = -1;
        quadrumTickCount = -1;
        yearTickCount = -1;

        int range = endAbsTick - startAbsTick;

        switch (range)
        {
            // 刻度单位为天，刻度步长超过 5 则切换为象。
            case <= DayTickThreshold * GenDate.TicksPerDay:
                step = MathUtil.CalculateNiceStep(range / (float)GenDate.TicksPerDay, MaxTickCount);

                foreach (var dayTickF in MathUtil.GenerateTicksFromStep(
                    startAbsTick / (float)GenDate.TicksPerDay,
                    endAbsTick / (float)GenDate.TicksPerDay,
                    step
                    ))
                {
                    int dayTick = (int)MathF.Round(dayTickF);
                    switch (dayTick)
                    {
                        case var _ when dayTick % GenDate.DaysPerYear == 0:
                            yearTicks[yearTickCount++] = dayTick * GenDate.TicksPerDay;
                            break;
                        case var _ when dayTick % GenDate.DaysPerQuadrum == 0:
                            quadrumTicks[quadrumTickCount++] = dayTick * GenDate.TicksPerDay;
                            break;
                        default:
                            dayTicks[dayTickCount++] = (dayTick - 1) * GenDate.TicksPerDay;
                            break;
                    }
                }
                break;

            // 刻度单位为象，刻度步长超过 2 则切换为年。
            case <= QuadrumTickThreshold * GenDate.TicksPerQuadrum:
                step = MathUtil.CalculateNiceStep(range / (float)GenDate.TicksPerQuadrum, MaxTickCount);

                foreach (var quadrumTickF in MathUtil.GenerateTicksFromStep(
                    startAbsTick / (float)GenDate.TicksPerQuadrum,
                    endAbsTick / (float)GenDate.TicksPerQuadrum,
                    step
                    ))
                {
                    int quadrumTick = (int)MathF.Round(quadrumTickF);
                    switch (quadrumTick)
                    {
                        case var _ when quadrumTick % 4 == 0:
                            yearTicks[yearTickCount++] = quadrumTick * GenDate.TicksPerQuadrum;
                            break;
                        default:
                            dayTicks[dayTickCount++] = quadrumTick * GenDate.TicksPerDay;
                            break;
                    }
                }
                break;

            default:
                // Handle year ticks
                break;
        }
        dayTickCount++;
        quadrumTickCount++;
        yearTickCount++;
    }



}

