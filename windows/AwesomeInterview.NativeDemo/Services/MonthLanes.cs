using System;
using System.Collections.Generic;
using System.Linq;

namespace AwesomeInterview.NativeDemo.Data;

public static class MonthLanes
{
    public static List<EventData?[]> Arrange(IReadOnlyList<List<EventData>> days)
    {
        var occurrences=days.SelectMany((items,day)=>items.Select(item=>(Item:item,Day:day)));
        // Recurrences share a lane, but individual occurrences retain their status and click target.
        var groups=occurrences.GroupBy(entry=>entry.Item.Id.Split('~',2)[0])
            .OrderByDescending(group=>group.Count()).ThenBy(group=>group.Min(entry=>entry.Item.StartAt))
            .ThenBy(group=>group.Key,StringComparer.Ordinal);
        var lanes=new List<EventData?[]>();
        foreach(var group in groups){
            var perDay=group.GroupBy(entry=>entry.Day).ToList();
            for(int occurrence=0;occurrence<perDay.Max(entries=>entries.Count());occurrence++){
                var run=perDay.Where(entries=>entries.Count()>occurrence).Select(entries=>entries.ElementAt(occurrence)).ToList();
                var lane=lanes.FirstOrDefault(row=>run.All(entry=>row[entry.Day]==null));
                if(lane==null){lane=new EventData?[days.Count];lanes.Add(lane);}
                foreach(var entry in run)lane[entry.Day]=entry.Item;
            }
        }
        return lanes;
    }
}
