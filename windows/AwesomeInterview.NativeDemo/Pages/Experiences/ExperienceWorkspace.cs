using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class ExperienceWorkspace(MainWindow shell, ScheduleTimePicker timePicker) : WorkspacePage(shell)
{
    string notesQuery="";int notesVisibility,notesOrder,notesPage=1;

    internal void ShowExperience()
    {
        Begin(PageRoute.Experiences,"我的面经","我的面经","逐题整理、全文解析、导入文件，保留问题和追问的原有层级");if(NeedAccount())return;
        var toolbar=new WrapPanel();var create=Button("＋ 新建面经",()=>EditExperienceMetadata(null));create.IsEnabled=Repository.Online;toolbar.Children.Add(create);toolbar.Children.Add(Button("同步面经",()=>Run(Repository.SynchronizeAsync)));Content.Children.Add(toolbar);
        var filter=new Grid();filter.ColumnDefinitions.Add(new());filter.ColumnDefinitions.Add(new(){Width=GridLength.Auto});filter.ColumnDefinitions.Add(new(){Width=GridLength.Auto});filter.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var search=new TextBox{Text=notesQuery,Height=44,MinWidth=140,ToolTip="标题、公司或岗位包含关键词"};filter.Children.Add(search);
        var visibility=new ComboBox{ItemsSource=new[]{"全部可见性","仅私密","仅公开"},SelectedIndex=notesVisibility,Width=130};Grid.SetColumn(visibility,1);filter.Children.Add(visibility);
        var sort=new ComboBox{ItemsSource=new[]{"最近更新","面试时间最新","面试时间最早"},SelectedIndex=notesOrder,Width=150};Grid.SetColumn(sort,2);filter.Children.Add(sort);
        void Apply(){notesQuery=search.Text.Trim();notesVisibility=visibility.SelectedIndex;notesOrder=sort.SelectedIndex;notesPage=1;ShowExperience();}
        var apply=Button("筛选",Apply);Grid.SetColumn(apply,3);filter.Children.Add(apply);search.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter)Apply();};
        Content.Children.Add(new Expander{Header="搜索与筛选",Content=Surface(filter),Margin=new(0,8,0,14),IsExpanded=!string.IsNullOrEmpty(notesQuery)||notesVisibility>0});
        var filtered=Repository.Current!.Experiences.Where(e=>(notesVisibility==0||(notesVisibility==2)==e.IsPublic)&&(string.IsNullOrEmpty(notesQuery)||$"{e.Title} {e.Company} {e.Position}".Contains(notesQuery,StringComparison.OrdinalIgnoreCase)));
        var items=(notesOrder switch{1=>filtered.OrderByDescending(e=>e.InterviewedAt),2=>filtered.OrderBy(e=>e.InterviewedAt),_=>filtered.OrderByDescending(e=>e.UpdatedAt)}).ThenBy(e=>e.Id).ToList();
        int pages=Math.Max(1,(items.Count+19)/20);notesPage=Math.Clamp(notesPage,1,pages);Paragraph($"{items.Count} 篇面经 · 第 {notesPage}/{pages} 页"+(Repository.Online?"":" · 离线缓存"));
        if(items.Count==0)Paragraph("没有符合条件的面经，可以新建草稿或调整筛选条件。");
        var cards=new System.Windows.Controls.Primitives.UniformGrid();void Columns()=>cards.Columns=Math.Max(1,Math.Min(3,(int)(Content.ActualWidth/320)));cards.SizeChanged+=(_,_)=>Columns();
        foreach(var item in items.Skip((notesPage-1)*20).Take(20))
        {
            var body=new StackPanel();var title=Text(item.Title,19);title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
            body.Children.Add(Text($"{item.Company??"未填写公司"}    {item.Position}    {item.Questions.Count} 道问题    {(item.IsPublic?"公开":"私密")}",13,"#64748B",new(0,10,0,12)));
            body.Children.Add(Text(item.InterviewedAt?.ToString("yyyy-MM-dd")??"面试时间未填写",12,"#64748B",new(0,0,0,14)));
            var actions=new WrapPanel();actions.Children.Add(Button("阅读与导出",()=>Run(()=>OpenNoteAsync(item.Id,false))));actions.Children.Add(Button("继续录入",()=>Run(()=>OpenNoteAsync(item.Id,true))));body.Children.Add(actions);var card=Surface(body);card.Margin=new(0,0,14,16);cards.Children.Add(card);
        }
        Content.Children.Add(cards);Columns();var paging=new WrapPanel();if(notesPage>1)paging.Children.Add(Button("上一页",()=>{notesPage--;ShowExperience();}));if(notesPage<pages)paging.Children.Add(Button("下一页",()=>{notesPage++;ShowExperience();}));Content.Children.Add(paging);
    }
    static string CategoryLabel(string value)=>value switch{"project"=>"项目问题","fundamentals"=>"八股问题","algorithm"=>"算法题","reverse"=>"反问","other"=>"其他",_=>value};
    internal void ShowExperienceDetail(ExperienceData item,string? author=null)
    {
        Begin(PageRoute.ExperienceDetail,author==null?"我的面经":"面经广场",item.Title,author==null?"我的面经":"作者："+author);
        var toolbar=new WrapPanel();toolbar.Children.Add(Button("返回列表",author==null?ShowExperience:ShowPlaza));
        var hideAnswers=new CheckBox{Content="隐藏所有回答",Margin=new(12,10,18,10)};toolbar.Children.Add(hideAnswers);
        if(author==null){toolbar.Children.Add(Button("编辑内容",()=>Run(()=>OpenNoteAsync(item.Id,true))));var info=Button("修改资料",()=>EditExperienceMetadata(item));info.IsEnabled=Repository.Online;toolbar.Children.Add(info);toolbar.Children.Add(Button("导出与分页预览",()=>ShowExportWorkspace(item)));var delete=Button("删除面经",()=>Run(async()=>{if(!Confirm("删除面经","将删除这篇面经及其问题，是否继续？","删除面经","保留"))return;await Repository.Api!.DeleteAsync("api/mobile/v1/workspace/experiences/"+Uri.EscapeDataString(item.Id));await Repository.RemoveExperienceAsync(item.Id);FormDirty=false;ShowExperience();}));delete.IsEnabled=Repository.Online;toolbar.Children.Add(delete);}
        Content.Children.Add(toolbar);
        Content.Children.Add(Text(string.Join(" · ",new[]{item.Company,item.Position,item.InterviewRound}.Where(value=>!string.IsNullOrWhiteSpace(value))),13,"#52647D",new(0,12,0,8)));
        if(item.TotalMinutes is int total)Content.Children.Add(Text($"总用时 {total} 分钟",13,"#52647D",new(0,0,0,12)));
        if(!string.IsNullOrWhiteSpace(item.Feeling)){
            var feedback=new StackPanel();feedback.Children.Add(Text("面试感受",16,margin:new(0,0,0,8)));feedback.Children.Add(Text(item.Feeling,14));
            Content.Children.Add(Surface(feedback,new(16)));
        }
        var questionsPanel=new System.Windows.Controls.Primitives.UniformGrid{Margin=new(0,8,0,0)};
        Content.Children.Add(questionsPanel);
        var roots=QuestionTree.Ordered(item.Questions).ToList();
        var grouped=roots.GroupBy(row=>row.Item.Category).ToList();
        var answerPanels=new System.Collections.Generic.List<StackPanel>();
        foreach(var group in grouped){
            var section=new StackPanel();
            string duration=item.CategoryMinutes.TryGetValue(group.Key,out int minutes)?$" · {minutes} 分钟":"";
            var title=Text(CategoryLabel(group.Key)+duration,18,"#2357CE",new(0,0,0,14));title.FontWeight=FontWeights.SemiBold;
            section.Children.Add(title);
            foreach(var row in group){
                var q=row.Item;
                var question=new StackPanel{Margin=new(Math.Min(row.Depth,4)*10,0,0,0)};
                var number=Text("Q"+row.Number,11,"#52647D",new(0,0,0,4));question.Children.Add(number);
                var label=Text(q.QuestionText,15);label.FontWeight=FontWeights.SemiBold;question.Children.Add(label);
                var answers=new StackPanel();question.Children.Add(answers);answerPanels.Add(answers);
                void Answer(string caption,string? value){
                    if(string.IsNullOrWhiteSpace(value))return;
                    answers.Children.Add(Text(caption,12,"#52647D",new(0,11,0,4)));
                    answers.Children.Add(Text(value,14));
                }
                if(q.Category=="reverse")Answer("面试官回答",q.InterviewerAnswer);
                else{Answer("现场回答",q.OnSiteAnswer);Answer("复盘答案",q.ReviewAnswer);}
                section.Children.Add(new Border{Padding=new(0,10,0,14),BorderBrush=Brush("#E4EAF2"),BorderThickness=new(0,1,0,0),Child=question});
            }
            var card=Surface(section,new(18));card.Margin=new(0,0,12,12);card.VerticalAlignment=VerticalAlignment.Top;questionsPanel.Children.Add(card);
        }
        if(grouped.Count==0)Content.Children.Add(Surface(Text("暂无问题记录",14,"#52647D")));
        hideAnswers.Click+=(_,_)=>{foreach(var answer in answerPanels)answer.Visibility=hideAnswers.IsChecked==true?Visibility.Collapsed:Visibility.Visible;};
        void Resize()=>questionsPanel.Columns=Math.Clamp((int)(Viewport.ActualWidth/290),1,Math.Max(1,grouped.Count));
        TrackViewportSize(questionsPanel, Resize);
    }
}
