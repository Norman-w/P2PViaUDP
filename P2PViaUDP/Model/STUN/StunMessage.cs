using System.Net;

namespace P2PViaUDP.Model;

/// <summary>
/// 一条STUN消息,用于客户端和服务器之间的通信
/// </summary>
public partial class StunMessage
{
	public StunMessage(MessageType messageType, MessageSource messageSource, Guid clientId, IPEndPoint clientEndPoint, IPEndPoint serverEndPoint)
	{
		MessageType = messageType;
		MessageSource = messageSource;
		ClientId = clientId;
		ClientEndPoint = clientEndPoint;
		ServerEndPoint = serverEndPoint;
	}
	/// <summary>
	/// 消息类型,不用通过服务端传递,直接由构造函数传递
	/// </summary>
	public MessageType MessageType { get; private set; }
	/// <summary>
	/// 消息来源
	/// </summary>
	public MessageSource MessageSource { get; set; }
	/// <summary>
	/// 客户端ID
	/// </summary>
	public Guid ClientId { get; set; }
	/// <summary>
	/// 客户端终结点
	/// </summary>
	public IPEndPoint ClientEndPoint { get; set; }
	/// <summary>
	/// 服务器终结点
	/// </summary>
	public IPEndPoint ServerEndPoint { get; set; }
	/// <summary>
	/// 发送时间
	/// </summary>
	public DateTime SendTime { get; set; } = DateTime.Now;
	
	public virtual byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(BitConverter.GetBytes((int)MessageSource));
		bytesList.AddRange(ClientId.ToByteArray());
		bytesList.AddRange(ClientEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(ClientEndPoint.Port));
		bytesList.AddRange(ServerEndPoint.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(ServerEndPoint.Port));
		bytesList.AddRange(BitConverter.GetBytes(SendTime.Ticks));
		var bytes = bytesList.ToArray();
		return bytes;
	}

	public static StunMessage FromBytes(byte[] bytes)
	{
		var messageType = (MessageType)BitConverter.ToInt32(bytes, 0);
		var messageSource = (MessageSource)BitConverter.ToInt32(bytes, 4);
		var clientId = new Guid(bytes.Skip(8).Take(16).ToArray());
		var clientEndPoint = new IPEndPoint(new IPAddress(bytes.Skip(24).Take(4).ToArray()), BitConverter.ToInt32(bytes, 28));
		var serverEndPoint = new IPEndPoint(new IPAddress(bytes.Skip(32).Take(4).ToArray()), BitConverter.ToInt32(bytes, 36));
		var sendTime = new DateTime(BitConverter.ToInt64(bytes, 40));
		return new StunMessage(messageType, messageSource, clientId, clientEndPoint, serverEndPoint)
		{
			SendTime = sendTime
		};
	}
}