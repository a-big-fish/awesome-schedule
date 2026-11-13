using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

public partial class MainWindow
{
    readonly List<PlazaItem> plazaItems=new();
    int? nextPlazaPage=1;
    bool plazaBusy;
    int plazaTotal;
    string plazaDraftCompany="";int plazaDraftOrder;bool plazaExpanded;int plazaGeneration;TextBox? companyInput;
    string plazaCompany="",plazaOrder="newest",plazaOwner="";
    ProfileData? profile;
    string profileScope="";
    void ShowExperience()
    {
        page="experiences";bSelect("我的面经");Begin("我的面经","保留问题、追问和回答的原有层级");if(NeedAccount())return;
        var items=repository.Current!.Experiences.OrderByDescending(e=>e.UpdatedAt).ToList();
        if(items.Count==0){Paragraph("尚未保存面经。");return;}
        foreach(var item in items)
        {
            var body=new StackPanel();var title=Text(item.Title,19);title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
            body.Children.Add(Text($"{item.Company??"未填写公司"}    {item.Position}    {item.Questions.Count} 道问题    {(item.IsPublic?"公开":"私密")}",13,"#64748B",new(0,10,0,12)));
            body.Children.Add(Button("阅读与导出",()=>ShowExperienceDetail(item)));content.Children.Add(Surface(body));
        }
    }
    static string CategoryLabel(string value)=>value switch{"project"=>"项目问题","fundamentals"=>"八股问题","algorithm"=>"算法题","reverse"=>"反问","other"=>"其他",_=>value};
    void ShowExperienceDetail(ExperienceData item,string? author=null)
    {
        page="experience-detail";Begin(item.Title,author==null?"我的面经":"作者："+author);
        var toolbar=new WrapPanel();toolbar.Children.Add(Button("返回列表",author==null?ShowExperience:ShowPlaza));
        var hideAnswers=new CheckBox{Content="隐藏所有回答",Margin=new(12,10,18,10)};toolbar.Children.Add(hideAnswers);
        if(author==null){toolbar.Children.Add(Button("下载 PDF",()=>Run(()=>ExportAsync(item,"pdf",hideAnswers.IsChecked==true))));toolbar.Children.Add(Button("下载 Markdown",()=>Run(()=>ExportAsync(item,"md",hideAnswers.IsChecked==true))));}
        content.Children.Add(toolbar);
        var paper=new StackPanel{MaxWidth=860,HorizontalAlignment=HorizontalAlignment.Stretch};paper.Children.Add(Text($"{item.Company}    {item.Position}    {item.InterviewRound}",13,"#64748B",new(0,0,0,14)));
        if(item.TotalMinutes is int total)paper.Children.Add(Text($"总用时（{total} min）",13,"#64748B",new(0,0,0,12)));
        if(!string.IsNullOrWhiteSpace(item.Feeling)){paper.Children.Add(Text("面试感受",18,margin:new(0,8,0,12)));paper.Children.Add(Text(item.Feeling,15,margin:new(0,0,0,24)));}
        var questionsPanel=new StackPanel();paper.Children.Add(questionsPanel);
        void Questions()
        {
            questionsPanel.Children.Clear();string? category=null;
            foreach(var row in QuestionTree.Ordered(item.Questions))
            {
                var q=row.Item;
                if(row.Depth==0&&q.Category!=category){category=q.Category;var minutes=item.CategoryMinutes.TryGetValue(category,out var m)?$"（{m} min）":"";questionsPanel.Children.Add(Text(CategoryLabel(category)+minutes,19,"#285DDD",new(0,22,0,14)));}
                var body=new StackPanel{Margin=new(Math.Min(row.Depth,8)*20+14,0,0,0)};var title=Text($"Q{row.Number}   {q.QuestionText}",16);title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
                if(hideAnswers.IsChecked!=true)
                {
                    void Answer(string label,string? value){if(!string.IsNullOrWhiteSpace(value)){body.Children.Add(Text(label,12,"#64748B",new(0,12,0,5)));body.Children.Add(Text(value,14));}}
                    if(q.Category=="reverse")Answer("面试官回答",q.InterviewerAnswer);else{Answer("现场回答",q.OnSiteAnswer);Answer("复盘答案",q.ReviewAnswer);}
                }
                questionsPanel.Children.Add(new Border{BorderBrush=Brush("#D7E3F8"),BorderThickness=new(1,0,0,0),Padding=new(0,7,0,14),Margin=new(0,0,0,8),Child=body});
            }
        }
        hideAnswers.Click+=(_,_)=>Questions();Questions();content.Children.Add(Surface(paper,new(28)));
    }
    async Task ExportAsync(ExperienceData item,string extension,bool hide)
    {
        if(!repository.Online||repository.Api==null){status.Text="文件导出需要连接服务器。";return;}
        var save=new Microsoft.Win32.SaveFileDialog{Title="导出面经",FileName=string.Concat(item.Title.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)),DefaultExt=extension,Filter=extension=="pdf"?"PDF 文件|*.pdf":"Markdown 文件|*.md"};
        if(save.ShowDialog(this)!=true)return;
        string flags=hide?"&hide_on_site_answer=true&hide_review_answer=true&hide_interviewer_answer=true":"";
        var bytes=await repository.Api.DownloadAsync($"my-schedule/experiences/{Uri.EscapeDataString(item.Id)}/export.{extension}?style=lines"+flags,extension=="pdf"?"application/pdf":"text/markdown");
        var temp=save.FileName+".tmp";try{await File.WriteAllBytesAsync(temp,bytes);File.Move(temp,save.FileName,true);}finally{if(File.Exists(temp))File.Delete(temp);}status.Text="导出已保存。";
    }
    void ShowPlaza()
    {
        page="plaza";bSelect("面经广场");Begin("面经广场","来自公开面经的真实问题与经验");if(NeedAccount())return;
        string scope=repository.Api!.Server.AbsoluteUri+repository.Current!.UserId;
        if(plazaOwner!=scope){plazaOwner=scope;plazaItems.Clear();plazaCompany="";plazaDraftCompany="";plazaDraftOrder=0;plazaOrder="newest";plazaExpanded=false;nextPlazaPage=1;}
        var tools=new Grid{VerticalAlignment=VerticalAlignment.Top};tools.ColumnDefinitions.Add(new());tools.ColumnDefinitions.Add(new());tools.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var companyField=new StackPanel{Margin=new(0,0,16,0)};companyField.Children.Add(Text("公司筛选",13,"#64748B"));var filter=new TextBox{Text=plazaDraftCompany,Height=48,Padding=new(10,6,10,6),VerticalAlignment=VerticalAlignment.Top,ToolTip="输入公司名称",Margin=new(0,8,0,0)};companyInput=filter;filter.TextChanged+=(_,_)=>plazaDraftCompany=filter.Text;System.Windows.Automation.AutomationProperties.SetName(filter,"公司名称包含关键词，例如：音");companyField.Children.Add(filter);tools.Children.Add(companyField);
        var orderField=new StackPanel{Margin=new(0,0,16,0)};orderField.Children.Add(Text("面试时间排序",13,"#64748B"));var order=new ComboBox{ItemsSource=new[]{"从新到旧","从旧到新"},SelectedIndex=plazaDraftOrder,Margin=new(0,8,0,0)};order.SelectionChanged+=(_,_)=>plazaDraftOrder=order.SelectedIndex;orderField.Children.Add(order);Grid.SetColumn(orderField,1);tools.Children.Add(orderField);
        var apply=Button("筛选",()=>{plazaGeneration++;plazaCompany=filter.Text.Trim();plazaOrder=order.SelectedIndex==1?"oldest":"newest";plazaItems.Clear();plazaTotal=0;nextPlazaPage=1;ShowPlaza();});filter.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&apply.IsEnabled){apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));e.Handled=true;}};apply.VerticalAlignment=VerticalAlignment.Bottom;apply.Margin=new(0);apply.IsEnabled=!plazaBusy;Grid.SetColumn(apply,2);tools.Children.Add(apply);var expander=new Expander{Header="筛选条件 · 公司名称支持包含搜索",IsExpanded=plazaExpanded,Content=Surface(tools),Margin=new(0,0,0,12)};expander.Expanded+=(_,_)=>plazaExpanded=true;expander.Collapsed+=(_,_)=>plazaExpanded=false;content.Children.Add(expander);
        content.Children.Add(Text($"找到 {plazaTotal} 篇面经",14,"#64748B",new(0,4,0,18)));
        var cards=new System.Windows.Controls.Primitives.UniformGrid();
        void Columns(){cards.Columns=Math.Max(1,Math.Min(3,(int)(content.ActualWidth/320)));}cards.SizeChanged+=(_,_)=>Columns();
        foreach(var item in plazaItems)
        {
            var row=new StackPanel();row.Children.Add(Text("公开面经"+(string.IsNullOrWhiteSpace(item.InterviewRound)?"":" · "+item.InterviewRound),12,"#285DDD",new(0,0,0,12)));
            var title=Text(item.Title,19);title.FontWeight=FontWeights.SemiBold;row.Children.Add(title);row.Children.Add(Text($"{item.Company} · {item.Position}",13,"#64748B",new(0,12,0,10)));row.Children.Add(Text($"作者：{item.Author}",13,"#64748B",new(0,0,0,20)));
            var metadata=new DockPanel{Margin=new(0,14,0,0)};var count=Text($"{item.QuestionCount} 道问题",12,"#64748B");DockPanel.SetDock(count,Dock.Right);metadata.Children.Add(count);metadata.Children.Add(Text(item.InterviewedAt?.ToString("yyyy-MM-dd")??"面试时间未填写",12,"#64748B"));row.Children.Add(new Border{BorderBrush=Brush("#E4EAF2"),BorderThickness=new(0,1,0,0),Child=metadata});
            var card=Button("",()=>Run(async()=>{var detail=await repository.Api!.GetAsync<PublicExperience>("api/mobile/v1/plaza/"+Uri.EscapeDataString(item.Id));if(page=="plaza")ShowExperienceDetail(detail.Experience,detail.Author);}));card.Content=row;card.HorizontalContentAlignment=HorizontalAlignment.Stretch;card.VerticalContentAlignment=VerticalAlignment.Top;card.Padding=new(22);card.Margin=new(0,0,14,16);card.MinHeight=230;System.Windows.Automation.AutomationProperties.SetName(card,item.Title+"，阅读面经");cards.Children.Add(card);
        }
        content.Children.Add(cards);Columns();
        if(nextPlazaPage!=null){var load=Button(plazaBusy?"正在加载…":"加载更多",()=>Run(LoadPlazaAsync));load.IsEnabled=!plazaBusy&&repository.Online;content.Children.Add(load);}
        else if(plazaItems.Count==0)Paragraph("没有符合条件的公开面经。");
        if(plazaItems.Count==0&&nextPlazaPage==1&&!plazaBusy&&repository.Online)Run(LoadPlazaAsync);
    }
    async Task LoadPlazaAsync()
    {
        if(plazaBusy||nextPlazaPage==null||repository.Api==null)return;plazaBusy=true;var api=repository.Api;int generation=plazaGeneration;
        try{var batch=await api.GetAsync<PlazaBatch>($"api/mobile/v1/plaza?page={nextPlazaPage}&company={Uri.EscapeDataString(plazaCompany)}&order={plazaOrder}");if(repository.Api!=api||generation!=plazaGeneration)return;foreach(var item in batch.Items)if(plazaItems.All(e=>e.Id!=item.Id))plazaItems.Add(item);nextPlazaPage=batch.NextPage;plazaTotal=batch.Total;}
        finally{plazaBusy=false;}if(page=="plaza"){bool focused=companyInput?.IsKeyboardFocusWithin==true;int caret=companyInput?.CaretIndex??0;ShowPlaza();if(focused&&companyInput!=null){companyInput.Focus();companyInput.CaretIndex=Math.Min(caret,companyInput.Text.Length);}}
    }
    async Task LoadProfileAsync()
    {
        var api=repository.Api??throw new ApiFailure(401,"请先登录。");string scope=api.Server.AbsoluteUri+repository.Current?.UserId;
        if(profile!=null&&profileScope==scope)return;profile=null;
        var result=await api.GetAsync<ProfileData>("api/mobile/v1/profile");if(repository.Api!=api||repository.Current?.UserId!=result.UserId)return;profile=result;profileScope=scope;repository.SetProfile(result);
    }
    void ShowWorkdaySettings(StackPanel body)
    {
        body.Children.Add(Text("默认工作时段",19));body.Children.Add(Text("只选日期的新日程使用该起止时刻；已有日程不受影响。格式 HH:mm，同一天内结束须晚于开始。",13,"#64748B",new(0,10,0,18)));
        if(profile==null||profileScope!=repository.Api?.Server.AbsoluteUri+repository.Current?.UserId){var message=Text("正在读取…",14);body.Children.Add(message);if(!repository.Online){message.Text="连接后可设置工作时段。";return;}Run(async()=>{try{await LoadProfileAsync();if(page=="settings")ShowSettings();}catch(Exception e)when(WorkspaceRepository.Expected(e)){message.Text=WorkspaceRepository.Explain(e);body.Children.Add(Button("重新读取",ShowSettings));}});return;}
        body.Children.Add(Text("开始时间"));var start=new TextBox{Text=profile.WorkdayStart,MaxLength=5};body.Children.Add(start);body.Children.Add(Text("结束时间"));var end=new TextBox{Text=profile.WorkdayEnd,MaxLength=5};body.Children.Add(end);var feedback=Text("",13);body.Children.Add(feedback);
        var save=Button("保存工作时段",()=>Run(async()=>{try{profile=await repository.Api!.WriteAsync<ProfileData>(HttpMethod.Put,"api/mobile/v1/profile",new{display_name=profile.EffectiveName,workday_start=start.Text.Trim(),workday_end=end.Text.Trim()});repository.SetProfile(profile);feedback.Text=profile.WorkdayStart==start.Text.Trim()&&profile.WorkdayEnd==end.Text.Trim()?"已同步；下次新增日程生效。":"服务器未保存该时段，请更新服务端后重试。";}catch(Exception e)when(WorkspaceRepository.Expected(e)){feedback.Text=WorkspaceRepository.Explain(e);}}));save.IsEnabled=repository.Online;save.HorizontalAlignment=HorizontalAlignment.Left;body.Children.Add(save);
    }
    void ShowProfileSettings(StackPanel body)
    {
        body.Children.Add(Text("账号",19,margin:new(0,24,0,16)));
        if(repository.Current==null){body.Children.Add(Button("登录账号",ShowAccount));return;}
        if(profileScope!=repository.Api!.Server.AbsoluteUri+repository.Current.UserId){profile=null;profileScope=repository.Api.Server.AbsoluteUri+repository.Current.UserId;}
        if(profile?.UserId!=repository.Current.UserId)profile=null;
        if(profile==null){var loading=Text(repository.Online?"正在获取账号信息…":"连接后可查看和修改账号信息。",14,"#64748B");body.Children.Add(loading);if(!repository.Online)return;var api=repository.Api;Run(async()=>{try{var result=await api!.GetAsync<ProfileData>("api/mobile/v1/profile");if(repository.Api!=api||repository.Current?.UserId!=result.UserId)return;profile=result;if(page=="settings")ShowSettings();}catch(Exception failure)when(WorkspaceRepository.Expected(failure)){loading.Text=WorkspaceRepository.Explain(failure);if(page=="settings")body.Children.Add(Button("重新加载账号信息",ShowSettings));}});return;}
        body.Children.Add(Text("邮箱（不可修改）",12,"#8492A6"));body.Children.Add(Text(profile.Email??"未绑定邮箱",14,margin:new(0,6,0,18)));
        body.Children.Add(Text("公开显示的用户名"));var name=new TextBox{Text=profile.EffectiveName,MaxLength=40};body.Children.Add(name);if(string.IsNullOrWhiteSpace(profile.DisplayName))body.Children.Add(Text("账号尚未设置昵称，已填入默认用户名；保存后同步。",12,"#64748B"));body.Children.Add(Button("重新读取账号资料",()=>{profile=null;ShowSettings();}));
        var save=Button("保存用户名",()=>Run(async()=>{profile=await repository.Api!.WriteAsync<ProfileData>(HttpMethod.Put,"api/mobile/v1/profile",new{display_name=name.Text.Trim()});repository.SetProfile(profile);status.Text="用户名已同步，广场将显示新名称。";}));save.IsEnabled=repository.Online;body.Children.Add(save);
    }
}
