using System.Security.Cryptography;
using System.Text.Json;

// Diagnostic-only, append-only evidence vault. There is deliberately no cleanup API.
internal sealed class EvidenceStore
{
    internal const long ReserveBytes = 2L * 1024 * 1024 * 1024;
    private readonly string root;
    internal EvidenceStore(string directory)
    {
        root=Path.GetFullPath(directory);
        var wer=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CrashDumps"));
        if(root.Equals(wer,StringComparison.OrdinalIgnoreCase)||root.StartsWith(wer+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Evidence vault must be outside the WER rotation directory.");
        for(var p=new DirectoryInfo(root);p is not null;p=p.Parent)
            if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Evidence vault cannot traverse reparse points.");
        Directory.CreateDirectory(root);
        var policy=Path.Combine(root,"retention.json");
        if(!File.Exists(policy))WriteNew(policy,new{Policy="Manual review only; no automatic overwrite, deletion, expiry or pruning",CreatedUtc=DateTimeOffset.UtcNow});
    }
    internal void RequireSpace(long additionalBytes=0)
    {
        var available=new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
        if(additionalBytes<0||available<additionalBytes||available-additionalBytes<ReserveBytes)
            throw new IOException($"Evidence storage needs {additionalBytes} bytes plus {ReserveBytes} reserve; available={available}.");
    }
    internal static string Hash(string file)
    {
        using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    internal object[] ProtectWer()
    {
        var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CrashDumps");
        if(!Directory.Exists(directory))return Array.Empty<object>();
        // Preserve related existing dumps before a new target can affect WER quota.
        return Directory.EnumerateFiles(directory,"*.dmp")
            .Where(p=>new[]{"dotnet.exe.","FormDesigner.exe.","AvaloniaDesigner.VsHost.exe."}.Any(prefix=>Path.GetFileName(p).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))
            .Select(p=>(object)Preserve(p)).ToArray();
    }
    internal sealed record Copy(string Source,string ProtectedPath,long Bytes,string SHA256,bool Reused);
    internal Copy Preserve(string source)
    {
        source=Path.GetFullPath(source);
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Source evidence cannot be a reparse point.");
        // Deny writes/deletion while hashing and copying, including WER rotation.
        using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
        var sha=Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();input.Position=0;
        var destination=Path.Combine(root,sha,"dump.dmp");
        if(File.Exists(destination))
        {
            if(new FileInfo(destination).Length!=input.Length||Hash(destination)!=sha)
                throw new IOException("Existing vault evidence failed verification; no overwrite attempted: "+destination);
            return new(source,destination,input.Length,sha,true);
        }
        RequireSpace(input.Length);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using(var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {input.CopyTo(output);output.Flush(flushToDisk:true);}
        if(Hash(destination)!=sha)throw new IOException("Evidence copy hash mismatch; both files retained.");
        WriteNew(Path.Combine(Path.GetDirectoryName(destination)!,"metadata.json"),new{Source=source,Bytes=input.Length,SHA256=sha,PreservedUtc=DateTimeOffset.UtcNow});
        return new(source,destination,input.Length,sha,false);
    }
    internal static void WriteNew(string path,object value)
    {
        using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        JsonSerializer.Serialize(stream,value,new JsonSerializerOptions{WriteIndented=true});stream.Flush(flushToDisk:true);
    }
    internal static int Checks(string directory)
    {
        directory=Path.GetFullPath(directory);
        if(Directory.Exists(directory))throw new IOException("Checks require a fresh directory.");
        Directory.CreateDirectory(directory);var passed=0;
        void Check(string name,Action action){action();passed++;Console.WriteLine("PASS EVIDENCE_"+name);}
        var original=Path.Combine(directory,"synthetic-copy-fixture.dmp");File.WriteAllBytes(original,Enumerable.Range(0,4096).Select(i=>(byte)i).ToArray());
        var originalHash=Hash(original);var vault=new EvidenceStore(Path.Combine(directory,"vault"));Copy? copy=null;
        Check("VerifiedCopy",()=>{copy=vault.Preserve(original);if(Hash(copy.ProtectedPath)!=originalHash||copy.SHA256!=originalHash)throw new Exception();});
        Check("NoOverwriteOnReuse",()=>{var time=File.GetLastWriteTimeUtc(copy!.ProtectedPath);var again=vault.Preserve(original);if(!again.Reused||File.GetLastWriteTimeUtc(again.ProtectedPath)!=time)throw new Exception();});
        Check("SourceUnchanged",()=>{if(Hash(original)!=originalHash)throw new Exception();});
        Check("MetadataAndRetention",()=>{if(!File.Exists(Path.Combine(directory,"vault","retention.json"))||!File.Exists(Path.Combine(Path.GetDirectoryName(copy!.ProtectedPath)!,"metadata.json")))throw new Exception();});
        Check("LowSpaceFailsClosed",()=>{try{vault.RequireSpace(long.MaxValue);}catch(IOException){return;}throw new Exception();});
        Check("MetadataNeverOverwritten",()=>{var file=Path.Combine(directory,"once.json");WriteNew(file,new{Value=1});try{WriteNew(file,new{Value=2});}catch(IOException){if(File.ReadAllText(file).Contains('1'))return;}throw new Exception();});
        Check("CorruptBackupRetained",()=>{var folder=Path.Combine(directory,"corrupt-vault");var other=new EvidenceStore(folder);var bad=Path.Combine(folder,originalHash,"dump.dmp");Directory.CreateDirectory(Path.GetDirectoryName(bad)!);File.WriteAllText(bad,"owned synthetic corrupt fixture");try{other.Preserve(original);}catch(IOException){if(File.ReadAllText(bad)=="owned synthetic corrupt fixture")return;}throw new Exception();});
        Console.WriteLine($"Evidence checks: {passed}/{passed}");return 0;
    }
}
