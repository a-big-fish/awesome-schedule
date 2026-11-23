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

internal sealed partial class ExperienceWorkspace
{
    readonly List<PlazaItem> plazaItems=new();
    int? nextPlazaPage=1;
    bool plazaBusy;
    int plazaTotal;
    string plazaDraftCompany="";int plazaDraftOrder;bool plazaExpanded;int plazaGeneration;TextBox? companyInput;
    string plazaCompany="",plazaOrder="newest",plazaOwner="";

    internal void OnViewportScroll(ScrollChangedEventArgs args)
    {
        if (Route != PageRoute.Plaza || args.VerticalChange <= 0 ||
            Viewport.ScrollableHeight - Viewport.VerticalOffset >= 160 || plazaBusy || nextPlazaPage == null ||
            !Repository.Online)
            return;

        Run(LoadPlazaAsync);
    }

    internal void ShowPlaza()
    {
        Begin(PageRoute.Plaza,"面经广场","面经广场","来自公开面经的真实问题与经验");if(NeedAccount())return;
        string scope=Repository.Api!.Server.AbsoluteUri+Repository.Current!.UserId;
        if(plazaOwner!=scope){plazaOwner=scope;plazaItems.Clear();plazaCompany="";plazaDraftCompany="";plazaDraftOrder=0;plazaOrder="newest";plazaExpanded=false;nextPlazaPage=1;}
        var tools=new Grid{VerticalAlignment=VerticalAlignment.Top};tools.ColumnDefinitions.Add(new());tools.ColumnDefinitions.Add(new());tools.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var companyField=new StackPanel{Margin=new(0,0,16,0)};companyField.Children.Add(Text("公司筛选",13,"#64748B"));var filter=new TextBox{Text=plazaDraftCompany,Height=48,Padding=new(10,6,10,6),VerticalAlignment=VerticalAlignment.Top,ToolTip="输入公司名称",Margin=new(0,8,0,0)};companyInput=filter;filter.TextChanged+=(_,_)=>plazaDraftCompany=filter.Text;System.Windows.Automation.AutomationProperties.SetName(filter,"公司名称包含关键词，例如：音");companyField.Children.Add(filter);tools.Children.Add(companyField);
        var orderField=new StackPanel{Margin=new(0,0,16,0)};orderField.Children.Add(Text("面试时间排序",13,"#64748B"));var order=new ComboBox{ItemsSource=new[]{"从新到旧","从旧到新"},SelectedIndex=plazaDraftOrder,Margin=new(0,8,0,0)};order.SelectionChanged+=(_,_)=>plazaDraftOrder=order.SelectedIndex;orderField.Children.Add(order);Grid.SetColumn(orderField,1);tools.Children.Add(orderField);
        var apply=Button("筛选",()=>{plazaGeneration++;plazaCompany=filter.Text.Trim();plazaOrder=order.SelectedIndex==1?"oldest":"newest";plazaItems.Clear();plazaTotal=0;nextPlazaPage=1;ShowPlaza();});filter.KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter&&apply.IsEnabled){apply.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));e.Handled=true;}};apply.VerticalAlignment=VerticalAlignment.Bottom;apply.Margin=new(0);apply.IsEnabled=!plazaBusy;Grid.SetColumn(apply,2);tools.Children.Add(apply);var expander=new Expander{Header="筛选条件 · 公司名称支持包含搜索",IsExpanded=plazaExpanded,Content=Surface(tools),Margin=new(0,0,0,12)};expander.Expanded+=(_,_)=>plazaExpanded=true;expander.Collapsed+=(_,_)=>plazaExpanded=false;Content.Children.Add(expander);
        Content.Children.Add(Text($"找到 {plazaTotal} 篇面经",14,"#64748B",new(0,4,0,18)));
        var cards=new System.Windows.Controls.Primitives.UniformGrid();
        void Columns(){cards.Columns=Math.Max(1,Math.Min(3,(int)(Content.ActualWidth/320)));}cards.SizeChanged+=(_,_)=>Columns();
        foreach(var item in plazaItems)
        {
            var row=new StackPanel();row.Children.Add(Text("公开面经"+(string.IsNullOrWhiteSpace(item.InterviewRound)?"":" · "+item.InterviewRound),12,"#285DDD",new(0,0,0,12)));
            var title=Text(item.Title,19);title.FontWeight=FontWeights.SemiBold;row.Children.Add(title);row.Children.Add(Text($"{item.Company} · {item.Position}",13,"#64748B",new(0,12,0,10)));row.Children.Add(Text($"作者：{item.Author}",13,"#64748B",new(0,0,0,20)));
            var metadata=new DockPanel{Margin=new(0,14,0,0)};var count=Text($"{item.QuestionCount} 道问题",12,"#64748B");DockPanel.SetDock(count,Dock.Right);metadata.Children.Add(count);metadata.Children.Add(Text(item.InterviewedAt?.ToString("yyyy-MM-dd")??"面试时间未填写",12,"#64748B"));row.Children.Add(new Border{BorderBrush=Brush("#E4EAF2"),BorderThickness=new(0,1,0,0),Child=metadata});
            var card=Button("",()=>Run(async()=>{var detail=await Repository.Api!.GetAsync<PublicExperience>("api/mobile/v1/plaza/"+Uri.EscapeDataString(item.Id));if(Route == PageRoute.Plaza)ShowExperienceDetail(detail.Experience,detail.Author);}));card.Content=row;card.HorizontalContentAlignment=HorizontalAlignment.Stretch;card.VerticalContentAlignment=VerticalAlignment.Top;card.Padding=new(22);card.Margin=new(0,0,14,16);card.MinHeight=230;System.Windows.Automation.AutomationProperties.SetName(card,item.Title+"，阅读面经");cards.Children.Add(card);
        }
        Content.Children.Add(cards);Columns();
        if(nextPlazaPage!=null){var load=Button(plazaBusy?"正在加载…":"加载更多",()=>Run(LoadPlazaAsync));load.IsEnabled=!plazaBusy&&Repository.Online;Content.Children.Add(load);}
        else if(plazaItems.Count==0)Paragraph("没有符合条件的公开面经。");
        if(plazaItems.Count==0&&nextPlazaPage==1&&!plazaBusy&&Repository.Online)Run(LoadPlazaAsync);
    }
    async Task LoadPlazaAsync()
    {
        if(plazaBusy||nextPlazaPage==null||Repository.Api==null)return;plazaBusy=true;var api=Repository.Api;int generation=plazaGeneration;
        try{var batch=await api.GetAsync<PlazaBatch>($"api/mobile/v1/plaza?page={nextPlazaPage}&company={Uri.EscapeDataString(plazaCompany)}&order={plazaOrder}");if(Repository.Api!=api||generation!=plazaGeneration)return;foreach(var item in batch.Items)if(plazaItems.All(e=>e.Id!=item.Id))plazaItems.Add(item);nextPlazaPage=batch.NextPage;plazaTotal=batch.Total;}
        finally{plazaBusy=false;}if(Route == PageRoute.Plaza){bool focused=companyInput?.IsKeyboardFocusWithin==true;int caret=companyInput?.CaretIndex??0;ShowPlaza();if(focused&&companyInput!=null){companyInput.Focus();companyInput.CaretIndex=Math.Min(caret,companyInput.Text.Length);}}
    }
}
