using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Ras.Backend;
using Ras.Backend.Server;

static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static async Task<BackgroundMapStylesResult> Catalog(HttpClient c, int ms, CancellationToken token = default, bool only = false, string? styleID = null)
{
 var m=typeof(BackgroundMapService).GetMethod("GetStylesAsync", BindingFlags.NonPublic|BindingFlags.Static, null,
  [typeof(BackgroundMapStylesRequest),typeof(HttpClient),typeof(string),typeof(TimeSpan),typeof(CancellationToken)],null)!;
 return await (Task<BackgroundMapStylesResult>)m.Invoke(null,[new BackgroundMapStylesRequest("test-id",only,styleID),c,"https://fake.invalid",TimeSpan.FromMilliseconds(ms),token])!;
}
var sw=Stopwatch.StartNew();
using (var c=new HttpClient(new FakeHandler("slow-preview"))) {
 var result=await Catalog(c,250); Assert(result.Styles.Length==2 && result.Styles.All(x=>x.Warning!=null),"IDs and warnings must survive optional body timeout"); Assert(sw.ElapsedMilliseconds<1800,"body deadline");
}
using (var c=new HttpClient(new FakeHandler("slow-catalog"))) {
 var result=await Catalog(c,200); Assert(result.Styles.Length==0 && result.Warning!=null,"catalog body deadline");
}
using (var c=new HttpClient(new FakeHandler("oversize-stream"))) {
 var result=await Catalog(c,1000); Assert(result.Styles.Length==2 && result.Styles.All(x=>x.Warning!=null && x.Preview==null),"oversized preview rejected");
}
using (var c=new HttpClient(new FakeHandler("slow-preview"))) using (var cts=new CancellationTokenSource(100)) {
 try { await Catalog(c,2000,cts.Token); throw new Exception("caller cancellation lost"); } catch(OperationCanceledException) when(cts.IsCancellationRequested) {}
}
var manyHandler=new FakeHandler("many");
using (var c=new HttpClient(manyHandler)) {
 var result=await Catalog(c,4000); Assert(result.Styles.Length==32 && result.Warning!=null,"style count bound");
 int serializedBytes=JsonSerializer.SerializeToUtf8Bytes(new { id = new string('a',32), result }, BackendJson.Options).Length;
 Assert(serializedBytes < 8*1024*1024,"bounded protocol output"); Console.WriteLine($"Fake maximal response bytes: {serializedBytes}");
 Assert(manyHandler.MaxActive<=4 && manyHandler.MaxActive>1,"preview concurrency bound");
 Assert(result.Styles.Count(x => x.Preview != null)==4 && result.Styles.Count(x => x.Warning != null)==28,"aggregate preview budget");
}
using (var c=new HttpClient(new FakeHandler("oversize"))) {
 var result=await Catalog(c,1000,only:true); Assert(result.Styles.Length==2 && result.Styles.All(x=>x.Preview==null && x.Warning==null),"catalog-only must not fetch preview");
}
using (var c=new HttpClient(new FakeHandler("large"))) {
 var result=await Catalog(c,1000,styleID:"alpha"); Assert(result.Styles.Length==1 && result.Styles[0].Preview?.Length==700*1024,"real-sized selected preview");
 var missing=await Catalog(c,1000,styleID:"absent"); Assert(missing.Styles.Length==0 && missing.Warning!=null,"selected ID must exist in source catalog");
}
Console.WriteLine("PASS fake HTTP stalled catalog/preview body, size limit, caller cancellation, count/total protocol budget and concurrency");
var ctor=typeof(OperationManager).GetConstructor(BindingFlags.NonPublic|BindingFlags.Instance,null,[typeof(Func<Process>)],null)!;
string script=Path.Combine(AppContext.BaseDirectory,"worker.py");
Func<Process> factory=()=> { var p=new Process {StartInfo=new ProcessStartInfo("/usr/bin/python3",script){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}}; p.Start(); return p; };
var manager=(OperationManager)ctor.Invoke([factory]); await manager.StartAsync(default);
using var doc=JsonDocument.Parse("{}");
string Submit(string method) { Assert(manager.TrySubmit(method,doc.RootElement,out var id),"submit "+method); return id; }
async Task<string> State(string id) { var snap=manager.GetSnapshot(id,0)!; var o=JsonSerializer.SerializeToElement(snap.Payload); return o.GetProperty("state").GetString()!; }
async Task Wait(string id,string state,int timeout=2500) { var d=Stopwatch.StartNew(); while(await State(id)!=state) { if(d.ElapsedMilliseconds>timeout)throw new Exception($"{id} did not reach {state}; got {await State(id)}"); await Task.Delay(20); } }
var style=Submit("map.background.styles"); await Wait(style,"running");
var slow=Submit("slow"); await Wait(slow,"running");
var fast=Submit("fast"); await Task.Delay(200); Assert(await State(fast)=="queued","native lane must stay serial");
await Wait(slow,"completed"); await Wait(fast,"completed"); Assert(await State(style)=="running","native lane must run while style worker held");
var q1=Submit("map.background.styles"); var q2=Submit("map.background.styles"); var q3=Submit("map.background.styles"); Assert(!manager.TrySubmit("map.background.styles",doc.RootElement,out _),"style lane bound");
manager.RequestCancel(q1); await Wait(q1,"canceled"); manager.RequestCancel(style); await Wait(style,"canceled");
manager.RequestCancel(q2); manager.RequestCancel(q3); await Wait(q2,"canceled");await Wait(q3,"canceled");
await manager.StopAsync(default); Console.WriteLine("PASS fake-worker protocol: separate bounded style lane, native serialization, queued/running cancellation and shutdown");

sealed class FakeHandler(string mode): HttpMessageHandler {
 private int active; private int maxActive;
 public int MaxActive=>maxActive;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
  bool catalog=request.RequestUri!.AbsolutePath=="/styles";
  if (mode=="many" && !catalog) {
   int count=Interlocked.Increment(ref active); int old;
   do { old=maxActive; if(count<=old)break; } while(Interlocked.CompareExchange(ref maxActive,count,old)!=old);
   try { await Task.Delay(10,ct); } finally { Interlocked.Decrement(ref active); }
  }
  HttpContent content = catalog ? (mode=="slow-catalog" ? new StreamContent(new StallStream()) : new StringContent(mode=="many" ? JsonSerializer.Serialize(Enumerable.Range(0,40).Select(i=>"style"+i)) : JsonSerializer.Serialize(new[]{"alpha","beta"}))) :
   mode=="slow-preview" ? new StreamContent(new StallStream()) : mode=="oversize-stream" ? new StreamContent(new OversizeStream()) : mode=="oversize" ? new ByteArrayContent(new byte[1025*1024]) : (mode=="many" || mode=="large") && request.RequestUri.AbsolutePath.EndsWith("/image") ? new ByteArrayContent(Enumerable.Repeat((byte)0xfb, mode=="many" ? 1024*1024 : 700*1024).ToArray()) : mode=="many" ? new StringContent(new string('\u0001',4096)) : new StringContent("ok");
  return new HttpResponseMessage(HttpStatusCode.OK){Content=content};
 }
}
sealed class StallStream:Stream {
 public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false; public override long Length=>throw new NotSupportedException(); public override long Position {get=>0;set=>throw new NotSupportedException();}
 public override async ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default){await Task.Delay(Timeout.Infinite,ct);return 0;}
 public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException(); public override void Flush(){} public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long x)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
}

sealed class OversizeStream:Stream {
 private int left=1024*1024+1;
 public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false; public override long Length=>throw new NotSupportedException(); public override long Position {get=>0;set=>throw new NotSupportedException();}
 public override ValueTask<int> ReadAsync(Memory<byte> b,CancellationToken ct=default){int n=Math.Min(left,b.Length);left-=n;return ValueTask.FromResult(n);}
 public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Flush(){}public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long x)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
}
