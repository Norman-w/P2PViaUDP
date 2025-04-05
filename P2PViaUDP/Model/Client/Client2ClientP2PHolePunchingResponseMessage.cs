using System.Net;
using TURNServer;

namespace P2PViaUDP.Model.Client;

/// <summary>
/// 客户端到客户端的P2P打洞响应消息
/// </summary>
public partial class Client2ClientP2PHolePunchingResponseMessage
{
	private static MessageType MessageType => MessageType.P2PHolePunchingResponse;
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
	public required IPEndPoint RequestSenderEndPoint { get; init; }
	public required IPEndPoint RequestReceiverEndPoint { get; init; }
	public NATTypeEnum RequestReceiverNATTye { get; init; }
	public Guid RequestSenderClientId { get; init; }
	public Guid RequestReceiverClientId { get; init; }
	public Guid GroupId { get; init; }
	public DateTime SendTime { get; init; }
}
public partial class Client2ClientP2PHolePunchingResponseMessage
{
	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(RequestSenderEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(RequestSenderEndPoint.Port));
		bytesList.AddRange(RequestReceiverEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(RequestReceiverEndPoint.Port));
		bytesList.AddRange(BitConverter.GetBytes((int)RequestReceiverNATTye));
		bytesList.AddRange(RequestSenderClientId.ToByteArray());
		bytesList.AddRange(RequestReceiverClientId.ToByteArray());
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
		var activeClientId = new Guid(receivedBytes.Skip(24).Take(16).ToArray());
		var passiveClientId = new Guid(receivedBytes.Skip(40).Take(16).ToArray());
		var groupId = new Guid(receivedBytes.Skip(56).Take(16).ToArray());
		var sendTime = new DateTime(BitConverter.ToInt64(receivedBytes, 72));

		return new Client2ClientP2PHolePunchingResponseMessage
		{
			RequestSenderEndPoint = activeClientEndPoint,
			RequestReceiverEndPoint = passiveClientEndPoint,
			RequestReceiverNATTye = passiveClientNATTye,
			RequestSenderClientId = activeClientId,
			RequestReceiverClientId = passiveClientId,
			GroupId = groupId,
			SendTime = sendTime
		};
	}
}