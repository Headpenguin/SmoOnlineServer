using System.Runtime.InteropServices;

namespace Shared.Packet.Packets;

[Packet(PacketType.ChatVoice)]
public struct ChatVoicePacket : IPacket {
	
	public uint Frame;
	public float Distance;
	public byte[] Audio;

	public short Size { get; }

	public ChatVoicePacket(uint frame, float distance) {
		Frame = frame;
		Distance = distance;
		Audio = Array.Empty<byte>();
		Size = 8;
	}

	public void Serialize(Span<byte> data) {
		MemoryMarshal.Write(data, ref Frame);
		MemoryMarshal.Write(data[4..], ref Distance);
		Audio.CopyTo(data[8..]);
	}

	public void Deserialize(ReadOnlySpan<byte> data) {
		Frame = MemoryMarshal.Read<uint>(data);
		Distance = MemoryMarshal.Read<float>(data[4..]);
		Audio = data[8..].ToArray();
	}
	
	// Necessary because serialization + deserialization is expensive
	public static void SetDistance(Span<byte> data, float distance) {
		MemoryMarshal.Write(data[4..], ref distance);
	}
	// Same as above
	public static uint GetFrame(Span<byte> data) {
		return MemoryMarshal.Read<uint>(data[..4]);
	}
}

