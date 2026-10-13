using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace Translate.Native;
internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CryptProtectData(ref Blob input,string? description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,nint description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    private static byte[] Transform(byte[] bytes,bool encrypt)
    {
        var input=new Blob { Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length) };
        Blob output=default;
        try {
            Marshal.Copy(bytes,0,input.Data,bytes.Length);
            bool success=encrypt?CryptProtectData(ref input,"Translate API credential",0,0,0,1,out output):CryptUnprotectData(ref input,0,0,0,0,1,out output);
            if(!success)throw new Win32Exception(Marshal.GetLastWin32Error());
            var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        } finally {
            if(output.Data!=0)LocalFree(output.Data);
            for(int i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);
            Marshal.FreeHGlobal(input.Data);Array.Clear(bytes);
        }
    }
    internal static string Protect(string text) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(text),true));
    internal static string Unprotect(string encoded) { var data=Transform(Convert.FromBase64String(encoded),false);try{return Encoding.UTF8.GetString(data);}finally{Array.Clear(data);} }
}
