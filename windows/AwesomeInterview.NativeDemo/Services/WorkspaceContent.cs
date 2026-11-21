using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace AwesomeInterview.NativeDemo.Data;

public record ExperienceEnvelope(ExperienceData Experience, List<QuestionData>? Questions = null)
{
    public ExperienceData Full => Experience with { Questions = Questions ?? Experience.Questions, HasCompleteDraft = true };
}
public record ExperienceSummary(string Id,string Title,string? Company,string? Position,string? InterviewRound,bool IsPublic,DateTimeOffset UpdatedAt,int QuestionCount);
public record ExperienceBatch(List<ExperienceSummary> Items,int Total,int Page,int PageCount);
public record InterviewBatch(List<EventData> Items,int Total,int Page,int PageCount);
public record ResumeInfo(string Id,string FileName,string Version,string PdfUrl);
public record ResumeState(ResumeInfo? Resume);
public record DocumentPages(List<byte[]> Pages,string? Id = null,string? FileName=null,string? Version=null);
public record CachedDocument(string UserId,DateTimeOffset FetchedAt,DocumentPages Document);
public record ParsedNotes(List<QuestionData> Questions, List<string>? Warnings, JsonElement Unrecognized);

public sealed partial class WorkspaceRepository
{
    public async Task PutExperienceAsync(ExperienceData item)
    {
        await syncGate.WaitAsync();
        try {
            if(Current==null||item.OwnerId!=Current.UserId)throw new System.IO.InvalidDataException("面经账号不匹配。");
            int index=Current.Experiences.FindIndex(e=>e.Id==item.Id);
            if(index<0)Current.Experiences.Add(item);else Current.Experiences[index]=item;
            QueuePersist();
        } finally { syncGate.Release(); }
        Changed?.Invoke();
    }
    public async Task RemoveExperienceAsync(string id)
    {
        await syncGate.WaitAsync();
        try {if(Current!=null&&Api!=null){Current.Experiences.RemoveAll(e=>e.Id==id);cache.ForgetEditorDraft(Api.Server.AbsoluteUri,Current.UserId,id);}QueuePersist();}
        finally{syncGate.Release();}Changed?.Invoke();
    }
    public async Task<CachedDocument?> ReadDocumentAsync(bool previous=false)
    {
        var owner=Current?.UserId;var api=Api;if(owner==null||api==null)return null;
        var value=await cache.ReadDocumentAsync(api.Server.AbsoluteUri,owner,previous);
        if(Api!=api||Current?.UserId!=owner||value?.UserId!=owner)return null;
        return value;
    }
    public async Task SaveDocumentAsync(DocumentPages document)
    {
        var owner=Current?.UserId;var api=Api;if(owner==null||api==null)return;
        // Share the persistence tail with logout; a late document cannot recreate a deleted cache.
        await syncGate.WaitAsync();
        try {if(Api==api&&Current?.UserId==owner){await persistTail;await cache.SaveDocumentAsync(api.Server.AbsoluteUri,owner,new(owner,DateTimeOffset.UtcNow,document));}}
        finally{syncGate.Release();}
    }
    public async Task<bool> SaveEditorDraftAsync(ExperienceData item)
    {
        var api=Api;var owner=Current?.UserId;if(api==null||owner!=item.OwnerId)return false;
        await syncGate.WaitAsync();try{if(Api!=api||Current?.UserId!=owner||!Current.Experiences.Any(e=>e.Id==item.Id))return false;await cache.SaveEditorDraftAsync(api.Server.AbsoluteUri,owner,item);return true;}finally{syncGate.Release();}
    }
    public Task<ExperienceData?> ReadEditorDraftAsync(string id) => cache.ReadEditorDraftAsync(Api!.Server.AbsoluteUri,Current!.UserId,id);
}
