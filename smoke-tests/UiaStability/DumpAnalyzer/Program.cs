using System.Runtime.InteropServices;
internal static class Program {
 [DllImport("dbgeng.dll",ExactSpelling=true)] static extern int DebugCreate(ref Guid id,out IntPtr value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Open(IntPtr self,[MarshalAs(UnmanagedType.LPStr)]string path);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Log(IntPtr self,[MarshalAs(UnmanagedType.LPStr)]string path,int append);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Wait(IntPtr self,uint flags,uint timeout);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Execute(IntPtr self,uint output,[MarshalAs(UnmanagedType.LPStr)]string command,uint flags);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int None(IntPtr self);
 static T Method<T>(IntPtr value,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value),slot*IntPtr.Size));
 static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
 static int Main(string[] args){
  var clientId=new Guid("27fe5639-8407-4f47-8364-ee118fb08ac8");Check(DebugCreate(ref clientId,out var client));
  var controlId=new Guid("5182e668-105e-416e-ad92-24ef800424ba");Check(Marshal.QueryInterface(client,ref controlId,out var control));
  try{
   Check(Method<Log>(control,8)(control,Path.GetFullPath(args[1]),0));
   Check(Method<Open>(client,19)(client,Path.GetFullPath(args[0])));
   Check(Method<Wait>(control,93)(control,0,30000));
   foreach(var command in File.ReadAllLines(args[2]).Where(x=>!string.IsNullOrWhiteSpace(x))){Console.WriteLine(command);Console.Out.Flush();Check(Method<Execute>(control,66)(control,0,command,0));}
   Check(Method<None>(control,9)(control));return 0;
  }finally{Marshal.Release(control);Marshal.Release(client);}
 }
}
