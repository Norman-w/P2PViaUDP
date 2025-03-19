using System.Net;
using TURNServer;

namespace P2PViaUDP.Model.Client;

/// <summary>
/// 客户端到客户端的P2P打洞响应消息
/// </summary>
public partial class Client2ClientP2PHolePunchingResponseMessage
{
	private static MessageType MessageType => MessageType.P2PHolePunchingRequest;
	private static uint DefaultMessageLength => 
	4 + // MessageType
	4 + // ActiveClientEndPoint.Address
	4 + // ActiveClientEndPoint.Port
	4 + // PassiveClientEndPoint.Address
	4 + // PassiveClientEndPoint.Port
	4 + // PassiveClientNATTye
	16 + // ActiveClientId
	16 + // PassiveClientId
	16 + // GroupId
	8; // SendTime
	//= 76
	public required IPEndPoint ActiveClientEndPoint { get; init; }
	public required IPEndPoint PassiveClientEndPoint { get; init; }
	public NATTypeEnum PassiveClientNATTye { get; init; }
	public Guid ActiveClientId { get; init; }
	public Guid PassiveClientId { get; init; }
	public Guid GroupId { get; init; }
	public DateTime SendTime { get; init; }
}
public partial class Client2ClientP2PHolePunchingResponseMessage
{
	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(ActiveClientEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(ActiveClientEndPoint.Port));
		bytesList.AddRange(PassiveClientEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(PassiveClientEndPoint.Port));
		bytesList.AddRange(BitConverter.GetBytes((int)PassiveClientNATTye));
		bytesList.AddRange(ActiveClientId.ToByteArray());
		bytesList.AddRange(PassiveClientId.ToByteArray());
		bytesList.AddRange(GroupId.ToByteArray());
		bytesList.AddRange(BitConverter.GetBytes(SendTime.Ticks));
		var bytes = bytesList.ToArray();
		return bytes;
	}
	public static Client2ClientP2PHolePunchingResponseMessage FromBytes(byte[] receivedBytes)
	{
		if (receivedBytes.Length != DefaultMessageLength)
		{
			throw new ArgumentException($"接收到的字节数组长度不正确，应为{DefaultMessageLength}，实际为{receivedBytes.Length}");
		}

		var messageType = (MessageType)BitConverter.ToInt32(receivedBytes, 0);
		if (messageType != MessageType)
		{
			throw new ArgumentException("读取的消息类型不匹配");
		}

		var activeClientAddress = new IPAddress(receivedBytes.Skip(4).Take(4).ToArray());
		var activeClientPort = BitConverter.ToInt32(receivedBytes, 8);
		var activeClientEndPoint = new IPEndPoint(activeClientAddress, activeClientPort);
		var passiveClientAddress = new IPAddress(receivedBytes.Skip(12).Take(4).ToArray());
		var passiveClientPort = BitConverter.ToInt32(receivedBytes, 16);
		var passiveClientEndPoint = new IPEndPoint(passiveClientAddress, passiveClientPort);
		var passiveClientNATTye = (NATTypeEnum)BitConverter.ToInt32(receivedBytes, 20);
		var activeClientId = new Guid(receivedBytes.Skip(20).Take(16).ToArray());
		var passiveClientId = new Guid(receivedBytes.Skip(36).Take(16).ToArray());
		var groupId = new Guid(receivedBytes.Skip(52).Take(16).ToArray());
		var sendTime = new DateTime(BitConverter.ToInt64(receivedBytes, 68));

		return new Client2ClientP2PHolePunchingResponseMessage
		{
			ActiveClientEndPoint = activeClientEndPoint,
			PassiveClientEndPoint = passiveClientEndPoint,
			PassiveClientNATTye = passiveClientNATTye,
			ActiveClientId = activeClientId,
			PassiveClientId = passiveClientId,
			GroupId = groupId,
			SendTime = sendTime
		};
	}
}