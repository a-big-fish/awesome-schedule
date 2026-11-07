using System;
namespace AwesomeInterview.NativeDemo.Data;

public readonly record struct ScheduleTimeChoice(DateTime Value,bool DateOnly);
public static class ScheduleTime
{
    public static DateTime Select(DateTime day,bool dateOnly,TimeSpan time,TimeSpan workTime)
        => DateTime.SpecifyKind(day.Date.Add(dateOnly?workTime:time),DateTimeKind.Unspecified);
    public static DateTime SuggestedEnd(ScheduleTimeChoice start,TimeSpan workEnd)
        => start.DateOnly?start.Value.Date.Add(workEnd):start.Value.AddHours(1);
}
