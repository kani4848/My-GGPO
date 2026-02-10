using System;
using System.IO;

public static class NetMessage
{
    // 小さいenumでOK（通信テスト用）
    public enum MsgType : byte
    {
        Start = 1,
        Ready = 2,
        Input = 3,
    }

    public struct OwnerStartMsg
    {
        public uint seed;
        public int inputDelayFrames;
    }

    public struct ReadyMsg
    {
        public int ready;
    }

    public struct InputMsg
    {
        public int frame;
        public byte pressed; // 0 or 1
    }

    public static byte[] PackStart(uint seed, int inputDelayFrames)
    {
        using var ms = new MemoryStream(16);
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)MsgType.Start);
        bw.Write(seed);
        bw.Write(inputDelayFrames);
        return ms.ToArray();
    }

    public static byte[] PackReady()
    {
        using var ms = new MemoryStream(16);
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)MsgType.Ready);
        bw.Write((int)1);
        return ms.ToArray();
    }

    public static byte[] PackInput(int frame, bool pressed)
    {
        using var ms = new MemoryStream(16);
        using var bw = new BinaryWriter(ms);
        bw.Write((byte)MsgType.Input);
        bw.Write(frame);
        bw.Write((byte)(pressed ? 1 : 0));
        return ms.ToArray();
    }

    public static MsgType PeekType(ReadOnlySpan<byte> data)
    {
        if (data.Length <= 0) return 0;
        return (MsgType)data[0];
    }

    public static OwnerStartMsg UnpackStart(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var br = new BinaryReader(ms);

        _ = br.ReadByte(); // type
        return new OwnerStartMsg
        {
            seed = br.ReadUInt32(),
            inputDelayFrames = br.ReadInt32()
        };
    }

    public static ReadyMsg UnpackReady(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var br = new BinaryReader(ms);

        _ = br.ReadByte(); // type
        return new ReadyMsg()
        {
            ready = br.ReadInt32(),
        };
    }


    public static InputMsg UnpackInput(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var br = new BinaryReader(ms);

        _ = br.ReadByte(); // type
        return new InputMsg
        {
            frame = br.ReadInt32(),
            pressed = br.ReadByte()
        };
    }
}
