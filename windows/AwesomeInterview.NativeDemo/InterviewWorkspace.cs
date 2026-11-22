using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;
public partial class MainWindow
{
    string interviewQuery="",interviewStatus="all";int interviewPage=1;
    int resumeGeneration;
    CancellationTokenSource? resumeUpload;
    void ShowInterviews()
    {
        page="interviews";bSelect("我的面试");Begin("我的面试","准备资料、进入会议，再把问题整理成面经");if(NeedAccount())return;
        var bar=new WrapPanel();bar.Children.Add(Button("我的简历",()=>Run(ShowResumeAsync)));bar.Children.Add(Button("＋ 添加面试",()=>ShowEditor(null,null,"interview")));bar.Children.Add(Button("同步面试列表",()=>Run(repository.RefreshCalendarAsync)));content.Children.Add(bar);
        var filters=new DockPanel{Margin=new(0,8,0,18)};
        var statusFilter=new ComboBox{ItemsSource=new[]{"全部","待进行","已结束"},SelectedIndex=interviewStatus=="upcoming"?1:interviewStatus=="finished"?2:0,Width=140};
        var query=new TextBox{Text=interviewQuery,MinWidth=240,ToolTip="搜索标题、岗位或备注"};
        var apply=Button("搜索",()=>{interviewQuery=query.Text.Trim();interviewStatus=statusFilter.SelectedIndex==1?"upcoming":statusFilter.SelectedIndex==2?"finished":"all";interviewPage=1;ShowInterviews();});
        DockPanel.SetDock(apply,Dock.Right);filters.Children.Add(apply);DockPanel.SetDock(statusFilter,Dock.Right);filters.Children.Add(statusFilter);filters.Children.Add(query);content.Children.Add(filters);
        var now=DateTimeOffset.UtcNow;
        var rows=repository.Current!.Events.Where(e=>e.Type=="interview").Where(e=>string.IsNullOrEmpty(interviewQuery)||$"{e.Title} {e.Position} {e.Note}".Contains(interviewQuery,StringComparison.OrdinalIgnoreCase))
            .Where(e=>interviewStatus=="all"||(interviewStatus=="upcoming"?e.EndAt>now&&e.CancelledAt==null&&e.CompletedAt==null:e.EndAt<=now||e.CompletedAt!=null)).OrderByDescending(e=>e.StartAt).ThenBy(e=>e.Id).ToList();
        int pages=Math.Max(1,(rows.Count+19)/20);interviewPage=Math.Clamp(interviewPage,1,pages);
        Paragraph($"{rows.Count} 场面试 · 第 {interviewPage}/{pages} 页"+(repository.Online?"":" · 本机缓存"));
        foreach(var item in rows.Skip((interviewPage-1)*20).Take(20)){
            var body=new StackPanel();body.Children.Add(Text(item.Title,20));body.Children.Add(Text($"{item.Local(item.StartAt):yyyy-MM-dd HH:mm} — {item.Local(item.EndAt):HH:mm} · {item.Position}",13,"#64748B",new(0,8,0,16)));
            var actions=new WrapPanel();actions.Children.Add(Button("查看日程",()=>ShowEventDetail(item)));if(item.MeetingUrl!=null)actions.Children.Add(Button("点击入会",()=>OpenLink(item.MeetingUrl)));
            actions.Children.Add(Button("简历",()=>Run(ShowResumeAsync)));
            var notes=repository.Current.Experiences.Where(e=>e.LinkedEventId==item.Id).ToList();
            foreach(var note in notes)actions.Children.Add(Button("面经 · "+note.Title,()=>ShowExperienceDetail(note)));
            actions.Children.Add(Button("＋ 记录面经",()=>EditExperienceMetadata(null,item)));body.Children.Add(actions);content.Children.Add(Surface(body));
        }
        if(rows.Count==0)Paragraph("没有符合条件的面试；可以添加日程或调整搜索条件。");
        var paging=new WrapPanel();if(interviewPage>1)paging.Children.Add(Button("上一页",()=>{interviewPage--;ShowInterviews();}));if(interviewPage<pages)paging.Children.Add(Button("下一页",()=>{interviewPage++;ShowInterviews();}));content.Children.Add(paging);
    }
    async Task ShowResumeAsync()
    {
        page="resume";bSelect("我的面试");Begin("我的简历","本机版本先显示；同步时只下载有变化的版本");if(NeedAccount())return;
        int generation=++resumeGeneration;var api=repository.Api!;string owner=repository.Current!.UserId;
        var feedback=Text("正在读取缓存…",13,"#64748B",new(0,12,0,12));var host=new StackPanel();CachedDocument? cached=null;
        var tools=new WrapPanel();tools.Children.Add(Button("返回面试",ShowInterviews));tools.Children.Add(Button("同步最新简历",()=>Run(()=>LoadResumeAsync(true))));tools.Children.Add(Button("上传/替换 PDF",()=>Run(UploadResumeAsync)));tools.Children.Add(Button("取消上传",()=>resumeUpload?.Cancel()));content.Children.Add(tools);
        content.Children.Add(feedback);content.Children.Add(ResumeColumns(host));
        tools.Children.Add(Button("查看上一版本",()=>Run(async()=>{var previous=await repository.ReadDocumentAsync(true);if(page!="resume"||generation!=resumeGeneration||repository.Api!=api||repository.Current?.UserId!=owner)return;if(previous==null){feedback.Text="暂无上一版本，成功替换过简历后会自动保留。";return;}host.Children.Clear();host.Children.Add(DocumentViewer(previous.Document));feedback.Text="上一版本 · "+previous.Document.FileName;})));
        try{cached=await repository.ReadDocumentAsync();}catch(Exception e)when(WorkspaceRepository.Expected(e)){feedback.Text="缓存不可用，联网后可重新获取。";}
        if(page!="resume"||generation!=resumeGeneration||repository.Api!=api||repository.Current?.UserId!=owner)return;
        if(cached!=null){feedback.Text=$"本机版本 · {cached.FetchedAt:yyyy-MM-dd HH:mm}";host.Children.Add(DocumentViewer(cached.Document));}
        async Task LoadResumeAsync(bool force)
        {
            if(!repository.Online){feedback.Text=cached==null?"离线且没有已缓存简历。":"当前离线，显示已缓存简历。";return;}
            try{
                var state=await api.GetAsync<ResumeState>("api/mobile/v1/workspace/resume");
                if(page!="resume"||generation!=resumeGeneration||repository.Api!=api||repository.Current?.UserId!=owner)return;
                if(state.Resume==null){host.Children.Clear();feedback.Text="尚未上传简历。";return;}
                if(!force&&cached?.Document.Version==state.Resume.Version){feedback.Text="已是最新版本 · "+state.Resume.FileName;return;}
                feedback.Text="正在下载并准备分页预览…";
                var document=await api.DocumentAsync("api/mobile/v1/workspace/resume/document");
                if(page!="resume"||generation!=resumeGeneration||repository.Api!=api||repository.Current?.UserId!=owner)return;
                await repository.SaveDocumentAsync(document);cached=new(owner,DateTimeOffset.UtcNow,document);host.Children.Clear();host.Children.Add(DocumentViewer(document));feedback.Text=state.Resume.FileName+" · 已缓存，可离线阅读";
            }catch(Exception e)when(WorkspaceRepository.Expected(e)){feedback.Text=WorkspaceRepository.Explain(e)+(cached!=null?" 已保留旧版本。":"");}
        }
        async Task UploadResumeAsync()
        {
            if(!repository.Online){feedback.Text="上传需要联网。";return;}
            var picker=new Microsoft.Win32.OpenFileDialog{Filter="PDF 简历|*.pdf"};if(picker.ShowDialog(this)!=true)return;
            if(new FileInfo(picker.FileName).Length>20*1024*1024){feedback.Text="文件不能超过20MB。";return;}
            resumeUpload?.Cancel();resumeUpload?.Dispose();resumeUpload=new();var cancellation=resumeUpload.Token;
            try{
                var progress=new Progress<double>(value=>feedback.Text=value>=1?"上传完成，服务端正在解析…":$"正在上传 {value:P0}");
                feedback.Text="正在上传…";
                await api.UploadAsync<ResumeState>("api/mobile/v1/workspace/resume",picker.FileName,"resume",progress,cancellation);
                if(repository.Api==api&&repository.Current?.UserId==owner&&page=="resume"&&generation==resumeGeneration)await LoadResumeAsync(true);
            }catch(OperationCanceledException){feedback.Text="上传已取消或超时；请同步核对服务端结果，旧版本仍保留。";}
            catch(Exception e)when(WorkspaceRepository.Expected(e)){feedback.Text=WorkspaceRepository.Explain(e);}
        }
        if(repository.Online)await LoadResumeAsync(false);else if(cached==null)feedback.Text="离线且没有已缓存简历。";
    }
    UIElement DocumentViewer(DocumentPages document)
    {
        var root=new StackPanel();if(document.Pages.Count==0){root.Children.Add(Text("文件没有可预览页面。"));return root;}
        int index=0;double scale=1;var image=new Image{Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Center};
        var label=Text("",14,margin:new(10,10,10,10));var controls=new WrapPanel();root.Children.Add(controls);
        void Display(){using var input=new MemoryStream(document.Pages[index]);var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();image.Source=bitmap;label.Text=$"{index+1} / {document.Pages.Count} 页 · {scale:P0}";}
        controls.Children.Add(Button("上一页",()=>{if(index>0){index--;Display();}}));controls.Children.Add(label);controls.Children.Add(Button("下一页",()=>{if(index+1<document.Pages.Count){index++;Display();}}));controls.Children.Add(Button("缩小",()=>{scale=Math.Max(.5,scale-.25);Display();}));controls.Children.Add(Button("放大",()=>{scale=Math.Min(2,scale+.25);Display();}));
        var scroll=new QuietScrollViewer{Content=image,Height=Math.Max(340,viewport.ActualHeight-210),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brush("#EEF2F7")};
        void Fit(){if(scroll.ActualWidth>24)image.Width=(scroll.ActualWidth-24)*scale;label.Text=$"{index+1} / {document.Pages.Count} 页 · {scale:P0}";}
        scroll.SizeChanged+=(_,_)=>Fit();image.Margin=new(12);
        controls.Children.Add(Button("适应宽度",()=>{scale=1;Fit();}));
        controls.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,new RoutedEventHandler((_,_)=>Fit()));
        root.Children.Add(scroll);Display();return Surface(root,new(12));
    }
}
