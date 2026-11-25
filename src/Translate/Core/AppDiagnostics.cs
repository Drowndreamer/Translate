using System.Diagnostics;
using System.Text;
namespace Translate.Core;
internal static class AppDiagnostics
{
    private static readonly object Sync=new();
    private const long MaximumBytes=256*1024;
    // Never record Exception.Message/ToString, request bodies, URLs, or credentials.
    // Stack entries contain method names only, with no arguments or local values.
    internal static void Record(string component,Exception error,string? directory=null)
    {
        try {
            var text=new StringBuilder();
            text.Append(DateTimeOffset.Now.ToString("O")).Append(' ').Append(new string(component.Where(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '.').Take(64).ToArray()));
            text.Append(" version=").Append(typeof(AppDiagnostics).Assembly.GetName().Version).Append(" runtime=").Append(Environment.Version).AppendLine();
            for(int depth=0;error!=null&&depth<4;depth++,error=error.InnerException!) {
                text.AppendLine(error.GetType().FullName);
                foreach(var frame in (new StackTrace(error,false).GetFrames()??Array.Empty<StackFrame>()).Take(24)) {
                    var method=frame.GetMethod();text.Append("  ").Append(method?.DeclaringType?.FullName).Append('.').AppendLine(method?.Name);
                }
            }
            lock(Sync) {
                var folder=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Translate","diagnostics");
                Directory.CreateDirectory(folder);var path=Path.Combine(folder,"errors.log");
                if(File.Exists(path)&&new FileInfo(path).Length+Encoding.UTF8.GetByteCount(text.ToString())>MaximumBytes)File.Move(path,Path.Combine(folder,"errors.previous.log"),true);
                File.AppendAllText(path,text.ToString(),new UTF8Encoding(false));
            }
        } catch(Exception) {} // Diagnostics must not cause a second failure during shutdown.
    }
}
