using System.Runtime.InteropServices;

namespace Shared.Packet.Packets;

[Packet(PacketType.ChatInit)]
public struct ChatInitPacket : IPacket {
	public short Size { get; } = 8;

	public float SilenceDistance;
	public float PeakDistance;

	public ChatInitPacket(float silenceDistance, float peakDistance) {
		SilenceDistance = silenceDistance;
		PeakDistance = peakDistance;
	}

	public void Serialize(Span<byte> data) {
		MemoryMarshal.Write(data, ref SilenceDistance);
		MemoryMarshal.Write(data[4..], ref PeakDistance);
	}

	public void Deserialize(ReadOnlySpan<byte> data) {
		SilenceDistance = MemoryMarshal.Read<float>(data);
		PeakDistance = MemoryMarshal.Read<float>(data);
	}
}

