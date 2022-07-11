using System.Runtime.InteropServices;

namespace Shared.Packet.Packets;

[Packet(PacketType.ChatVoice)]
public struct ChatVoicePacket : IPacket {
	
	public ulong Timestamp;
	public float Distance;
	public byte[] Audio;

	public short Size { get; }

	public ChatVoicePacket(ulong timestamp, float distance) {
		Timestamp = timestamp;
		Distance = distance;
		Audio = Array.Empty<byte>();
		Size = 12;
	}

	public void Serialize(Span<byte> data) {
		MemoryMarshal.Write(data, ref Timestamp);
		MemoryMarshal.Write(data[8..], ref Distance);
		Audio.CopyTo(data[12..]);
	}

	public void Deserialize(ReadOnlySpan<byte> data) {
		Timestamp = MemoryMarshal.Read<ulong>(data);
		Distance = MemoryMarshal.Read<float>(data[8..]);
		Audio = data[12..].ToArray();
	}
	// Necessary because serialization + deserialization is expensive
	public static void SetDistance(Span<byte> data, float distance) {
		MemoryMarshal.Write(data[8..], ref distance);
	}
}

