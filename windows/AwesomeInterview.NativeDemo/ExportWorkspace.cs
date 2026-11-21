using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AwesomeInterview.NativeDemo.Data;

namespace AwesomeInterview.NativeDemo;

public partial class MainWindow
{
    int exportGeneration;
    void ShowExportWorkspace(ExperienceData item)
    {
        page="note-export";Begin("导出与预览",item.Title+" · 预览和下载使用同一份导出选项");
        var api=repository.Api!;var owner=repository.Current!.UserId;int generation=++exportGeneration;
        var toolbar=new WrapPanel();toolbar.Children.Add(Button("返回面经",()=>ShowExperienceDetail(item)));content.Children.Add(toolbar);
        var options=new StackPanel();options.Children.Add(Text("导出标题（留空使用面经标题）",13));var title=new TextBox{Text=item.Title,MaxLength=200};options.Children.Add(title);
        var style=new ComboBox{ItemsSource=new[]{"按模块分区 · 保留层级","连续排版 · 保留追问"},SelectedIndex=0,Width=240,HorizontalAlignment=HorizontalAlignment.Left};options.Children.Add(style);
        var fields=new WrapPanel();options.Children.Add(fields);
        string[] flags={"hide_company","hide_position","hide_interviewed_at","hide_on_site_answer","hide_review_answer","hide_interviewer_answer"};
        string[] names={"隐藏公司","隐藏岗位","隐藏面试日期","隐藏现场回答","隐藏复盘答案","隐藏面试官回答"};
        var checks=names.Select(label=>new CheckBox{Content=label,Margin=new(0,8,18,8)}).ToArray();foreach(var check in checks)fields.Children.Add(check);
        content.Children.Add(Surface(options));var feedback=Text("调整选项后点击刷新预览；不会修改面经原文。",13,"#64748B",new(0,0,0,12));content.Children.Add(feedback);
        var host=new StackPanel();bool busy=false;string? previewQuery=null;
        string Query()=>"style="+(style.SelectedIndex==1?"flat":"lines")+"&title="+Uri.EscapeDataString(title.Text.Trim())+string.Concat(checks.Select((check,i)=>check.IsChecked==true?"&"+flags[i]+"=true":""));
        bool Current()=>page=="note-export"&&generation==exportGeneration&&repository.Api==api&&repository.Current?.UserId==owner;
        async Task Preview()
        {
            if(busy||!Current())return;if(!repository.Online){feedback.Text="生成预览需要联网，已显示的页面仍可阅读。";return;}
            var query=Query();if(query==previewQuery){feedback.Text="预览已是当前选项。";return;}busy=true;feedback.Text="正在生成分页预览…";
            try{var document=await api.DocumentAsync($"api/mobile/v1/workspace/experiences/{Uri.EscapeDataString(item.Id)}/document?"+query);if(!Current())return;host.Children.Clear();host.Children.Add(DocumentViewer(document));previewQuery=query;feedback.Text=$"预览已更新 · 共 {document.Pages.Count} 页";}
            catch(Exception e)when(WorkspaceRepository.Expected(e)){feedback.Text=WorkspaceRepository.Explain(e)+"，已保留上一份预览。";}finally{busy=false;}
        }
        async Task Download(string extension)
        {
            if(!repository.Online){feedback.Text="下载文件需要联网。";return;}
            var save=new Microsoft.Win32.SaveFileDialog{Title="导出面经",FileName=string.Concat(item.Title.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c)),DefaultExt=extension,Filter=extension=="pdf"?"PDF 文件|*.pdf":"Markdown 文件|*.md"};
            if(save.ShowDialog(this)!=true)return;var query=Query();feedback.Text="正在生成导出文件…";
            try{
                var bytes=await api.DownloadAsync($"my-schedule/experiences/{Uri.EscapeDataString(item.Id)}/export.{extension}?"+query,extension=="pdf"?"application/pdf":"text/markdown");
                if(!Current())return;
                var temp=save.FileName+"."+Guid.NewGuid().ToString("N")+".tmp";try{await File.WriteAllBytesAsync(temp,bytes);File.Move(temp,save.FileName,true);}finally{if(File.Exists(temp))File.Delete(temp);}
                feedback.Text="已保存到所选位置。";
            }catch(Exception e)when(WorkspaceRepository.Expected(e)){feedback.Text=WorkspaceRepository.Explain(e);}
        }
        var actions=new WrapPanel();actions.Children.Add(Button("刷新分页预览",()=>Run(Preview)));actions.Children.Add(Button("下载 PDF",()=>Run(()=>Download("pdf"))));actions.Children.Add(Button("下载 Markdown",()=>Run(()=>Download("md"))));content.Children.Add(actions);content.Children.Add(host);
        Run(Preview);
    }
}
