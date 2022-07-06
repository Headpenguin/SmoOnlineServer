using System.Text;

namespace Shared.Packet.Packets;

[Packet(PacketType.ChatInit)]
public struct ChatInitPacket : IPacket {
	public short Size { get; }
	public string Name;
	
	public ChatInitPacket(string name) {
		Name = name;
		int tmpSize = Name.Length * 2;
		if (tmpSize > Int16.MaxValue) {
			throw new OverflowException();
		}
		Size = (short) tmpSize;
	}
	
	public void Serialize(Span<byte> data) {
		throw new InvalidOperationException("Attempted to serialize a ChatInitPacket, which is only meant to be serialized by the client program.");
	}

	public void Deserialize(ReadOnlySpan<byte> data) {
		Console.WriteLine(BitConverter.ToString(data.ToArray()));
		Name = Encoding.UTF8.GetString(data);
	}
}

