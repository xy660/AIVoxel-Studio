using System.Buffers.Binary;
using System.Text;

namespace AIVoxelStudio.Core.Mc;

// 最小 NBT 写入器：大端、流式输出，压缩由调用方提供的流负责
public sealed class NbtWriter : IDisposable
{
    public enum TagType : byte
    {
        End = 0, Byte = 1, Short = 2, Int = 3, Long = 4,
        Float = 5, Double = 6, ByteArray = 7, String = 8,
        List = 9, Compound = 10, IntArray = 11, LongArray = 12,
    }

    private readonly Stream _stream;

    public NbtWriter(Stream stream) => _stream = stream;

    public void BeginRootCompound() => BeginTag(TagType.Compound, "");

    /// <summary>写入标签头（类型 + 名称）；compound/list 用，普通标量由 Put* 自动写入。</summary>
    public void BeginCompound(string name) => BeginTag(TagType.Compound, name);

    /// <summary>compound 作为 list 元素：list 元素只写 payload，不带类型字节与名称。</summary>
    public void BeginCompoundValue()
    {
    }

    public void BeginList(string name, TagType elementType, int count)
    {
        BeginTag(TagType.List, name);
        _stream.WriteByte((byte)elementType);
        WriteInt32(count);
    }

    /// <summary>结束当前 compound，或结束 compound 列表元素。</summary>
    public void EndTag() => _stream.WriteByte((byte)TagType.End);

    public void PutByte(string name, byte value)
    {
        BeginTag(TagType.Byte, name);
        _stream.WriteByte(value);
    }

    public void PutShort(string name, short value)
    {
        BeginTag(TagType.Short, name);
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void PutInt(string name, int value)
    {
        BeginTag(TagType.Int, name);
        WriteInt32(value);
    }

    /// <summary>int 作为 list 元素：只写数值。</summary>
    public void PutIntValue(int value) => WriteInt32(value);

    public void PutLong(string name, long value)
    {
        BeginTag(TagType.Long, name);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void PutFloat(string name, float value)
    {
        BeginTag(TagType.Float, name);
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void PutDouble(string name, double value)
    {
        BeginTag(TagType.Double, name);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        _stream.Write(buffer);
    }

    public void PutString(string name, string value)
    {
        BeginTag(TagType.String, name);
        WriteStringValue(value);
    }

    /// <summary>string 作为 list 元素：只写字符串内容。</summary>
    public void PutStringValue(string value)
    {
        value ??= "";
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > ushort.MaxValue) throw new ArgumentException("NBT 字符串过长", nameof(value));

        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)byteCount);
        _stream.Write(length);
        if (byteCount == 0) return;

        var bytes = Encoding.UTF8.GetBytes(value);
        _stream.Write(bytes, 0, bytes.Length);
    }

    public void Dispose() => _stream.Flush();

    private void WriteStringValue(string value) => PutStringValue(value);

    public void PutByteArray(string name, byte[] values)
    {
        BeginTag(TagType.ByteArray, name);
        WriteInt32(values.Length);
        _stream.Write(values, 0, values.Length);
    }

    public void PutIntArray(string name, int[] values)
    {
        BeginTag(TagType.IntArray, name);
        WriteInt32(values.Length);
        foreach (var value in values) WriteInt32(value);
    }

    public void PutLongArray(string name, long[] values)
    {
        BeginTag(TagType.LongArray, name);
        WriteInt32(values.Length);
        Span<byte> buffer = stackalloc byte[8];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
            _stream.Write(buffer);
        }
    }

    private void BeginTag(TagType type, string name)
    {
        _stream.WriteByte((byte)type);
        WriteStringValue(name);
    }

    private void WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        _stream.Write(buffer);
    }
}
