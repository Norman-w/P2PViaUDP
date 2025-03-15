using System.Text;

namespace P2PViaUDP.Model.Client;

public class P2PHeartbeatMessage
{
	public P2PHeartbeatMessage(Guid senderId, string additionalMessage)
	{
		Id = Guid.NewGuid();
		SenderId = senderId;
		SendTime = DateTime.Now;
		AdditionalMessage = additionalMessage;
	}
	public static MessageType MessageType => MessageType.P2PHeartbeat;
	private static uint DefaultMessageLength => 
	4 + // MessageType
	16 + // Guid
	16; // Guid
	//= 36
	/// <summary>
	/// 消息的唯一标识
	/// </summary>
	public Guid Id { get; private set; }
	/// <summary>
	/// 发送者的Guid
	/// </summary>
	public Guid SenderId { get; private init; }
	public DateTime SendTime { get; private set; }
	public string AdditionalMessage { get; private set; }
	public uint MessageContentLength => (uint)Encoding.UTF8.GetByteCount(AdditionalMessage);
	
	public byte[] ToBytes()
	{
		var bytesList = new List<byte>();
		bytesList.AddRange(BitConverter.GetBytes((int)MessageType));
		bytesList.AddRange(Id.ToByteArray());
		bytesList.AddRange(SenderId.ToByteArray());
		bytesList.AddRange(BitConverter.GetBytes(SendTime.Ticks));
		bytesList.AddRange(BitConverter.GetBytes(MessageContentLength));
		bytesList.AddRange(Encoding.UTF8.GetBytes(AdditionalMessage));
		
		var bytes = bytesList.ToArray();
		return bytes;
	}
	
	public static P2PHeartbeatMessage FromBytes(byte[] receivedBytes)
	{
		if (receivedBytes.Length < DefaultMessageLength)
		{
			throw new ArgumentException("接收到的P2P心跳消息字节数组长度不正确");
		}
		var messageType = (MessageType)BitConverter.ToInt32(receivedBytes, 0);
		if (messageType != MessageType.P2PHeartbeat)
		{
			throw new ArgumentException("接收到的P2P心跳消息类型不正确");
		}
		var guid = new Guid(receivedBytes.Skip(4).Take(16).ToArray());
		var senderId = new Guid(receivedBytes.Skip(20).Take(16).ToArray());
		var sendTime = new DateTime(BitConverter.ToInt64(receivedBytes, 36));
		var messageContentLength = BitConverter.ToUInt32(receivedBytes, 44);
		var additionalMessage = Encoding.UTF8.GetString(receivedBytes, 48, (int)messageContentLength);
		var message = new P2PHeartbeatMessage(senderId, additionalMessage)
		{
			Id = guid,
			SendTime = sendTime,
			SenderId = senderId
		};
		return message;
	}
}