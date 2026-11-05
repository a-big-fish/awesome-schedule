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
    string plazaCompany="",plazaOrder="newest",plazaOwner="";
    ProfileData? profile;
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
        if(plazaOwner!=scope){plazaOwner=scope;plazaItems.Clear();nextPlazaPage=1;}
        var tools=new DockPanel{Margin=new(0,0,0,18)};var filter=new TextBox{Text=plazaCompany,MinWidth=200,Margin=new(0,0,10,0),ToolTip="按公司筛选"};
        var order=new ComboBox{ItemsSource=new[]{"面试时间：由近到远","面试时间：由远到近"},SelectedIndex=plazaOrder=="newest"?0:1,Padding=new(8),Margin=new(0,0,10,0)};
        var apply=Button("筛选",()=>{plazaCompany=filter.Text.Trim();plazaOrder=order.SelectedIndex==1?"oldest":"newest";plazaItems.Clear();nextPlazaPage=1;ShowPlaza();});apply.IsEnabled=!plazaBusy;DockPanel.SetDock(apply,Dock.Right);tools.Children.Add(apply);DockPanel.SetDock(order,Dock.Right);tools.Children.Add(order);tools.Children.Add(filter);content.Children.Add(tools);
        foreach(var item in plazaItems)
        {
            var row=new StackPanel();row.Children.Add(Text(item.Title,19));row.Children.Add(Text($"{item.Company}    {item.Position}    {item.QuestionCount} 道问题",13,"#64748B",new(0,8,0,8)));row.Children.Add(Text($"作者：{item.Author}    {item.InterviewedAt?.ToString("yyyy-MM-dd")}",12,"#8492A6"));row.Children.Add(Button("阅读面经",()=>Run(async()=>{var detail=await repository.Api!.GetAsync<PublicExperience>("api/mobile/v1/plaza/"+Uri.EscapeDataString(item.Id));if(page=="plaza")ShowExperienceDetail(detail.Experience,detail.Author);})));content.Children.Add(Surface(row));
        }
        if(nextPlazaPage!=null){var load=Button(plazaBusy?"正在加载…":"加载更多",()=>Run(LoadPlazaAsync));load.IsEnabled=!plazaBusy&&repository.Online;content.Children.Add(load);}
        else if(plazaItems.Count==0)Paragraph("没有符合条件的公开面经。");
        if(plazaItems.Count==0&&nextPlazaPage==1&&!plazaBusy&&repository.Online)Run(LoadPlazaAsync);
    }
    async Task LoadPlazaAsync()
    {
        if(plazaBusy||nextPlazaPage==null||repository.Api==null)return;plazaBusy=true;var api=repository.Api;
        try{var batch=await api.GetAsync<PlazaBatch>($"api/mobile/v1/plaza?page={nextPlazaPage}&company={Uri.EscapeDataString(plazaCompany)}&order={plazaOrder}");if(repository.Api!=api)return;foreach(var item in batch.Items)if(plazaItems.All(e=>e.Id!=item.Id))plazaItems.Add(item);nextPlazaPage=batch.NextPage;}
        finally{plazaBusy=false;}if(page=="plaza")ShowPlaza();
    }
    void ShowProfileSettings(StackPanel body)
    {
        body.Children.Add(Text("账号",19,margin:new(0,24,0,16)));
        if(repository.Current==null){body.Children.Add(Button("登录账号",ShowAccount));return;}
        if(profile?.UserId!=repository.Current.UserId)profile=null;
        if(profile==null){var loading=Text(repository.Online?"正在获取账号信息…":"连接后可查看和修改账号信息。",14,"#64748B");body.Children.Add(loading);if(!repository.Online)return;var api=repository.Api;Run(async()=>{try{var result=await api!.GetAsync<ProfileData>("api/mobile/v1/profile");if(repository.Api!=api||repository.Current?.UserId!=result.UserId)return;profile=result;if(page=="settings")ShowSettings();}catch(Exception failure)when(WorkspaceRepository.Expected(failure)){loading.Text=WorkspaceRepository.Explain(failure);if(page=="settings")body.Children.Add(Button("重新加载账号信息",ShowSettings));}});return;}
        body.Children.Add(Text("邮箱（不可修改）",12,"#8492A6"));body.Children.Add(Text(profile.Email??"未绑定邮箱",14,margin:new(0,6,0,18)));
        body.Children.Add(Text("公开显示的用户名"));var name=new TextBox{Text=profile.DisplayName??"",MaxLength=40};body.Children.Add(name);
        var save=Button("保存用户名",()=>Run(async()=>{profile=await repository.Api!.WriteAsync<ProfileData>(HttpMethod.Put,"api/mobile/v1/profile",new{display_name=name.Text.Trim()});status.Text="用户名已同步，广场将显示新名称。";}));save.IsEnabled=repository.Online;body.Children.Add(save);
    }
}
