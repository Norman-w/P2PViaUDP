using System.Net;

namespace P2PViaUDP.Model.TURN;

public class TURNBroadcastMessage
{
	private static MessageType MessageType { get; } = MessageType.TURNBroadcast;

	private static uint DefaultMessageLength =>
		4 + // MessageType
		16 + // Guid
		4 + 4 + // EndPoint
		16; // GroupGuid
	
	/// <summary>
	/// 客户端的Guid
	/// </summary>
	public Guid Guid { get; init; }
	/// <summary>
	/// 客户端在TURN服务器中的公网信息
	/// </summary>
	public required IPEndPoint ClientSideEndPointToTURN { get; init; }
	
	/// <summary>
	/// 要加入的组Guid
	/// </summary>
	public Guid GroupGuid { get; init; }
	
	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(Guid.ToByteArray());
		bytesList.AddRange(ClientSideEndPointToTURN.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(ClientSideEndPointToTURN.Port));
		bytesList.AddRange(GroupGuid.ToByteArray());
		
		var bytes = bytesList.ToArray();
		return bytes;
	}
	public static TURNBroadcastMessage FromBytes(byte[] receivedBytes)
	{
		if (receivedBytes.Length < DefaultMessageLength)
		{
			throw new ArgumentException("接收到的新人加入的广播字节数组长度不正确");
		}
		var messageType = (MessageType)BitConverter.ToInt32(receivedBytes, 0);
		if (messageType != MessageType.TURNBroadcast)
		{
			throw new ArgumentException("接收到的新人加入的广播消息类型不正确");
		}
		var guid = new Guid(receivedBytes.Skip(4).Take(16).ToArray());
		var address = new IPAddress(receivedBytes.Skip(20).Take(4).ToArray());
		var port = BitConverter.ToInt32(receivedBytes, 24);
		var endPoint = new IPEndPoint(address, port);
		var groupGuid = new Guid(receivedBytes.Skip(28).Take(16).ToArray());
		
		return new TURNBroadcastMessage
		{
			Guid = guid,
			ClientSideEndPointToTURN = endPoint,
			GroupGuid = groupGuid,
		};
	}
}