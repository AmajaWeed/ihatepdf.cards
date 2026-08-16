using System.IO.Compression;
using System.Text;

namespace iHateCards.Pdf;

internal static class PdfUtil
{
    public static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    public static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    public static string Num(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    public static string Num2(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Побайтовый писатель PDF-объектов с xref (общая часть обоих PDF-билдеров).</summary>
internal sealed class PdfObjectWriter
{
    private readonly MemoryStream _out = new();
    private readonly Dictionary<int, long> _offsets = new();
    private int _maxNum;

    public PdfObjectWriter()
    {
        Write(PdfUtil.Latin1("%PDF-1.4\n"));
        Write(new byte[] { 0x25, 0xe2, 0xe3, 0xcf, 0xd3, 0x0a }); // %âãÏÓ
    }

    public void Write(byte[] data) => _out.Write(data, 0, data.Length);

    public void WriteObj(int num, byte[] body)
    {
        _offsets[num] = _out.Position;
        _maxNum = Math.Max(_maxNum, num);
        Write(PdfUtil.Latin1($"{num} 0 obj\n"));
        Write(body);
        Write(PdfUtil.Latin1("\nendobj\n"));
    }

    public void WriteObj(int num, string body) => WriteObj(num, PdfUtil.Latin1(body));

    /// <summary>Байты, записанные к текущему моменту (для /ID как в оригинале —
    /// md5 после записи xref, до trailer).</summary>
    public byte[] CurrentBytes() => _out.ToArray();

    /// <summary>Пишет таблицу xref, возвращает её позицию для startxref.</summary>
    public long WriteXref()
    {
        long xrefPos = _out.Position;
        Write(PdfUtil.Latin1($"xref\n0 {_maxNum + 1}\n"));
        Write(PdfUtil.Latin1("0000000000 65535 f \n"));
        for (int n = 1; n <= _maxNum; n++)
            Write(PdfUtil.Latin1($"{_offsets[n]:0000000000} 00000 n \n"));
        return xrefPos;
    }

    public byte[] FinishWithTrailer(string trailerDict, long xrefPos)
    {
        Write(PdfUtil.Latin1($"trailer\n{trailerDict}\nstartxref\n{xrefPos}\n%%EOF"));
        return _out.ToArray();
    }

    public int MaxNum => _maxNum;
}
