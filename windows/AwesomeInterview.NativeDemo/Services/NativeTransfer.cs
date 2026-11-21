using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace AwesomeInterview.NativeDemo.Data;
public sealed partial class NativeApi
{
    public async Task<DocumentPages> DocumentAsync(string path,CancellationToken cancellation=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,path);
        return await SendAsync<DocumentPages>(request,cancellation,180);
    }
    public async Task<T> FormAsync<T>(string path,Dictionary<string,string> fields,CancellationToken cancellation=default)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new FormUrlEncodedContent(fields)};
        request.Headers.Add("X-CSRF-Token",Session?.CsrfToken??throw new ApiFailure(401,"请先登录。"));
        return await SendAsync<T>(request,cancellation,180);
    }
    public async Task<T> UploadAsync<T>(string path,string file,string field,IProgress<double>? progress,CancellationToken cancellation=default)
    {
        using var multipart=new MultipartFormDataContent();
        var content=new UploadContent(file,progress);content.Headers.ContentType=new MediaTypeHeaderValue(Path.GetExtension(file).Equals(".pdf",StringComparison.OrdinalIgnoreCase)?"application/pdf":"text/markdown");
        multipart.Add(content,field,Path.GetFileName(file));
        using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=multipart};
        request.Headers.Add("X-CSRF-Token",Session?.CsrfToken??throw new ApiFailure(401,"请先登录。"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await SendAsync<T>(request,cancellation,180);
    }
    sealed class UploadContent(string file,IProgress<double>? progress):HttpContent
    {
        protected override bool TryComputeLength(out long length){length=new FileInfo(file).Length;return true;}
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>SerializeToStreamAsync(stream,context,CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken cancellation)
        {
            using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
            var buffer=new byte[65536];long sent=0;int count;
            while((count=await input.ReadAsync(buffer,cancellation))>0){await stream.WriteAsync(buffer.AsMemory(0,count),cancellation);sent+=count;progress?.Report(input.Length==0?1:(double)sent/input.Length);}
        }
    }
}
