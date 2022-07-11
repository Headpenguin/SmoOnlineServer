using System.Runtime.InteropServices;

namespace Shared.Packet.Packets;

[Packet(PacketType.ChatInit)]
public struct ChatInitPacket : IPacket {
	public short Size { get; } = 12;

	public float SilenceDistance;
	public float PeakDistance;
	public uint CurrentFrame;

	public ChatInitPacket(float silenceDistance, float peakDistance, uint currentFrame) {
		SilenceDistance = silenceDistance;
		PeakDistance = peakDistance;
		CurrentFrame = currentFrame;
	}

	public void Serialize(Span<byte> data) {
		MemoryMarshal.Write(data, ref SilenceDistance);
		MemoryMarshal.Write(data[4..], ref PeakDistance);
		MemoryMarshal.Write(data[8..], ref CurrentFrame);
	}

	public void Deserialize(ReadOnlySpan<byte> data) {
		SilenceDistance = MemoryMarshal.Read<float>(data);
		PeakDistance = MemoryMarshal.Read<float>(data[4..]);
		CurrentFrame = MemoryMarshal.Read<uint>(data[8..]);
	}
}

