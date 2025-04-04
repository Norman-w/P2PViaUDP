using System.Net;

namespace P2PViaUDP.Model.Client;

public class Client2ClientIamReadyToReceiveYourHolePunchingMessage
{
	public static MessageType MessageType => MessageType.Client2ClientIamReadyToReceiveYourHolePunching;

	public static int DefaultMessageLength =>
		4 + // MessageType
		16 + // Guid : MessageId
		16 + // Guid : SenderId
		8 + // DateTime : SendTime
		4 + // Sender endpoint from stun when sending this message ip
		4 + // Sender endpoint from stun when sending this message port
		4 + // Sender endpoint from receiver when receive this message ip
		4; // Sender endpoint from receiver when receive this message port

	//= 64
	public Client2ClientIamReadyToReceiveYourHolePunchingMessage(Guid senderId,
		IPEndPoint senderEndPointFromStunWhenSendingThisMessage)
	{
		Id = Guid.NewGuid();
		SenderId = senderId;
		SendTime = DateTime.Now;
		SenderEndPointFromStunWhenSendingThisMessage = senderEndPointFromStunWhenSendingThisMessage;
	}

	public Guid Id { get; init; }
	public Guid SenderId { get; init; }
	public DateTime SendTime { get; init; }

	/// <summary>
	/// 发送者发送消息时,他认为自己的公网IP和端口
	/// </summary>
	public IPEndPoint SenderEndPointFromStunWhenSendingThisMessage { get; init; }

	/// <summary>
	/// 接收者接受到消息时,该消息的接收者发现的发送者的实际公网IP和端口, 当接收者受到的时候才会设置这个信息
	/// </summary>
	public IPEndPoint? SenderEndPointFromReceiverWhenReceiveThisMessage { get; set; }

	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(Id.ToByteArray());
		bytesList.AddRange(SenderId.ToByteArray());
		bytesList.AddRange(BitConverter.GetBytes(SendTime.Ticks));
		bytesList.AddRange(SenderEndPointFromStunWhenSendingThisMessage.Address.GetAddressBytes());
		bytesList.AddRange(BitConverter.GetBytes(SenderEndPointFromStunWhenSendingThisMessage.Port));
		return bytesList.ToArray();
	}

	public static Client2ClientIamReadyToReceiveYourHolePunchingMessage FromBytes(byte[] receivedBytes)
	{
		if (receivedBytes.Length < DefaultMessageLength)
		{
			throw new ArgumentException("接收到的 已准备好对方连接我(抛送橄榄枝) 消息字节数组长度不正确");
		}

		var messageType = (MessageType)BitConverter.ToInt32(receivedBytes, 0);
		if (messageType != MessageType.Client2ClientIamReadyToReceiveYourHolePunching)
		{
			throw new ArgumentException("接收到的 已准备好对方连接我(抛送橄榄枝) 消息类型不正确");
		}

		var guid = new Guid(receivedBytes.Skip(4).Take(16).ToArray());
		var senderId = new Guid(receivedBytes.Skip(20).Take(16).ToArray());
		var sendTime = new DateTime(BitConverter.ToInt64(receivedBytes, 36));
		var senderEndPointFromStunWhenSendingThisMessage =
			new IPEndPoint(new IPAddress(receivedBytes.Skip(44).Take(4).ToArray()),
				BitConverter.ToInt32(receivedBytes, 48));
		var senderEndPointFromReceiverWhenReceiveThisMessage =
			new IPEndPoint(new IPAddress(receivedBytes.Skip(52).Take(4).ToArray()),
				BitConverter.ToInt32(receivedBytes, 56));
		var message =
			new Client2ClientIamReadyToReceiveYourHolePunchingMessage(senderId,
				senderEndPointFromStunWhenSendingThisMessage)
			{
				Id = guid,
				SendTime = sendTime,
				SenderEndPointFromReceiverWhenReceiveThisMessage = senderEndPointFromReceiverWhenReceiveThisMessage
			};
		return message;
	}
}