using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AwesomeInterview.NativeDemo.Data;
using static AwesomeInterview.NativeDemo.WorkspaceControls;

namespace AwesomeInterview.NativeDemo;

internal sealed partial class ExperienceWorkspace
{
    async Task OpenNoteAsync(string id,bool edit)
    {
        var api=Repository.Api;var owner=Repository.Current?.UserId;
        var item=Repository.Current?.Experiences.FirstOrDefault(e=>e.Id==id);
        if(Repository.Online&&api!=null)try{
            var envelope=await api.GetAsync<ExperienceEnvelope>("api/mobile/v1/experiences/"+Uri.EscapeDataString(id));
            if(Repository.Api!=api||Repository.Current?.UserId!=owner)return;
            item=envelope.Full;await Repository.PutExperienceAsync(item);
        }catch(Exception e)when(WorkspaceRepository.Expected(e)){if(item==null)throw;Status.Text="服务端读取失败，保留本机内容："+WorkspaceRepository.Explain(e);}
        if(item==null)return;
        if(edit){
            var local=await Repository.ReadEditorDraftAsync(id);
            if(Repository.Api!=api||Repository.Current?.UserId!=owner)return;
            if(local!=null&&local.OwnerId==owner&&System.Text.Json.JsonSerializer.Serialize(local,Wire.Json)!=System.Text.Json.JsonSerializer.Serialize(item,Wire.Json)&&Confirm("发现本机草稿","是否恢复上次编辑内容？若服务器版本已变化，保存时会检查冲突。","恢复草稿","使用服务端")){
                item=local.HasCompleteDraft?local:local with{RawTextDraft=string.IsNullOrEmpty(local.RawTextDraft)?item.RawTextDraft:local.RawTextDraft,HasCompleteDraft=item.HasCompleteDraft};
            }
            ShowNoteEditor(item);
        }else ShowExperienceDetail(item);
    }
    internal void EditExperienceMetadata(ExperienceData? item,EventData? linked=null)
    {
        if(!Repository.Online){Status.Text="创建或修改面经资料需要联网，已有正文可以保存本机草稿。";return;}
        var body=new StackPanel{Margin=new(24)};
        var title=new TextBox{Text=item?.Title??linked?.Title??"",MaxLength=200};
        var company=new TextBox{Text=item?.Company??"",MaxLength=200};
        var position=new TextBox{Text=item?.Position??linked?.Position??"",MaxLength=200};
        var round=new TextBox{Text=item?.InterviewRound??"",MaxLength=50};
        var interviews=Repository.Current!.Events.Where(e=>e.Type=="interview").ToList();
        var association=new ComboBox{ItemsSource=new[]{"不关联面试"}.Concat(interviews.Select(e=>e.Title)),SelectedIndex=Math.Max(0,interviews.FindIndex(e=>e.Id==(item?.LinkedEventId??linked?.Id))+1)};
        foreach(var p in new[]{("标题",title),("公司",company),("岗位",position),("轮次（可选）",round)}){body.Children.Add(Text(p.Item1));body.Children.Add(p.Item2);}
        body.Children.Add(Text("关联面试"));body.Children.Add(association);
        DateTimeOffset? interviewDate=item?.InterviewedAt??linked?.StartAt;Button? dateButton=null;
        string DateCaption()=>interviewDate is DateTimeOffset date?CalendarProjection.DisplayTime(date).ToString("yyyy-MM-dd HH:mm"):"选择面试日期（可选）";
        dateButton=Button(DateCaption(),()=>{var choice=timePicker.Pick("面试日期",interviewDate is DateTimeOffset value?CalendarProjection.DisplayTime(value):CalendarProjection.Now,false,"Asia/Shanghai");if(choice!=null){var date=choice.Value.Value;interviewDate=new DateTimeOffset(date,TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai").GetUtcOffset(date));dateButton!.Content=DateCaption();}});body.Children.Add(dateButton);body.Children.Add(Button("清除日期",()=>{interviewDate=null;dateButton.Content=DateCaption();}));
        body.Children.Add(Text("关联面试时使用该场面试的时间。",12,"#64748B"));var feedback=Text("",13,"#B42332");body.Children.Add(feedback);
        var dialog=new Window{Title=item==null?"新建面经草稿":"面经资料",Owner=Owner,Width=560,Height=660,MaxHeight=SystemParameters.WorkArea.Height-50,WindowStartupLocation=WindowStartupLocation.CenterOwner,FontFamily=FontFamily,Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}};
        Button? save=null;save=Button("保存面经资料",()=>Run(async()=>{
            if(string.IsNullOrWhiteSpace(title.Text)){feedback.Text="请输入标题。";return;}save!.IsEnabled=false;
            var api=Repository.Api!;string owner=Repository.Current!.UserId;
            try{
                string? associated=association.SelectedIndex>0?interviews[association.SelectedIndex-1].Id:null;
                var envelope=await api.WriteAsync<ExperienceEnvelope>(item==null?HttpMethod.Post:HttpMethod.Put,"api/mobile/v1/experiences"+(item==null?"":"/"+Uri.EscapeDataString(item.Id)),
                    new{title=title.Text.Trim(),company=company.Text.Trim(),position=position.Text.Trim(),interview_round=round.Text.Trim(),linked_event_id=associated,
                    interviewed_at=interviewDate,is_public=item?.IsPublic??false});
                if(Repository.Api!=api||Repository.Current?.UserId!=owner)return;
                var saved=envelope.Full with{Questions=item?.Questions??new()};
                await Repository.PutExperienceAsync(saved);dialog.Close();if(item==null)ShowNoteEditor(saved,true);else ShowExperienceDetail(saved);
            }catch(Exception e)when(WorkspaceRepository.Expected(e)){feedback.Text=WorkspaceRepository.Explain(e);}finally{save.IsEnabled=true;}
        }));body.Children.Add(save);var cancel=Button("取消",()=>dialog.Close());cancel.IsCancel=true;body.Children.Add(cancel);dialog.ShowDialog();
    }

    readonly Dictionary<string, (Uri Server, ExperienceData Draft)> pendingLocalDrafts = new();

    async Task SaveLocalDraftAsync(Uri server, ExperienceData draft)
    {
        if (Repository.Api?.Server != server || !await Repository.SaveEditorDraftAsync(draft))
            return;
        if (pendingLocalDrafts.TryGetValue(draft.Id, out var pending) && ReferenceEquals(draft, pending.Draft))
            pendingLocalDrafts.Remove(draft.Id);
    }

    internal async Task FlushDraftsForUpdateAsync(CancellationToken token)
    {
        foreach (var (server, draft) in pendingLocalDrafts.Values.ToArray())
        {
            if (Repository.Api?.Server != server || Repository.Current?.UserId != draft.OwnerId ||
                Repository.Current.Experiences.All(item => item.Id != draft.Id))
                continue;
            await SaveLocalDraftAsync(server, draft).WaitAsync(token);
            if (pendingLocalDrafts.ContainsKey(draft.Id))
                throw new UpdatePreparationException("有面经草稿尚未保存，请保存草稿后重试。");
        }
    }

    int noteEditorGeneration;
    void ShowNoteEditor(ExperienceData original,bool newNote=false)
    {
        Begin(PageRoute.NoteEditor,"我的面经","编辑面经",original.Title+" · 双击问题或回答原位编辑，空回答按需展开");
        int generation=++noteEditorGeneration;var api=Repository.Api!;string owner=Repository.Current!.UserId;
        var draft=original with{Questions=new(original.Questions)};var version=original.UpdatedAt;int revision=0;bool saving=false;
        CancellationTokenSource? debounce=null;CancellationTokenSource? parseDelay=null;
        var feedback=Text("本机草稿与服务器保存状态会显示在这里。",13,"#64748B",new(0,8,0,14));
        var toolbar=new WrapPanel();Content.Children.Add(toolbar);Content.Children.Add(feedback);
        var publicBox=new CheckBox{Content="公开面经",IsChecked=newNote||original.IsPublic,Margin=new(12,10,12,10)};
        var mode="questions";var workspace=new StackPanel();var questionHost=new StackPanel();Content.Children.Add(workspace);
        var newQuestion=new TextBox{MaxLength=2000,ToolTip="输入问题后失焦即加入草稿"};var category=new ComboBox{ItemsSource=new[]{"项目问题","八股问题","算法题","其他","反问"},SelectedIndex=0};
        string[] categories={"project","fundamentals","algorithm","other","reverse"};
        async Task SaveDraft(bool final)
        {
            if(saving){feedback.Text="正在保存上一份草稿，请稍候。";return;}
            saving=true;int sentRevision=revision;var sent=draft;bool savedLocally=false;
            try{
                savedLocally=await Repository.SaveEditorDraftAsync(sent);if(!savedLocally)return;
                if(!sent.HasCompleteDraft){feedback.Text="已保存本机草稿。此缓存不包含完整原文，联网后请重新打开并恢复草稿，核对后再同步。";return;}
                if(!Repository.Online){feedback.Text="已保存本机草稿；联网后点击保存同步。";return;}
                var result=await api.WriteAsync<ExperienceEnvelope>(HttpMethod.Put,$"api/mobile/v1/workspace/experiences/{Uri.EscapeDataString(draft.Id)}/draft",
                    new{version,questions=sent.Questions,category_minutes=sent.CategoryMinutes,total_minutes=sent.TotalMinutes,feeling=sent.Feeling,raw_text_draft=sent.RawTextDraft});
                if(Repository.Api!=api||Repository.Current?.UserId!=owner)return;
                version=result.Experience.UpdatedAt;
                if(revision==sentRevision)draft=result.Full;else draft=draft with{UpdatedAt=version};
                await Repository.PutExperienceAsync(result.Full);await Repository.SaveEditorDraftAsync(draft);
                feedback.Text="草稿已同步 · "+CalendarProjection.Now.ToString("HH:mm");
                if(final&&revision==sentRevision){
                    var metadata=await api.WriteAsync<ExperienceEnvelope>(HttpMethod.Put,"api/mobile/v1/experiences/"+draft.Id,
                        new{title=draft.Title,company=draft.Company,position=draft.Position,linked_event_id=draft.LinkedEventId,interviewed_at=draft.InterviewedAt,interview_round=draft.InterviewRound,is_public=publicBox.IsChecked==true});
                    if(Repository.Api!=api||Repository.Current?.UserId!=owner)return;
                    draft=metadata.Full with{Questions=draft.Questions};await Repository.PutExperienceAsync(draft);await Repository.SaveEditorDraftAsync(draft);
                    FormDirty=false;ShowExperience();
                }
            }catch(ApiFailure e)when(e.Status==409){feedback.Text="服务器内容已变化，本机草稿已保留。请重新打开面经，核对后再恢复；不会覆盖服务器。";}
            catch(Exception e)when(WorkspaceRepository.Expected(e)){feedback.Text=(savedLocally?"本机草稿已保留；":"尚未写入磁盘，请勿退出；")+WorkspaceRepository.Explain(e);}
            finally{saving=false;}
        }
        void Changed()
        {
            revision++;FormDirty=true;feedback.Text="尚未同步 · 正在保存本机草稿…";debounce?.Cancel();debounce=new();var token=debounce.Token;var value=draft;
            pendingLocalDrafts[value.Id] = (api.Server, value);
            Run(async()=>{
                try{await Task.Delay(450,token);await SaveLocalDraftAsync(api.Server,value);if(token.IsCancellationRequested)return;feedback.Text="已保存本机草稿";await Task.Delay(1550,token);if(Route == PageRoute.NoteEditor&&generation==noteEditorGeneration)await SaveDraft(false);}
                catch(OperationCanceledException){}
            });
        }
        void ChangeQuestion(string id,Func<QuestionData,QuestionData> change){draft=draft with{Questions=draft.Questions.Select(q=>q.Id==id?change(q):q).ToList()};Changed();}
        QuestionData NewQuestion(string text,string cat,string? parent=null)=>new(){Id=Guid.NewGuid().ToString(),ExperienceId=draft.Id,QuestionText=text,Category=cat,ParentQuestionId=parent,AskedOrder=draft.Questions.Count+1,SiblingOrder=draft.Questions.Count(q=>q.ParentQuestionId==parent)+1};
        void Inline(Panel parent,QuestionData q,string? value,string empty,Func<QuestionData,string,QuestionData> setter,bool question=false)
        {
            var holder=new StackPanel();parent.Children.Add(holder);
            void Edit(){
                holder.Children.Clear();var input=new TextBox{Text=value??"",AcceptsReturn=true,MaxLength=question?2000:20000,MinHeight=48,TextWrapping=TextWrapping.Wrap};
                input.LostKeyboardFocus+=(_,_)=>{if(question&&string.IsNullOrWhiteSpace(input.Text)){feedback.Text="问题内容不能为空。";return;}ChangeQuestion(q.Id,x=>setter(x,input.Text));Dispatcher.BeginInvoke(new Action(RenderQuestions));};
                holder.Children.Add(input);input.Focus();input.CaretIndex=input.Text.Length;
            }
            if(string.IsNullOrWhiteSpace(value)){holder.Children.Add(Button(empty,Edit));return;}
            var text=Text(value,question?16:14,question?"#192C46":"#52647D",new(0,4,0,8));text.ToolTip="双击编辑";text.MouseLeftButtonDown+=(_,e)=>{if(e.ClickCount==2){Edit();e.Handled=true;}};holder.Children.Add(text);
        }
        void RenderQuestions()
        {
            questionHost.Children.Clear();
            foreach(var row in QuestionTree.Ordered(draft.Questions)){
                var q=row.Item;var body=new StackPanel{Margin=new(Math.Min(row.Depth,8)*18,0,0,0)};
                body.Children.Add(Text("Q"+row.Number+" · "+CategoryLabel(q.Category),12,"#285DDD"));
                Inline(body,q,q.QuestionText,"填写问题",(x,t)=>x with{QuestionText=t},true);
                if(q.Category=="reverse")Inline(body,q,q.InterviewerAnswer,"添加面试官回答",(x,t)=>x with{InterviewerAnswer=t});
                else{
                    Inline(body,q,q.OnSiteAnswer,"添加现场回答",(x,t)=>x with{OnSiteAnswer=t});
                    Inline(body,q,q.ReviewAnswer,"添加复盘答案",(x,t)=>x with{ReviewAnswer=t});
                }
                var controls=new WrapPanel();
                controls.Children.Add(Button("添加追问",()=>{var child=NewQuestion("新追问",q.Category,q.Id);draft=draft with{Questions=draft.Questions.Append(child).ToList()};Changed();RenderQuestions();}));
                if(q.ParentQuestionId==null){
                    var select=new ComboBox{ItemsSource=categories.Select(CategoryLabel),SelectedIndex=Array.IndexOf(categories,q.Category),Width=125};
                    select.SelectionChanged+=(_,_)=>{if(select.SelectedIndex>=0){ChangeQuestion(q.Id,x=>x with{Category=categories[select.SelectedIndex]});RenderQuestions();}};controls.Children.Add(select);
                }
                void Move(int direction){
                    var siblings=draft.Questions.Where(x=>x.ParentQuestionId==q.ParentQuestionId).OrderBy(x=>x.SiblingOrder??x.AskedOrder).ToList();int index=siblings.FindIndex(x=>x.Id==q.Id),other=index+direction;if(other<0||other>=siblings.Count)return;
                    (siblings[index],siblings[other])=(siblings[other],siblings[index]);var ranks=siblings.Select((x,i)=>(x.Id,Rank:i+1)).ToDictionary(x=>x.Id,x=>x.Rank);
                    draft=draft with{Questions=draft.Questions.Select(x=>ranks.TryGetValue(x.Id,out int rank)?x with{SiblingOrder=rank}:x).ToList()};Changed();RenderQuestions();
                }
                controls.Children.Add(Button("上移",()=>Move(-1)));controls.Children.Add(Button("下移",()=>Move(1)));
                controls.Children.Add(Button("删除",()=>{
                    if(!Confirm("删除问题","同时删除其追问，保存草稿后同步。","删除","取消"))return;
                    var remove=new HashSet<string>{q.Id};bool added;do{added=false;foreach(var item in draft.Questions)if(item.ParentQuestionId!=null&&remove.Contains(item.ParentQuestionId)&&remove.Add(item.Id))added=true;}while(added);
                    draft=draft with{Questions=draft.Questions.Where(x=>!remove.Contains(x.Id)).ToList()};Changed();RenderQuestions();
                }));body.Children.Add(controls);questionHost.Children.Add(Surface(body,new(16)));
            }
        }
        void AddNew(){
            if(string.IsNullOrWhiteSpace(newQuestion.Text))return;
            var q=NewQuestion(newQuestion.Text.Trim(),categories[Math.Max(0,category.SelectedIndex)]);
            draft=draft with{Questions=draft.Questions.Append(q).ToList()};newQuestion.Clear();Changed();RenderQuestions();
        }
        newQuestion.LostKeyboardFocus+=(_,_)=>AddNew();
        void ApplyParsed(List<QuestionData> rows,bool replace){
            if(replace&&!Confirm("替换问题","将用解析结果替换当前所有问题；确认后仍可校对，再保存。","替换","取消"))return;
            // Import ids are distinct; remap all parent references together.
            var ids=rows.ToDictionary(q=>q.Id,q=>Guid.NewGuid().ToString());
            var imported=rows.Select((q,i)=>q with{Id=ids[q.Id],ExperienceId=draft.Id,ParentQuestionId=q.ParentQuestionId!=null&&ids.TryGetValue(q.ParentQuestionId,out var parent)?parent:null,AskedOrder=(replace?0:draft.Questions.Count)+i+1}).ToList();
            draft=draft with{Questions=replace?imported:draft.Questions.Concat(imported).ToList()};Changed();mode="questions";RenderMode();
        }
        async Task ParseText(string source,StackPanel preview,CancellationToken token)
        {
            try{
                var result=await api.FormAsync<System.Text.Json.JsonElement>($"my-schedule/experiences/{draft.Id}/parse",new(){{"source",source}},token);
                if(token.IsCancellationRequested||Route != PageRoute.NoteEditor||generation!=noteEditorGeneration)return;
                PreviewParsed(result,preview);
            }catch(OperationCanceledException){}
            catch(Exception e)when(WorkspaceRepository.Expected(e)){preview.Children.Clear();preview.Children.Add(Text(WorkspaceRepository.Explain(e),14,"#B42332"));}
        }
        void PreviewParsed(System.Text.Json.JsonElement result,StackPanel preview)
        {
            var rows=ReadParsedQuestions(result,draft.Id);preview.Children.Clear();
            var actions=new WrapPanel();actions.Children.Add(Button("追加到草稿",()=>ApplyParsed(rows,false)));actions.Children.Add(Button("替换草稿",()=>ApplyParsed(rows,true)));preview.Children.Add(actions);
            if(result.TryGetProperty("warnings",out var warnings))foreach(var w in warnings.EnumerateArray())preview.Children.Add(Text(w.GetString()??"",12,"#A85B23"));
            if(result.TryGetProperty("unrecognized",out var unrecognized)&&unrecognized.GetArrayLength()>0)preview.Children.Add(Text("部分原文未归入问题，请核对原文，避免遗漏。",13,"#A85B23"));
            foreach(var row in QuestionTree.Ordered(rows)){var b=new StackPanel{Margin=new(row.Depth*14,0,0,0)};b.Children.Add(Text($"Q{row.Number} {row.Item.QuestionText}",16));if(!string.IsNullOrWhiteSpace(row.Item.OnSiteAnswer))b.Children.Add(new Expander{Header="现场回答",Content=Text(row.Item.OnSiteAnswer)});preview.Children.Add(Surface(b,new(12)));}
            feedback.Text=$"识别 {rows.Count} 道问题 · 尚未加入草稿";
        }
        void RenderMode()
        {
            workspace.Children.Clear();
            foreach(var control in new FrameworkElement[]{newQuestion,category,questionHost})
                if(control.Parent is Panel parent)parent.Children.Remove(control);
            if(mode=="questions"){
                var add=new DockPanel();var addButton=Button("加入草稿",AddNew);DockPanel.SetDock(addButton,Dock.Right);add.Children.Add(addButton);DockPanel.SetDock(category,Dock.Right);add.Children.Add(category);add.Children.Add(newQuestion);workspace.Children.Add(add);
                var feelings=new TextBox{Text=draft.Feeling??"",AcceptsReturn=true,Height=70,TextWrapping=TextWrapping.Wrap};feelings.TextChanged+=(_,_)=>{draft=draft with{Feeling=feelings.Text};Changed();};
                workspace.Children.Add(new Expander{Header="面试感受（展示在标题下）",Content=feelings});
                var minutes=new WrapPanel();foreach(var c in categories){var input=new TextBox{Width=70,Text=draft.CategoryMinutes.TryGetValue(c,out int m)?m.ToString():"",MaxLength=4};minutes.Children.Add(Text(CategoryLabel(c),12,margin:new(8,12,4,0)));minutes.Children.Add(input);input.LostKeyboardFocus+=(_,_)=>{var map=new Dictionary<string,int>(draft.CategoryMinutes);if(string.IsNullOrWhiteSpace(input.Text))map.Remove(c);else if(int.TryParse(input.Text,out int value)&&value is >=0 and <=1440)map[c]=value;else{feedback.Text="用时应为0至1440分钟，可留空。";return;}draft=draft with{CategoryMinutes=map};Changed();};}
                var total=new TextBox{Width=70,Text=draft.TotalMinutes?.ToString()??"",MaxLength=4};minutes.Children.Add(Text("总用时",12,margin:new(8,12,4,0)));minutes.Children.Add(total);total.LostKeyboardFocus+=(_,_)=>{if(string.IsNullOrWhiteSpace(total.Text))draft=draft with{TotalMinutes=null};else if(int.TryParse(total.Text,out int v)&&v is >=0 and <=1440)draft=draft with{TotalMinutes=v};else{feedback.Text="总用时无效。";return;}Changed();};workspace.Children.Add(new Expander{Header="模块用时（分钟，可选）",Content=minutes});
                workspace.Children.Add(questionHost);RenderQuestions();
            }else{
                var panels=new Grid();panels.ColumnDefinitions.Add(new());panels.ColumnDefinitions.Add(new());var left=new StackPanel{Margin=new(0,0,16,0)};var preview=new StackPanel();Grid.SetColumn(preview,1);panels.Children.Add(left);panels.Children.Add(preview);workspace.Children.Add(panels);
                left.Children.Add(Text("支持：1、问题 / 2:问题 / 3 问题；1.1为追问，换行输入回答；支持Markdown标题和列表。",13,"#64748B",new(0,8,0,14)));
                var raw=new TextBox{Text=draft.RawTextDraft,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=Math.Max(300,Viewport.ActualHeight-240),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
                left.Children.Add(raw);
                raw.TextChanged+=(_,_)=>{draft=draft with{RawTextDraft=raw.Text};Changed();parseDelay?.Cancel();parseDelay=new();var token=parseDelay.Token;var source=raw.Text;Run(async()=>{try{await Task.Delay(600,token);if(Repository.Online)await ParseText(source,preview,token);else feedback.Text="原文已保存在本机；联网后可解析。";}catch(OperationCanceledException){}});};
                left.Children.Add(Button("解析预览",()=>Run(()=>ParseText(raw.Text,preview,CancellationToken.None))));
                if(mode=="file")left.Children.Add(Button("导入 Markdown / PDF（5MB）",()=>Run(async()=>{
                    var picker=new Microsoft.Win32.OpenFileDialog{Filter="Markdown/PDF|*.md;*.markdown;*.pdf"};if(picker.ShowDialog(Owner)!=true)return;
                    if(new System.IO.FileInfo(picker.FileName).Length>5*1024*1024){feedback.Text="文件不能超过5MB。";return;}
                    var result=await api.UploadAsync<System.Text.Json.JsonElement>($"my-schedule/experiences/{draft.Id}/parse-file",picker.FileName,"file",null);
                    if(Route != PageRoute.NoteEditor||generation!=noteEditorGeneration)return;
                    if(result.TryGetProperty("source_text",out var sourceText))raw.Text=sourceText.GetString()??"";PreviewParsed(result,preview);
                })));
            }
        }
        toolbar.Children.Add(Button("逐题录入",()=>{mode="questions";RenderMode();}));toolbar.Children.Add(Button("全文录入",()=>{mode="text";RenderMode();}));toolbar.Children.Add(Button("文件导入",()=>{mode="file";RenderMode();}));
        toolbar.Children.Add(publicBox);publicBox.Click+=(_,_)=>{draft=draft with{IsPublic=publicBox.IsChecked==true};Changed();};
        toolbar.Children.Add(Button("保存面经",()=>Run(()=>SaveDraft(true))));toolbar.Children.Add(Button("返回我的面经",()=>Navigate(ShowExperience)));
        RenderMode();FormDirty=false;
    }

    static List<QuestionData> ReadParsedQuestions(System.Text.Json.JsonElement root,string experience)
    {
        var rows=new List<QuestionData>();if(!root.TryGetProperty("questions",out var list))return rows;
        foreach(var q in list.EnumerateArray()){
            string? S(string key)=>q.TryGetProperty(key,out var value)&&value.ValueKind==System.Text.Json.JsonValueKind.String?value.GetString():null;
            int I(string key,int fallback)=>q.TryGetProperty(key,out var value)&&value.TryGetInt32(out int n)?n:fallback;
            rows.Add(new(){Id=S("id")??Guid.NewGuid().ToString(),ExperienceId=experience,Category=S("category")??"other",QuestionText=S("question_text")??"",ParentQuestionId=S("parent_question_id"),AskedOrder=I("asked_order",rows.Count+1),SiblingOrder=I("sibling_order",rows.Count+1),OnSiteAnswer=S("on_site_answer"),ReviewAnswer=S("review_answer"),InterviewerAnswer=S("interviewer_answer")});
        }return rows;
    }
}
