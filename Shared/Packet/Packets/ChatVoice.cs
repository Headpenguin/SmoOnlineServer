using System.Runtime.InteropServices;

namespace Shared.Packet.Packets;

[Packet(PacketType.ChatVoice)]
public struct ChatVoicePacket : IPacket {
	
	public float Timestamp;
	public float Distance;
	public byte[] Audio;

	public short Size { get; }

	public ChatVoicePacket(float timestamp, float distance) {
		Timestamp = timestamp;
		Distance = distance;
		Audio = Array.Empty<byte>();
		Size = 8;
	}

	public void Serialize(Span<byte> data) {
		MemoryMarshal.Write(data, ref Timestamp);
		MemoryMarshal.Write(data[4..], ref Distance);
		Audio.CopyTo(data[8..]);
	}

	public void Deserialize(ReadOnlySpan<byte> data) {
		Timestamp = MemoryMarshal.Read<float>(data);
		Distance = MemoryMarshal.Read<float>(data[4..]);
		Audio = data[8..].ToArray();
	}
	// Necessary because serialization + deserialization is expensive
	public static void SetDistance(Span<byte> data, float distance) {
		MemoryMarshal.Write(data[4..], ref distance);
	}
}

