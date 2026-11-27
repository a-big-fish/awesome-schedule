using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed class ReferenceEditor : StackPanel
{
    readonly List<(TextBox Name,TextBox Url)> rows = new();
    readonly StackPanel entries = new();
    readonly Action changed;
    public bool HasLinks => rows.Count>0;
    public ReferenceEditor(List<ReferenceLink> values,Action changed)
    {
        this.changed=changed;Children.Add(Text("名称可以留空；每条链接单独保存。",12,"#64748B",new(0,8,0,10)));Children.Add(entries);
        foreach(var value in values)Add(value);
        Children.Add(Button("＋ 添加链接",()=>{if(rows.Count<20){Add(new("",""));changed();}}));
    }
    void Add(ReferenceLink value)
    {
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new(2,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new(3,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var name=new TextBox{Text=value.Title,MaxLength=200,ToolTip="名称（可选）",Margin=new(0,4,8,8)};
        var url=new TextBox{Text=value.Url,MaxLength=2048,ToolTip="https:// 链接",Margin=new(0,4,8,8)};Grid.SetColumn(url,1);
        System.Windows.Automation.AutomationProperties.SetName(name,"参考资料名称");System.Windows.Automation.AutomationProperties.SetName(url,"参考资料链接");
        var pair=(name,url);var remove=Button("移除",()=>{rows.Remove(pair);entries.Children.Remove(row);changed();});remove.Margin=new(0,4,0,8);Grid.SetColumn(remove,2);
        rows.Add(pair);name.TextChanged+=(_,_)=>changed();url.TextChanged+=(_,_)=>changed();row.Children.Add(name);row.Children.Add(url);row.Children.Add(remove);entries.Children.Add(row);
    }
    public static void ValidateUrl(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||string.IsNullOrEmpty(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("链接需为不含账号密码的 http:// 或 https:// 地址。");
    }
    public List<ReferenceLink> Read()
    {
        var result=new List<ReferenceLink>();
        foreach(var (name,url) in rows){if(string.IsNullOrWhiteSpace(name.Text)&&string.IsNullOrWhiteSpace(url.Text))continue;ValidateUrl(url.Text.Trim());result.Add(new(name.Text.Trim(),url.Text.Trim()));}
        return result;
    }
}
