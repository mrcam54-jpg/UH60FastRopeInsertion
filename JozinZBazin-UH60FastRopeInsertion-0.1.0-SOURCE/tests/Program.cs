using UH60FastRopeInsertion;
using UnityEngine;

int checks=0;
void Check(bool value,string name) { checks++; if(!value)throw new Exception(name); }
void Reject<T>(Action action,string name) where T:Exception
{ bool rejected=false; try{action();}catch(T){rejected=true;} Check(rejected,name); }
const string prefab="assets/content/firesupport/uh-60 black hawk/sikorsky uh-60 black hawk.prefab";
string temporary=Path.Combine(Path.GetTempPath(),"uh60-publication-contract-"+Guid.NewGuid().ToString("N"));
string bundlePath=Path.Combine(temporary,"Tylevo.TacticalServicesControl","assets","content","vehicles","uh60_blackhawk.bundle");
try
{
    AssetBundle.Reset();
    Reject<FileNotFoundException>(()=>InstalledFlightAsset.Open(temporary,prefab),"missing dependency fails explicitly");
    Check(AssetBundle.Loads==0,"missing dependency does not call Unity");
    Directory.CreateDirectory(Path.GetDirectoryName(bundlePath));
    File.WriteAllText(bundlePath,"own synthetic fixture, not a game bundle");
    var shared=new AssetBundle();shared.Assets.Add(prefab);AssetBundle.Loaded.Add(shared);
    var lease=InstalledFlightAsset.Open(temporary,prefab);
    Check(ReferenceEquals(lease.Bundle,shared),"reuse existing loaded asset");
    Check(AssetBundle.Loads==0,"do not double-load same Unity bundle");
    lease.Dispose();lease.Dispose();
    Check(shared.Unloads==0,"borrowed bundle never unloaded");Check(lease.Bundle==null,"disposed lease cleared");
    AssetBundle.Reset();var owned=new AssetBundle();owned.Assets.Add(prefab);AssetBundle.Next=owned;
    lease=InstalledFlightAsset.Open(temporary,prefab);
    Check(ReferenceEquals(lease.Bundle,owned),"read supported local bundle");
    Check(AssetBundle.Loads==1&&AssetBundle.LastPath==bundlePath,"only expected dependency path read");
    var borrower=InstalledFlightAsset.Open(temporary,prefab);
    Check(AssetBundle.Loads==1,"second lookup reuses handle");borrower.Dispose();
    Check(owned.Unloads==0,"borrower preserves owner");lease.Dispose();lease.Dispose();
    Check(owned.Unloads==1,"owned handle released exactly once");
    AssetBundle.Reset();
    Reject<InvalidOperationException>(()=>InstalledFlightAsset.Open(temporary,prefab),"Unity null load rejected");
    AssetBundle.Reset();AssetBundle.LoadError=new IOException("synthetic locked file");
    Reject<IOException>(()=>InstalledFlightAsset.Open(temporary,prefab),"IO error propagated for scene recovery");
    AssetBundle.Reset();var invalid=new AssetBundle();AssetBundle.Next=invalid;
    Reject<InvalidOperationException>(()=>InstalledFlightAsset.Open(temporary,prefab),"wrong prefab rejected");
    Check(invalid.Unloads==1,"unsupported own load cleaned up");
    AssetBundle.Reset();var foreign=new AssetBundle();foreign.Assets.Add("unrelated");AssetBundle.Loaded.Add(foreign);
    owned=new AssetBundle();owned.Assets.Add(prefab);AssetBundle.Next=owned;
    lease=InstalledFlightAsset.Open(temporary,prefab);lease.Dispose();
    Check(foreign.Unloads==0&&AssetBundle.Loads==1,"unrelated bundle not changed");
}
finally
{
    // Delete only the exact, newly-created synthetic fixture (never a game/workspace tree).
    if(File.Exists(bundlePath))File.Delete(bundlePath);
    string directory=Path.GetDirectoryName(bundlePath);
    while(directory!=null&&Path.GetFullPath(directory).StartsWith(Path.GetFullPath(temporary),StringComparison.OrdinalIgnoreCase))
    { if(Directory.Exists(directory))Directory.Delete(directory,false);if(directory==temporary)break;directory=Path.GetDirectoryName(directory); }
    AssetBundle.Reset();
}
if(args.Length!=1)throw new Exception("Pass the packaged cabin CSV path.");
var clip=CabinClipData.Read(args[0]);
Check(clip.Roles.Count==5,"all five authored roles packaged");
Check(clip.FrameCount==121&&clip.Fps==30&&clip.Loop,"accepted animation metadata");
foreach(var role in clip.Roles)
    foreach(var track in role.Value.Values)
        foreach(float seconds in new[]{0f,.123f,1f,2.25f,4f,12f})
        {
            var pose=clip.Sample(track,seconds);
            Check(!float.IsNaN(pose.Position.x)&&!float.IsNaN(pose.Rotation.w),"finite production sample");
        }
Reject<InvalidDataException>(()=>CabinClipData.Parse("bad header"),"invalid CSV rejected");
Console.WriteLine("PASS publication contracts: "+checks+" checks. Synthetic bundle API and production CSV parser/sampler; NOT a Unity/game test.");
