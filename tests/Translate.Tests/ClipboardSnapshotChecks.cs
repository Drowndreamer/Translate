using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Translate.Native;
using DataObject=System.Windows.DataObject;
namespace Translate.Tests;
internal static partial class Program
{
    private static void ClipboardSnapshotChecks()
    {
        Check(SelectionReader.SnapshotDataObject(null)==null,"empty clipboard data has no snapshot");

        const int width=3840,height=2160,stride=width*4;
        var pixels=new byte[stride*height];
        pixels[0]=37;pixels[1]=73;pixels[2]=109;pixels[3]=255;
        pixels[pixels.Length/2]=131;pixels[^4]=181;pixels[^1]=255;
        var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,stride);
        bitmap.Freeze();
        var pngBytes=new byte[1024*1024];
        pngBytes[0]=137;pngBytes[1]=80;pngBytes[2]=78;pngBytes[3]=71;pngBytes[^1]=29;
        var dibBytes=new byte[1024*1024];
        dibBytes[0]=40;dibBytes[1]=17;dibBytes[^1]=61;
        using var png=new MemoryStream(pngBytes);
        using var dib=new MemoryStream(dibBytes);
        png.Position=11;dib.Position=17;
        var attachment=new byte[]{3,5,7,11};
        var source=new DataObject();
        source.SetData(DataFormats.Bitmap,bitmap,false);
        // These raw stream payloads model separately advertised screenshot formats.
        // The snapshot must preserve them without decoding or re-encoding them.
        source.SetData("PNG",png,false);
        source.SetData(DataFormats.Dib,dib,false);
        source.SetData("TranslateTest.Bytes",attachment,false);
        source.SetData(DataFormats.UnicodeText,"screenshot description",false);

        Check(ClipboardSnapshotRejected(source,16L*1024*1024),"4K screenshot reproduces the former 16 MiB snapshot rejection");
        Check(png.Position==11&&dib.Position==17&&png.CanRead&&dib.CanRead,"rejected screenshot snapshot leaves source streams untouched");
        DataObject? screenshot=null;
        try {
            screenshot=SelectionReader.SnapshotDataObject(source);
            Check(screenshot!=null,"4K screenshot and multiple formats fit the default snapshot budget");
            var saved=screenshot!;
            Check(source.GetFormats(false).All(format=>saved.GetDataPresent(format,false)),"4K snapshot retains every explicit image, stream, byte and text format");
            var savedBitmap=saved.GetData(DataFormats.Bitmap,false) as BitmapSource
                ?? throw new Exception("FAIL: bitmap format is missing from the snapshot");
            Check(!ReferenceEquals(savedBitmap,bitmap)&&savedBitmap.PixelWidth==width&&savedBitmap.PixelHeight==height&&savedBitmap.IsFrozen,"4K bitmap snapshot is a separate frozen image with matching dimensions");
            var row=new byte[stride];
            foreach(var y in new[]{0,height/2,height-1}) {
                savedBitmap.CopyPixels(new Int32Rect(0,y,width,1),row,stride,0);
                Check(row.AsSpan().SequenceEqual(pixels.AsSpan(y*stride,stride)),"4K bitmap snapshot preserves pixel row "+y);
            }
            var savedPng=saved.GetData("PNG",false) as MemoryStream
                ?? throw new Exception("FAIL: PNG stream is missing from the snapshot");
            var savedDib=saved.GetData(DataFormats.Dib,false) as MemoryStream
                ?? throw new Exception("FAIL: DIB stream is missing from the snapshot");
            var savedBytes=saved.GetData("TranslateTest.Bytes",false) as byte[]
                ?? throw new Exception("FAIL: byte-array format is missing from the snapshot");
            Check(!ReferenceEquals(savedPng,png)&&!ReferenceEquals(savedDib,dib),"screenshot stream formats have independently owned clones");
            Check(savedPng.ToArray().AsSpan().SequenceEqual(pngBytes)&&savedDib.ToArray().AsSpan().SequenceEqual(dibBytes),"screenshot stream formats retain their complete original bytes");
            Check(png.Position==11&&dib.Position==17,"snapshot copying preserves original stream positions");
            Check(!ReferenceEquals(savedBytes,attachment)&&savedBytes.AsSpan().SequenceEqual(attachment),"byte-array clipboard format is independently cloned");
            Check((string?)saved.GetData(DataFormats.UnicodeText,false)=="screenshot description","image snapshot retains accompanying text");
            pngBytes[0]=1;dibBytes[0]=2;attachment[0]=13;
            Check(savedPng.ToArray()[0]==137&&savedDib.ToArray()[0]==40&&savedBytes[0]==3,"mutating source buffers cannot alter the snapshot");
            SelectionReader.DisposeSnapshot(saved);
            screenshot=null;
            Check(!savedPng.CanRead&&!savedDib.CanRead,"disposing a snapshot releases its owned stream clones");
            Check(png.CanRead&&dib.CanRead&&png.ReadByte()==pngBytes[11]&&dib.ReadByte()==dibBytes[17],"disposing a snapshot keeps the original streams usable");
        } finally {SelectionReader.DisposeSnapshot(screenshot);}

        var boundary=new DataObject();
        var boundaryBytes=new byte[]{19,23,31,43};
        using var boundaryStream=new MemoryStream(new byte[]{47,53,59,61});
        boundaryStream.Position=2;
        boundary.SetData("TranslateTest.BoundaryBytes",boundaryBytes,false);
        boundary.SetData("TranslateTest.BoundaryStream",boundaryStream,false);
        DataObject? exact=null;
        try {
            exact=SelectionReader.SnapshotDataObject(boundary,8);
            Check(exact!=null&&exact.GetDataPresent("TranslateTest.BoundaryStream",false),"snapshot accepts payloads exactly at the total byte budget");
        } finally {SelectionReader.DisposeSnapshot(exact);}
        Check(ClipboardSnapshotRejected(boundary,7),"snapshot rejects a multi-format payload one byte above the total budget");
        Check(ReferenceEquals(boundary.GetData("TranslateTest.BoundaryBytes",false),boundaryBytes)&&ReferenceEquals(boundary.GetData("TranslateTest.BoundaryStream",false),boundaryStream)&&boundaryStream.Position==2&&boundaryStream.CanRead,"budget rejection preserves original data references, stream ownership and position");

        using var oversizedStream=new SnapshotLengthStream(SelectionReader.SnapshotBytes+1);
        var oversized=new DataObject();
        oversized.SetData("TranslateTest.OversizedStream",oversizedStream,false);
        Check(ClipboardSnapshotRejected(oversized,SelectionReader.SnapshotBytes)&&oversizedStream.CanRead,"snapshot rejects a stream above the production budget before allocating its clone");

        var unknown=new DataObject();
        var unsupported=new object();
        unknown.SetData("TranslateTest.ValidStream",boundaryStream,false);
        unknown.SetData("TranslateTest.Unsupported",unsupported,false);
        Check(ClipboardSnapshotRejected(unknown,1024),"snapshot rejects an unsupported format without overwriting its source");
        Check(ReferenceEquals(unknown.GetData("TranslateTest.Unsupported",false),unsupported)&&boundaryStream.Position==2&&boundaryStream.CanRead,"unsupported-format rejection keeps original objects and streams usable");

        var formats=new DataObject();
        for(int i=0;i<65;i++)formats.SetData("TranslateTest.Format"+i,i,false);
        Check(ClipboardSnapshotRejected(formats,1024)&&formats.GetFormats(false).Length==65,"snapshot format-count limit leaves complex source data intact");
    }

    private static bool ClipboardSnapshotRejected(System.Windows.IDataObject source,long maximumBytes)
    {
        DataObject? snapshot=null;
        try {snapshot=SelectionReader.SnapshotDataObject(source,maximumBytes);return false;}
        catch(InvalidOperationException){return true;}
        finally {SelectionReader.DisposeSnapshot(snapshot);}
    }

    private sealed class SnapshotLengthStream(long length) : MemoryStream
    {
        public override long Length=>length;
    }
}
