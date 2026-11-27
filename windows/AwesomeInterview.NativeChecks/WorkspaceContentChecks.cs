using AwesomeInterview.NativeDemo.Data;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

static class WorkspaceContentChecks
{
    public static async Task Run(Action<bool,string> check)
    {
        const string server="https://fixture.test/";
        var dir=Path.Combine(Path.GetTempPath(),"awesome-content-"+Guid.NewGuid().ToString("N"));var cache=new AccountCache(dir);
        var id=Guid.NewGuid().ToString();var note=new ExperienceData{Id=id,OwnerId="a",Title="Synthetic note",RawTextDraft="private source",HasCompleteDraft=true};
        try{
            check(NativeApi.Node("api/mobile/v1/workspace/resume","POST")=="resume_upload"&&NativeApi.Node("api/mobile/v1/workspace/resume/document?private=value")=="resume_file","resume monitor separates metadata upload and document work");
            check(NativeApi.Node("my-schedule/experiences/private-id/parse-file")=="parse"&&NativeApi.Node("api/mobile/v1/workspace/experiences/private-id/draft")=="draft","parser and draft metrics use fixed nodes without private IDs");
            var first=new CachedDocument("a",DateTimeOffset.UtcNow,new(new(){new byte[]{1,2,3}},"one","fixture.pdf","v1"));
            await cache.SaveDocumentAsync(server,"a",first);await cache.SaveEditorDraftAsync(server,"a",note);
            check((await cache.ReadDocumentAsync(server,"a"))?.Document.Pages[0].SequenceEqual(new byte[]{1,2,3})==true,"document page bytes survive encrypted cache");
            check(await cache.ReadDocumentAsync(server,"b")==null&&await cache.ReadDocumentAsync("https://different.test/","a")==null,"resume cache separates server and owner");
            check((await cache.ReadEditorDraftAsync(server,"a",id))?.RawTextDraft=="private source"&&await cache.ReadEditorDraftAsync(server,"b",id)==null,"note source and draft isolated by account");
            check(Directory.GetFiles(dir).All(file=>!Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("private source")),"local note source is never plaintext");
            await cache.SaveDocumentAsync(server,"a",first with{Document=first.Document with{Version="v2"}});
            check((await cache.ReadDocumentAsync(server,"a",true))?.Document.Version=="v1","replacing resume retains one previous successful version");
            await cache.SaveDocumentAsync(server,"a",first with{Document=first.Document with{Version="v2"}});
            check((await cache.ReadDocumentAsync(server,"a",true))?.Document.Version=="v1","refreshing unchanged resume does not evict previous version");
            cache.Forget(server,"a");
            check(Directory.GetFiles(dir).Length==0,"logout clears resume versions and per-note private drafts");
            var envelope=Wire.Read<ExperienceEnvelope>(JsonSerializer.Serialize(new{experience=note,questions=new[]{new QuestionData{Id="q",ExperienceId=id,Category="project",QuestionText="Fixture question"}}},Wire.Json));
            check(envelope.Full.HasCompleteDraft&&envelope.Full.Questions.Count==1&&envelope.Full.RawTextDraft=="private source","full note details combine questions and mark complete parsing source");
            var snapshotNote=Wire.Read<ExperienceData>($"{{\"id\":\"{id}\",\"owner_id\":\"a\",\"title\":\"fixture\"}}");
            check(!snapshotNote.HasCompleteDraft,"summary-only cached note cannot silently erase full parsing source");
            var file=Path.Combine(dir,"generated.md");await File.WriteAllTextAsync(file,"# Synthetic import\n1、A question");
            bool form=false,upload=false;
            using(var api=new NativeApi(server,new ContentTransport(async request=>{
                if(request.RequestUri!.AbsolutePath.EndsWith("/session"))return Json(new SessionInfo("a","fixture-csrf"));
                check(request.Headers.GetValues("X-CSRF-Token").Single()=="fixture-csrf","native import request carries session CSRF");
                var body=await request.Content!.ReadAsStringAsync();
                if(request.RequestUri.AbsolutePath.EndsWith("parse-file")){upload=body.Contains("Synthetic import")&&body.Contains("generated.md")&&request.Headers.Accept.Any(a=>a.MediaType=="application/json");}
                else form=request.Content.Headers.ContentType?.MediaType=="application/x-www-form-urlencoded"&&body.Contains("source=");
                return Json(new{questions=Array.Empty<object>(),warnings=Array.Empty<string>()});
            }))){await api.ConnectAsync();await api.FormAsync<JsonElement>("my-schedule/experiences/fixture/parse",new(){{"source","1、Question"}});await api.UploadAsync<JsonElement>("my-schedule/experiences/fixture/parse-file",file,"file",null);}
            check(form&&upload,"text and multipart imports use native authenticated requests");
            using(var api=new NativeApi(server,new ContentTransport(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict){Content=new StringContent("{\"detail\":\"草稿版本已变化\"}",Encoding.UTF8,"application/json")})))){
                try{await api.GetAsync<JsonElement>("fixture");throw new Exception("conflict accepted");}catch(ApiFailure e){check(e.Status==409&&e.Message=="草稿版本已变化","note version errors remain actionable rather than overlap warnings");}
            }
        }finally{foreach(var file in Directory.Exists(dir)?Directory.GetFiles(dir):Array.Empty<string>())File.Delete(file);if(Directory.Exists(dir))Directory.Delete(dir);}
    }
    static HttpResponseMessage Json(object value)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(value,Wire.Json),Encoding.UTF8,"application/json")};
    sealed class ContentTransport(Func<HttpRequestMessage,Task<HttpResponseMessage>> handler):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>handler(request);}
}
