using System.Text;

namespace Shared.Packet.Packets;

[Packet(PacketType.ChatConnect)]
public struct ChatConnectPacket : IPacket {
	public short Size { get; }
	public string Name;
	
	public ChatConnectPacket(string name) {
		Name = name;
		int tmpSize = Encoding.UTF8.GetByteCount(Name);
		if (tmpSize > Int16.MaxValue) {
			throw new OverflowException();
		}
		Size = (short) tmpSize;
	}
	
	public void Serialize(Span<byte> data) {
		Encoding.UTF8.GetBytes(Name, data);
	}

	public void Deserialize(ReadOnlySpan<byte> data) {
		Name = Encoding.UTF8.GetString(data);
	}
}

